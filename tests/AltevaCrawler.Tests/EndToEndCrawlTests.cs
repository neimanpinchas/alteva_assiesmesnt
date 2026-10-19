namespace AltevaCrawler.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AltevaCrawler.Api.Consumers;
using AltevaCrawler.Api.Controllers;
using AltevaCrawler.Shared.Data;
using AltevaCrawler.Shared.Entities;
using AltevaCrawler.Shared.Enums;
using AltevaCrawler.Shared.Models;
using AltevaCrawler.Shared.Storage;
using AltevaCrawler.Shared.Utilities;
using AltevaCrawler.Worker.Consumers;
using AltevaCrawler.Worker.Services;

/// <summary>A tiny local website the crawler can really download from.</summary>
internal sealed class LocalSite : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Task _loop;

    public string BaseUrl { get; }

    public LocalSite(IReadOnlyDictionary<string, (int Status, string ContentType, string Body)> routes)
    {
        int port = new Random().Next(20000, 40000);
        BaseUrl = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(BaseUrl);
        _listener.Start();
        _loop = Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { return; }

                var (status, type, body) = routes.TryGetValue(ctx.Request.Url!.AbsolutePath, out var r) ? r : (404, "text/plain", "nope");
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = type;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.Close();
            }
        });
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
    }
}

/// <summary>
/// Whole pipeline in one process: real JobsController, real MassTransit in-memory bus, real Worker consumer + engine,
/// real API consumers, real HTTP against a local site, real MySQL (scratch database). Only RabbitMQ is swapped for the in-memory bus.
/// Runs only when ALTEVA_TEST_MYSQL points at a server, e.g. "Server=127.0.0.1;Port=3307;Uid=root;Pwd=...;" (see compose file).
/// </summary>
public class EndToEndCrawlTests
{
    private static readonly Dictionary<string, (int, string, string)> SiteRoutes = new()
    {
        ["/"] = (200, "text/html", "<html><body>home <a href='/a'>a</a> <a href='/b'>b</a> <a href='/a'>dup</a> <a href='/missing'>x</a> <a href='/data.json'>j</a> <a href='mailto:x@y.z'>m</a></body></html>"),
        ["/a"] = (200, "text/html", "<html><body>page a <a href='/b'>b</a> <a href='/c'>c</a></body></html>"),
        ["/b"] = (200, "text/html", "<html><body>page b <a href='/'>home</a></body></html>"),
        ["/c"] = (200, "text/html", "<html><body>page c leaf</body></html>"),
        ["/data.json"] = (200, "application/json", "{}")
    };

    private sealed record Pipeline(ServiceProvider Provider, ITestHarness Harness, AppDbContext Admin) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Harness.Stop();
            await Provider.DisposeAsync();
            await Admin.Database.EnsureDeletedAsync();
            await Admin.DisposeAsync();
        }

        public async Task<JobSummaryDto> RunJobAsync(string url, int maxDepth, int maxPages, string expectedStatus = "Completed")
        {
            await using var scope = Provider.CreateAsyncScope();
            var controller = ActivatorUtilities.CreateInstance<JobsController>(scope.ServiceProvider);
            var created = Assert.IsType<OkObjectResult>(await controller.CreateJob(new CreateJobRequest(url, maxDepth, maxPages)));
            string jobId = (string)created.Value!.GetType().GetProperty("jobId")!.GetValue(created.Value)!;

            await WaitForStatusAsync(jobId, expectedStatus);
            await Harness.InactivityTask;

            await using var readScope = Provider.CreateAsyncScope();
            var reader = ActivatorUtilities.CreateInstance<JobsController>(readScope.ServiceProvider);
            return (JobSummaryDto)Assert.IsType<OkObjectResult>(await reader.GetJob(jobId)).Value!;
        }

        public async Task<List<TreeNodeDto>> TreeAsync(string jobId)
        {
            await using var scope = Provider.CreateAsyncScope();
            var controller = ActivatorUtilities.CreateInstance<JobsController>(scope.ServiceProvider);
            return (List<TreeNodeDto>)Assert.IsType<OkObjectResult>(await controller.GetJobTree(jobId, null, 5)).Value!;
        }

        public async Task<AppDbContext> DbAsync() => Provider.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

        private async Task WaitForStatusAsync(string jobId, string status)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                await using var scope = Provider.CreateAsyncScope();
                var job = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Jobs.FirstAsync(j => j.Id == jobId);
                if (job.Status.ToString() == status) return;
                await Task.Delay(100);
            }
            await using var last = Provider.CreateAsyncScope();
            var final = await last.ServiceProvider.GetRequiredService<AppDbContext>().Jobs.FirstAsync(j => j.Id == jobId);
            throw new TimeoutException($"Job {jobId} never reached {status}; it is {final.Status} (crawled {final.TotalPagesCrawled}, reason '{final.FailureReason}').");
        }
    }

    private const string ServerEnvVar = "ALTEVA_TEST_MYSQL";

    private static async Task<Pipeline?> StartPipelineAsync()
    {
        string? server = Environment.GetEnvironmentVariable(ServerEnvVar);
        if (string.IsNullOrWhiteSpace(server)) return null;

        string dbName = "alteva_e2e_" + Guid.NewGuid().ToString("N")[..12];
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(server.TrimEnd(';') + $";Database={dbName};", ServerVersion.Create(8, 0, 36, Pomelo.EntityFrameworkCore.MySql.Infrastructure.ServerType.MySql))
            .Options;
        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        services.AddScoped<AppDbContext>(_ => new AppDbContext(options));
        services.AddScoped<AltevaCrawler.Api.Services.PageGraphWriter>();
        services.AddScoped<AltevaCrawler.Api.Services.PageGraphWriter>();
        services.AddSingleton<IStorageService>(new MockS3StorageService("e2e"));
        services.AddHttpClient("CrawlClient");
        services.AddSingleton<CrawlerEngine>(sp => new CrawlerEngine(
            sp.GetRequiredService<System.Net.Http.IHttpClientFactory>(), sp.GetRequiredService<IBus>(),
            sp.GetRequiredService<IStorageService>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CrawlerEngine>>()));
        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<CrawlJobConsumer>();
            x.AddConsumer<ApiPageCrawledConsumer>();
            x.AddConsumer<ApiLinkDiscoveredConsumer>();
            x.AddConsumer<ApiJobProgressConsumer>();
        });

        var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return new Pipeline(provider, harness, new AppDbContext(options));
    }

    private static List<string> Flatten(IEnumerable<TreeNodeDto> nodes) =>
        nodes.SelectMany(n => new[] { n.Url }.Concat(Flatten(n.Children))).ToList();

    [Fact]
    public async Task FullCrawl_StoresPagesLinksAndExposesTheTree()
    {
        using var site = new LocalSite(SiteRoutes);
        await using var pipeline = await StartPipelineAsync();
        if (pipeline == null) return;

        var job = await pipeline.RunJobAsync(site.BaseUrl, maxDepth: 3, maxPages: 50);

        // 4 html pages crawled; /missing (404) and /data.json (non-HTML) are errors, not crawled pages.
        Assert.Equal(4, job.TotalPagesCrawled);
        Assert.Equal("Completed", job.Status);

        var tree = await pipeline.TreeAsync(job.Id);
        var root = Assert.Single(tree);
        Assert.Equal(site.BaseUrl, root.Url);

        var urls = Flatten(tree);
        foreach (string path in new[] { "a", "b", "c" })
        {
            Assert.Contains(site.BaseUrl + path, urls);
        }
        // Root links: /a /b /missing /data.json (duplicate /a and mailto collapsed).
        Assert.Equal(4, root.Children.Count);
        Assert.Contains(root.Children, c => c.Url.EndsWith("/missing") && c.ErrorKind == CrawlErrorClassifier.Http);
        Assert.Contains(root.Children, c => c.Url.EndsWith("/data.json") && c.ErrorKind == CrawlErrorClassifier.NonHtml);
    }

    [Fact]
    public async Task MaxPagesLimit_IsStrictEndToEnd()
    {
        using var site = new LocalSite(SiteRoutes);
        await using var pipeline = await StartPipelineAsync();
        if (pipeline == null) return;

        var job = await pipeline.RunJobAsync(site.BaseUrl, maxDepth: 3, maxPages: 2);

        Assert.Equal(2, job.TotalPagesCrawled);
        await using var db = await pipeline.DbAsync();
        Assert.Equal(2, await db.Pages.CountAsync(p => p.IsCrawled && p.ByteSize > 0));
    }

    [Fact]
    public async Task MaxDepthZero_CrawlsOnlyTheStartPage()
    {
        using var site = new LocalSite(SiteRoutes);
        await using var pipeline = await StartPipelineAsync();
        if (pipeline == null) return;

        var job = await pipeline.RunJobAsync(site.BaseUrl, maxDepth: 0, maxPages: 50);

        Assert.Equal(1, job.TotalPagesCrawled);
        var root = Assert.Single(await pipeline.TreeAsync(job.Id));
        Assert.Empty(root.Children);
    }

    [Fact]
    public async Task UnreachableStartPage_FailsJobWithReasonAndShowsErrorOnPage()
    {
        await using var pipeline = await StartPipelineAsync();
        if (pipeline == null) return;

        var job = await pipeline.RunJobAsync("http://127.0.0.1:1/", maxDepth: 2, maxPages: 10, expectedStatus: "Failed");

        Assert.Equal("Failed", job.Status);
        Assert.StartsWith("Start page failed:", job.FailureReason);
        Assert.Equal(0, job.TotalPagesCrawled);
    }
}
