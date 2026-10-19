namespace AltevaCrawler.Worker.Consumers;

using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.Logging;
using AltevaCrawler.Shared.Events;
using AltevaCrawler.Worker.Services;

public class PauseJobConsumer : IConsumer<PauseJobRequestedEvent>
{
    private readonly CrawlerEngine _crawlerEngine;
    private readonly ILogger<PauseJobConsumer> _logger;

    public PauseJobConsumer(CrawlerEngine crawlerEngine, ILogger<PauseJobConsumer> logger)
    {
        _crawlerEngine = crawlerEngine;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<PauseJobRequestedEvent> context)
    {
        var msg = context.Message;
        _logger.LogInformation("Received PauseJobRequestedEvent for JobId: {JobId}", msg.JobId);
        _crawlerEngine.PauseJob(msg.JobId);
        return Task.CompletedTask;
    }
}
