namespace AltevaCrawler.Worker.Consumers;

using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.Logging;
using AltevaCrawler.Shared.Events;
using AltevaCrawler.Worker.Services;

public class CancelJobConsumer : IConsumer<CancelJobRequestedEvent>
{
    private readonly CrawlerEngine _crawlerEngine;
    private readonly ILogger<CancelJobConsumer> _logger;

    public CancelJobConsumer(CrawlerEngine crawlerEngine, ILogger<CancelJobConsumer> logger)
    {
        _crawlerEngine = crawlerEngine;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<CancelJobRequestedEvent> context)
    {
        var msg = context.Message;
        _logger.LogInformation("Received CancelJobRequestedEvent for JobId: {JobId}", msg.JobId);
        _crawlerEngine.CancelJob(msg.JobId);
        return Task.CompletedTask;
    }
}
