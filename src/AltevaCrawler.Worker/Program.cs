using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using AltevaCrawler.Shared.Storage;
using AltevaCrawler.Shared.Utilities;
using AltevaCrawler.Worker.Consumers;
using AltevaCrawler.Worker.Services;

var builder = WebApplication.CreateBuilder(args);

string rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
string rabbitUser = builder.Configuration["RabbitMQ:Username"] ?? "guest";
string rabbitPass = builder.Configuration["RabbitMQ:Password"] ?? "guest";

bool allowInvalidCerts = builder.Configuration.GetValue<bool>("Crawler:AllowInvalidCertificates");

// The worker has no database access by design: everything it learns is published as events and the API
// persists them. A MySQL outage therefore cannot stop crawling or crash the worker.

builder.Services.AddSingleton<IStorageService, LocalStorageService>();

builder.Services.AddHttpClient("CrawlClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    var handler = new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All
    };
    if (allowInvalidCerts)
    {
        handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
    }
    return handler;
})
// Transient = 5xx, 408, timeouts and network errors. Certificate/TLS failures are permanent, so they are not retried
// and surface immediately as an "ssl" error on the page.
.AddPolicyHandler(Policy<HttpResponseMessage>
    .Handle<HttpRequestException>(ex => !CrawlErrorClassifier.IsTlsError(ex))
    .Or<Polly.Timeout.TimeoutRejectedException>()
    .OrResult(r => (int)r.StatusCode >= 500 || r.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
    .WaitAndRetryAsync(2, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

// The engine is a singleton (it owns the per-job cancel/pause state that the control consumers act on), so it
// must not capture the scoped IPublishEndpoint. IBus is the singleton bus-level publisher.
builder.Services.AddSingleton<CrawlerEngine>(sp => new CrawlerEngine(
    sp.GetRequiredService<IHttpClientFactory>(),
    sp.GetRequiredService<IBus>(),
    sp.GetRequiredService<IStorageService>(),
    sp.GetRequiredService<ILogger<CrawlerEngine>>()));

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<CrawlJobConsumer>();
    x.AddConsumer<CancelJobConsumer>();
    x.AddConsumer<PauseJobConsumer>();
    x.AddConsumer<ResumeJobConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitHost, "/", h =>
        {
            h.Username(rabbitUser);
            h.Password(rabbitPass);
        });

        cfg.ReceiveEndpoint("crawl-job-queue", e =>
        {
            e.ConfigureConsumer<CrawlJobConsumer>(context);
            e.ConfigureConsumer<CancelJobConsumer>(context);
            e.ConfigureConsumer<PauseJobConsumer>(context);
            e.ConfigureConsumer<ResumeJobConsumer>(context);
            e.UseMessageRetry(r => r.Interval(2, TimeSpan.FromSeconds(2)));
        });
    });
});

builder.Services.AddHealthChecks()
    .AddCheck<MassTransitBusHealthCheck>("masstransit_bus");

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run("http://0.0.0.0:5001");

public class MassTransitBusHealthCheck : IHealthCheck
{
    private readonly IBusControl _busControl;

    public MassTransitBusHealthCheck(IBusControl busControl)
    {
        _busControl = busControl;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var health = _busControl.CheckHealth();
        if (health.Status.ToString() == "Healthy")
        {
            return Task.FromResult(HealthCheckResult.Healthy("MassTransit bus is active and healthy."));
        }
        else if (health.Status.ToString() == "Degraded")
        {
            return Task.FromResult(HealthCheckResult.Degraded("MassTransit bus is degraded."));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy("MassTransit bus is unhealthy."));
    }
}