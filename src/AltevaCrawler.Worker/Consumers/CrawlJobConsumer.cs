namespace AltevaCrawler.Worker.Consumers;

using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.Logging;
using AltevaCrawler.Shared.Events;
using AltevaCrawler.Worker.Services;

public class CrawlJobConsumer : IConsumer<CrawlJobRequestedEvent>
{
    private readonly CrawlerEngine _crawlerEngine;
    private readonly ILogger<CrawlJobConsumer> _logger;

    public CrawlJobConsumer(CrawlerEngine crawlerEngine, ILogger<CrawlJobConsumer> logger)
    {
        _crawlerEngine = crawlerEngine;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CrawlJobRequestedEvent> context)
    {
        var msg = context.Message;
        _logger.LogInformation("Received CrawlJobRequestedEvent for JobId: {JobId}, Url: {Url}", msg.JobId, msg.Url);

        await _crawlerEngine.ProcessJobAsync(msg.JobId, msg.Url, msg.MaxDepth, msg.MaxPagesLimit, context.CancellationToken);
    }
}
