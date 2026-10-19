namespace AltevaCrawler.Worker.Consumers;

using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.Logging;
using AltevaCrawler.Shared.Events;
using AltevaCrawler.Worker.Services;

public class ResumeJobConsumer : IConsumer<ResumeJobRequestedEvent>
{
    private readonly CrawlerEngine _crawlerEngine;
    private readonly ILogger<ResumeJobConsumer> _logger;

    public ResumeJobConsumer(CrawlerEngine crawlerEngine, ILogger<ResumeJobConsumer> logger)
    {
        _crawlerEngine = crawlerEngine;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<ResumeJobRequestedEvent> context)
    {
        var msg = context.Message;
        _logger.LogInformation("Received ResumeJobRequestedEvent for JobId: {JobId}", msg.JobId);
        _crawlerEngine.ResumeJob(msg.JobId);
        return Task.CompletedTask;
    }
}
