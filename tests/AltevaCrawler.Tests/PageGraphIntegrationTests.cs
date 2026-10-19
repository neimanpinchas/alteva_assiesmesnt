namespace AltevaCrawler.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;
using AltevaCrawler.Api.Services;
using AltevaCrawler.Shared.Data;
using AltevaCrawler.Shared.Entities;

/// <summary>
/// Real-MySQL checks for the page graph (run only when ALTEVA_TEST_MYSQL is set; a scratch database is created and dropped).
/// They cover the "tree shows only one file" bug: edges lost when a link event is processed before its source page exists,
/// and duplicate page/edge rows created by concurrent check-then-insert.
/// </summary>
public class PageGraphIntegrationTests
{
    private const string ServerEnvVar = "ALTEVA_TEST_MYSQL";

    private static DbContextOptions<AppDbContext> Options(string cs) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(cs, ServerVersion.Create(8, 0, 36, Pomelo.EntityFrameworkCore.MySql.Infrastructure.ServerType.MySql))
            .Options;

    private static async Task WithScratchDbAsync(Func<string, Task> body)
    {
        string? server = Environment.GetEnvironmentVariable(ServerEnvVar);
        if (string.IsNullOrWhiteSpace(server)) return;

        string dbName = "alteva_it_" + Guid.NewGuid().ToString("N")[..12];
        await using var admin = new AppDbContext(Options(server));
        await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE `{dbName}`");
        try
        {
            await body(server.TrimEnd(';') + $";Database={dbName};");
        }
        finally
        {
            await admin.Database.ExecuteSqlRawAsync($"DROP DATABASE IF EXISTS `{dbName}`");
        }
    }

    private static async Task SeedJobAsync(AppDbContext db, string jobId, string url = "https://root.example/")
    {
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO `Jobs` (`Id`,`Url`,`MaxDepth`,`MaxPagesLimit`,`status`,`TotalPagesCrawled`,`TotalBytesDownloaded`,`CreatedAt`)
            VALUES ({jobId},{url},2,200,'R',0,0,UTC_TIMESTAMP())");
    }

    [Fact]
    public async Task ConcurrentGetOrCreate_ForTheSameUrl_YieldsExactlyOneRow()
    {
        await WithScratchDbAsync(async cs =>
        {
            await using (var setup = new AppDbContext(Options(cs))) await setup.Database.EnsureCreatedAsync();

            const string url = "https://race.example/page";
            var ids = await Task.WhenAll(Enumerable.Range(0, 24).Select(async _ =>
            {
                await using var db = new AppDbContext(Options(cs));
                return (await new PageGraphWriter(db).GetOrCreatePageAsync(url)).Id;
            }));

            Assert.Single(ids.Distinct());
            await using var check = new AppDbContext(Options(cs));
            Assert.Equal(1, await check.Pages.CountAsync(p => p.NormalizedUrl == url));
        });
    }

    [Fact]
    public async Task LinkProcessedBeforeItsSourcePageEvent_KeepsTheEdgeAndLaterPageEventReusesTheRow()
    {
        await WithScratchDbAsync(async cs =>
        {
            string jobId = Guid.NewGuid().ToString();
            await using (var setup = new AppDbContext(Options(cs)))
            {
                await setup.Database.EnsureCreatedAsync();
                await SeedJobAsync(setup, jobId);
            }

            long sourceIdFromLinkEvent, targetId;
            await using (var db = new AppDbContext(Options(cs)))
            {
                var graph = new PageGraphWriter(db);
                // LinkDiscoveredEvent arrives first: the root page has not been recorded yet.
                var source = await graph.GetOrCreatePageAsync("https://root.example/");
                var target = await graph.GetOrCreatePageAsync("https://root.example/about");
                Assert.True(await graph.TryAddLinkAsync(jobId, source.Id, target.Id));
                sourceIdFromLinkEvent = source.Id;
                targetId = target.Id;
            }

            await using (var db = new AppDbContext(Options(cs)))
            {
                // PageCrawledEvent for the root arrives later and must reuse the same row (so the edge stays attached).
                var graph = new PageGraphWriter(db);
                var root = await graph.GetOrCreatePageAsync("https://root.example/");
                root.IsCrawled = true;
                root.StatusCode = 200;
                await db.SaveChangesAsync();
                Assert.Equal(sourceIdFromLinkEvent, root.Id);
            }

            await using var check = new AppDbContext(Options(cs));
            var edges = await check.JobPageLinks.Where(l => l.JobId == jobId).ToListAsync();
            Assert.Single(edges);
            Assert.Equal(sourceIdFromLinkEvent, edges[0].SourcePageId);
            Assert.Equal(targetId, edges[0].TargetPageId);
        });
    }

    [Fact]
    public async Task DuplicateEdges_AreIgnored_EvenWhenInsertedConcurrently()
    {
        await WithScratchDbAsync(async cs =>
        {
            string jobId = Guid.NewGuid().ToString();
            long a, b;
            await using (var setup = new AppDbContext(Options(cs)))
            {
                await setup.Database.EnsureCreatedAsync();
                await SeedJobAsync(setup, jobId);
                var graph = new PageGraphWriter(setup);
                a = (await graph.GetOrCreatePageAsync("https://root.example/")).Id;
                b = (await graph.GetOrCreatePageAsync("https://root.example/b")).Id;
            }

            var created = await Task.WhenAll(Enumerable.Range(0, 16).Select(async _ =>
            {
                await using var db = new AppDbContext(Options(cs));
                return await new PageGraphWriter(db).TryAddLinkAsync(jobId, a, b);
            }));

            Assert.Equal(1, created.Count(c => c));
            await using var check = new AppDbContext(Options(cs));
            Assert.Equal(1, await check.JobPageLinks.CountAsync(l => l.JobId == jobId));
        });
    }

    [Fact]
    public async Task Upgrader_MergesLegacyDuplicatePagesAndEdges_ThenEnforcesUniqueKeys()
    {
        await WithScratchDbAsync(async cs =>
        {
            string jobId = Guid.NewGuid().ToString();
            await using var db = new AppDbContext(Options(cs));
            await db.Database.EnsureCreatedAsync();
            await SeedJobAsync(db, jobId);

            // Emulate a database written by the old consumers: no unique keys, duplicate rows and edges.
            await db.Database.ExecuteSqlRawAsync("DROP INDEX `UX_Pages_UrlHash` ON `Pages`");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `Pages` DROP COLUMN `UrlHash`");
            await db.Database.ExecuteSqlRawAsync("DROP INDEX `UX_JobPageLinks_Edge` ON `JobPageLinks`");

            async Task<long> InsertPage(string url, bool crawled)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO `Pages`
                    (`Url`,`NormalizedUrl`,`DomainHash`,`Domain`,`SimHash`,`ByteSize`,`StatusCode`,`OutgoingLinksCount`,`IsCrawled`,`CrawledAt`)
                    VALUES ({url},{url},1,'dup.example',0,0,{(crawled ? 200 : 0)},0,{crawled},{(crawled ? DateTime.UtcNow : (DateTime?)null)})");
                return await db.Pages.AsNoTracking().Where(p => p.NormalizedUrl == url).OrderByDescending(p => p.Id)
                    .Select(p => p.Id).FirstAsync();
            }
            Task AddEdge(long s, long t) => db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO `JobPageLinks` (`JobId`,`SourcePageId`,`TargetPageId`,`CreatedAt`) VALUES ({jobId},{s},{t},UTC_TIMESTAMP())");

            long root = await InsertPage("https://dup.example/", crawled: true);
            long uncrawledDup = await InsertPage("https://dup.example/a", crawled: false);
            long crawledDup = await InsertPage("https://dup.example/a", crawled: true);
            await AddEdge(root, uncrawledDup);
            await AddEdge(root, crawledDup);
            await AddEdge(root, crawledDup);

            await SchemaUpgrader.ApplyAsync(db);
            await SchemaUpgrader.ApplyAsync(db); // idempotent

            var pages = await db.Pages.AsNoTracking().Where(p => p.NormalizedUrl == "https://dup.example/a").ToListAsync();
            Assert.Single(pages);
            Assert.True(pages[0].IsCrawled);
            Assert.Equal(crawledDup, pages[0].Id);

            var edges = await db.JobPageLinks.AsNoTracking().Where(l => l.JobId == jobId).ToListAsync();
            Assert.Single(edges);
            Assert.Equal(root, edges[0].SourcePageId);
            Assert.Equal(crawledDup, edges[0].TargetPageId);

            // The unique keys are now enforced by the database.
            await Assert.ThrowsAnyAsync<Exception>(() => InsertPage("https://dup.example/a", crawled: true));
        });
    }
}
