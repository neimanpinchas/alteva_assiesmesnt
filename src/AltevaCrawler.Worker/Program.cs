using System;
using System.Net;
using System.Net.Http;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using Polly.Extensions.Http;
using AltevaCrawler.Shared.Data;
using AltevaCrawler.Worker.Consumers;
using AltevaCrawler.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

string connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=localhost;Port=3306;Database=alteva_crawler;Uid=root;Pwd=rootpassword;";

string rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
string rabbitUser = builder.Configuration["RabbitMQ:Username"] ?? "guest";
string rabbitPass = builder.Configuration["RabbitMQ:Password"] ?? "guest";

builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});

builder.Services.AddHttpClient("CrawlClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = DecompressionMethods.All,
    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
})
.AddPolicyHandler(HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(2, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

builder.Services.AddScoped<CrawlerEngine>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<CrawlJobConsumer>();

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
            e.UseMessageRetry(r => r.Interval(2, TimeSpan.FromSeconds(2)));
        });
    });
});

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var dbContext = dbFactory.CreateDbContext();
    dbContext.Database.EnsureCreated();
}

host.Run();
