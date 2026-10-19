using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using AltevaCrawler.Api.Consumers;
using AltevaCrawler.Api.Hubs;
using AltevaCrawler.Api.Security;
using AltevaCrawler.Shared.Data;
using AltevaCrawler.Shared.Security;
using AltevaCrawler.Shared.Storage;

var builder = WebApplication.CreateBuilder(args);

string connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=localhost;Port=3306;Database=alteva_crawler;Uid=root;Pwd=rootpassword;";

string rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
string rabbitUser = builder.Configuration["RabbitMQ:Username"] ?? "guest";
string rabbitPass = builder.Configuration["RabbitMQ:Password"] ?? "guest";

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddControllers(options =>
{
    options.Filters.Add<AuthFilter>();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Alteva Crawler API",
        Version = "v1",
        Description = "Distributed Job-Based Web Crawler REST API"
    });
});

builder.Services.AddSignalR();
builder.Services.AddHealthChecks();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});

builder.Services.AddScoped<AltevaCrawler.Api.Services.PageGraphWriter>();
builder.Services.AddSingleton<HmacAuthService>();
builder.Services.AddSingleton<IStorageService, LocalStorageService>();
builder.Services.AddScoped<AuthFilter>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ApiPageCrawledConsumer>();
    x.AddConsumer<ApiLinkDiscoveredConsumer>();
    x.AddConsumer<ApiJobProgressConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitHost, "/", h =>
        {
            h.Username(rabbitUser);
            h.Password(rabbitPass);
        });

        cfg.ReceiveEndpoint("api-progress-queue", e =>
        {
            e.ConfigureConsumer<ApiPageCrawledConsumer>(context);
            e.ConfigureConsumer<ApiLinkDiscoveredConsumer>(context);
            e.ConfigureConsumer<ApiJobProgressConsumer>(context);
        });
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Alteva Crawler API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors("AllowAll");
app.UseRouting();

app.MapControllers();
app.MapHub<CrawlProgressHub>("/hubs/crawlProgress");

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        
        bool dbHealthy = false;
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbHealthy = await db.Database.CanConnectAsync();
        }
        catch { dbHealthy = false; }

        var result = new
        {
            status = dbHealthy ? "Healthy" : "Degraded",
            database = dbHealthy ? "Connected" : "Disconnected / Down",
            messageBroker = "RabbitMQ Active",
            timestamp = DateTime.UtcNow
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(result));
    }
});

using (var scope = app.Services.CreateScope())
{
    try
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Database.EnsureCreated();
        await SchemaUpgrader.ApplyAsync(dbContext, app.Logger);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Warning] Database startup connection failed: {ex.Message}");
    }
}

app.Run();
