namespace AltevaCrawler.Api.Consumers;

using System;
using System.Linq;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using AltevaCrawler.Api.Hubs;
using AltevaCrawler.Shared.Data;
using AltevaCrawler.Shared.Entities;
using AltevaCrawler.Shared.Enums;
using AltevaCrawler.Shared.Events;
using AltevaCrawler.Shared.Utilities;

public class ApiPageCrawledConsumer : IConsumer<PageCrawledEvent>
{
    private readonly AppDbContext _dbContext;
    private readonly IHubContext<CrawlProgressHub> _hubContext;

    public ApiPageCrawledConsumer(AppDbContext dbContext, IHubContext<CrawlProgressHub> hubContext)
    {
        _dbContext = dbContext;
        _hubContext = hubContext;
    }

    public async Task Consume(ConsumeContext<PageCrawledEvent> context)
    {
        var msg = context.Message;

        // Check if job exists in database before processing
        var dbJob = await _dbContext.Jobs.FirstOrDefaultAsync(j => j.Id == msg.JobId);
        if (dbJob == null)
        {
            return; // Skip if job was deleted/pruned
        }

        uint domainHash = UrlNormalizer.ComputeDomainHash(UrlNormalizer.ExtractDomain(msg.Url));

        var page = await _dbContext.Pages.FirstOrDefaultAsync(p => p.UrlCrc32 == msg.UrlCrc32 && p.NormalizedUrl == msg.Url);
        if (page == null)
        {
            page = new Page
            {
                Url = msg.Url,
                NormalizedUrl = msg.Url,
                UrlCrc32 = msg.UrlCrc32,
                Domain = UrlNormalizer.ExtractDomain(msg.Url),
                DomainHash = domainHash,
                SimHash = msg.SimHash,
                ContentHash = msg.ContentHash,
                StoragePath = msg.StoragePath,
                StatusCode = msg.StatusCode,
                ByteSize = msg.ByteSize,
                OutgoingLinksCount = msg.OutgoingLinksCount,
                IsCrawled = true,
                CrawledAt = msg.CrawledAt
            };
            _dbContext.Pages.Add(page);
        }
        else
        {
            page.SimHash = msg.SimHash;
            page.ContentHash = msg.ContentHash;
            page.StoragePath = msg.StoragePath;
            page.StatusCode = msg.StatusCode;
            page.ByteSize = msg.ByteSize;
            page.OutgoingLinksCount = msg.OutgoingLinksCount;
            page.IsCrawled = true;
            page.CrawledAt = msg.CrawledAt;
        }

        dbJob.TotalPagesCrawled++;
        dbJob.TotalBytesDownloaded += msg.ByteSize;
        if (dbJob.Status == JobStatus.Pending)
        {
            dbJob.Status = JobStatus.Running;
            dbJob.StartedAt = DateTime.UtcNow;
        }

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Safely ignore foreign key race conditions
        }

        await _hubContext.Clients.Group($"job-{msg.JobId}")
            .SendAsync("PageCrawled", msg);
    }
}

public class ApiLinkDiscoveredConsumer : IConsumer<LinkDiscoveredEvent>
{
    private readonly AppDbContext _dbContext;
    private readonly IHubContext<CrawlProgressHub> _hubContext;

    public ApiLinkDiscoveredConsumer(AppDbContext dbContext, IHubContext<CrawlProgressHub> hubContext)
    {
        _dbContext = dbContext;
        _hubContext = hubContext;
    }

    public async Task Consume(ConsumeContext<LinkDiscoveredEvent> context)
    {
        var msg = context.Message;

        // Check if job exists before creating foreign key link
        bool jobExists = await _dbContext.Jobs.AnyAsync(j => j.Id == msg.JobId);
        if (!jobExists)
        {
            return; // Skip if job was deleted/pruned
        }

        uint sourceCrc = UrlNormalizer.ComputeCrc32(msg.SourceUrl);

        var sourcePage = await _dbContext.Pages.FirstOrDefaultAsync(p => p.UrlCrc32 == sourceCrc && p.NormalizedUrl == msg.SourceUrl);
        var targetPage = await _dbContext.Pages.FirstOrDefaultAsync(p => p.UrlCrc32 == msg.TargetUrlCrc32 && p.NormalizedUrl == msg.TargetUrl);

        if (targetPage == null)
        {
            string targetDomain = UrlNormalizer.ExtractDomain(msg.TargetUrl);
            targetPage = new Page
            {
                Url = msg.TargetUrl,
                NormalizedUrl = msg.TargetUrl,
                UrlCrc32 = msg.TargetUrlCrc32,
                Domain = targetDomain,
                DomainHash = UrlNormalizer.ComputeDomainHash(targetDomain),
                IsCrawled = false
            };
            _dbContext.Pages.Add(targetPage);
            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException) { }
        }

        if (sourcePage != null)
        {
            bool linkExists = await _dbContext.JobPageLinks.AnyAsync(l =>
                l.JobId == msg.JobId && l.SourcePageId == sourcePage.Id && l.TargetPageId == targetPage.Id);

            if (!linkExists)
            {
                _dbContext.JobPageLinks.Add(new JobPageLink
                {
                    JobId = msg.JobId,
                    SourcePageId = sourcePage.Id,
                    TargetPageId = targetPage.Id,
                    CreatedAt = DateTime.UtcNow
                });
                try
                {
                    await _dbContext.SaveChangesAsync();
                }
                catch (DbUpdateException) { }
            }
        }

        await _hubContext.Clients.Group($"job-{msg.JobId}")
            .SendAsync("LinkDiscovered", msg);
    }
}

public class ApiJobProgressConsumer : IConsumer<JobProgressUpdatedEvent>
{
    private readonly AppDbContext _dbContext;
    private readonly IHubContext<CrawlProgressHub> _hubContext;

    public ApiJobProgressConsumer(AppDbContext dbContext, IHubContext<CrawlProgressHub> hubContext)
    {
        _dbContext = dbContext;
        _hubContext = hubContext;
    }

    public async Task Consume(ConsumeContext<JobProgressUpdatedEvent> context)
    {
        var msg = context.Message;
        var job = await _dbContext.Jobs.FirstOrDefaultAsync(j => j.Id == msg.JobId);
        if (job != null)
        {
            if (msg.Status == "Completed")
            {
                job.Status = JobStatus.Completed;
                job.CompletedAt = DateTime.UtcNow;
            }
            else if (msg.Status == "Failed")
            {
                job.Status = JobStatus.Failed;
            }
            else if (msg.Status == "Paused")
            {
                job.Status = JobStatus.Pending;
            }
            job.TotalPagesCrawled = msg.CrawledPagesCount;
            job.TotalBytesDownloaded = msg.TotalBytesDownloaded;
            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException) { }
        }

        await _hubContext.Clients.Group($"job-{msg.JobId}")
            .SendAsync("JobProgressUpdated", msg);
    }
}
