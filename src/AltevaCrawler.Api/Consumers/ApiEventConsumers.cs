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
                Domain = UrlNormalizer.ExtractDomain(msg.Url),
                DomainHash = domainHash,
                SimHash = msg.SimHash,
                ContentHash = msg.ContentHash,
                StoragePath = msg.StoragePath,
                StatusCode = msg.StatusCode,
                ErrorKind = msg.IsSuccess ? null : msg.ErrorKind,
                ErrorMessage = msg.IsSuccess ? null : msg.ErrorMessage,
                ByteSize = msg.ByteSize,
                OutgoingLinksCount = msg.OutgoingLinksCount,
                IsCrawled = true,
                CrawledAt = msg.CrawledAt
            };
            _dbContext.Pages.Add(page);
        }
        else
        {
            bool isFailure = !msg.IsSuccess;

            if (!isFailure || page.SimHash == 0)
            {
                page.SimHash = msg.SimHash;
            }
            if (!isFailure || string.IsNullOrEmpty(page.ContentHash))
            {
                page.ContentHash = msg.ContentHash;
            }
            if (!isFailure || string.IsNullOrEmpty(page.StoragePath))
            {
                page.StoragePath = msg.StoragePath;
            }
            if (!isFailure || page.ByteSize == 0)
            {
                page.ByteSize = msg.ByteSize;
            }

            page.StatusCode = msg.StatusCode;
            page.ErrorKind = msg.IsSuccess ? null : msg.ErrorKind;
            page.ErrorMessage = msg.IsSuccess ? null : msg.ErrorMessage;
            page.OutgoingLinksCount = msg.OutgoingLinksCount;
            page.IsCrawled = true;
            page.CrawledAt = msg.CrawledAt;
        }

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Safely ignore foreign key race conditions
        }

        await UpdateJobAfterPageAsync(dbJob, msg);

        await _hubContext.Clients.Group($"job-{msg.JobId}")
            .SendAsync("PageCrawled", msg);
    }

    /// <summary>
    /// Counts the page and moves Pending to Running. Saved separately from the page so a conflict never drops the page.
    /// </summary>
    private async Task UpdateJobAfterPageAsync(Job dbJob, PageCrawledEvent msg)
    {
        try
        {
            await _dbContext.Entry(dbJob).ReloadAsync();
        }
        catch (Exception)
        {
            // If job was deleted concurrently
            return;
        }

        if (dbJob.Status == JobStatus.Completed || dbJob.Status == JobStatus.Failed || dbJob.Status == JobStatus.Canceled)
        {
            return;
        }

        if (msg.IsSuccess)
        {
            dbJob.TotalPagesCrawled++;
            dbJob.TotalBytesDownloaded += msg.ByteSize;
        }
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
            // The job moved on concurrently (usually already Completed/Failed, whose final event carries the
            // authoritative totals). Dropping this stale update is correct; overwriting the newer status is not.
        }
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

    private const int MaxSaveAttempts = 3;

    public async Task Consume(ConsumeContext<JobProgressUpdatedEvent> context)
    {
        var msg = context.Message;
        await ApplyWithRetryAsync(msg);

        await _hubContext.Clients.Group($"job-{msg.JobId}")
            .SendAsync("JobProgressUpdated", msg);
    }

    /// <summary>
    /// Applies the event to a freshly loaded job. If another consumer changed the status in between (for example the
    /// first page flipping Pending to Running) the attempt is repeated on the new state, so a terminal status always wins.
    /// </summary>
    private async Task ApplyWithRetryAsync(JobProgressUpdatedEvent msg)
    {
        for (int attempt = 1; attempt <= MaxSaveAttempts; attempt++)
        {
            _dbContext.ChangeTracker.Clear();
            var job = await _dbContext.Jobs.FirstOrDefaultAsync(j => j.Id == msg.JobId);
            if (job == null) return;

            ApplyProgress(job, msg);
            try
            {
                await _dbContext.SaveChangesAsync();
                return;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Retry on the latest status.
            }
            catch (DbUpdateException)
            {
                return;
            }
        }
    }

    private void ApplyProgress(Job job, JobProgressUpdatedEvent msg)
    {
        if (msg.Status == "Completed")
        {
            job.Status = JobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
        }
        else if (msg.Status == "Failed")
        {
            MarkFailed(job, msg);
        }
        else if (msg.Status == "Paused")
        {
            job.Status = JobStatus.Paused;
        }
        job.TotalPagesCrawled = msg.CrawledPagesCount;
        job.TotalBytesDownloaded = msg.TotalBytesDownloaded;
    }

    private void MarkFailed(Job job, JobProgressUpdatedEvent msg)
    {
        char previousStatus = job.StatusChar;
        job.Status = JobStatus.Failed;
        job.CompletedAt = DateTime.UtcNow;
        string reason = string.IsNullOrWhiteSpace(msg.FailureReason) ? "Crawl failed." : msg.FailureReason;
        job.FailureReason = reason.Length > 512 ? reason[..512] : reason;
        _dbContext.JobStatusTransitions.Add(new JobStatusTransition
        {
            JobId = job.Id,
            FromStatus = previousStatus,
            ToStatus = JobStatus.Failed.ToChar(),
            TransitionedAt = DateTime.UtcNow,
            Reason = job.FailureReason.Length > 255 ? job.FailureReason[..255] : job.FailureReason
        });
    }
}
