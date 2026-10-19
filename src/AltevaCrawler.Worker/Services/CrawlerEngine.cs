namespace AltevaCrawler.Worker.Services;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.Logging;
using AltevaCrawler.Shared.Events;
using AltevaCrawler.Shared.Storage;
using AltevaCrawler.Shared.Utilities;

/// <summary>
/// Orchestrates one crawl job: schedules batches of page crawls and publishes the resulting events.
/// Fetching, HTML analysis, limit bookkeeping and the final job outcome live in
/// <see cref="PageFetcher"/>, <see cref="PageLinkExtractor"/>, <see cref="CrawlJobState"/> and <see cref="JobOutcomes"/>.
/// </summary>
public class CrawlerEngine
{
    private const int BatchSize = 5;
    private const int StorageAttempts = 2;
    private const int FailedFetchStatus = 500;
    private static readonly TimeSpan PausePollInterval = TimeSpan.FromSeconds(1);

    private readonly PageFetcher _fetcher;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IStorageService _storageService;
    private readonly ILogger<CrawlerEngine> _logger;

    private readonly ConcurrentDictionary<string, bool> _pausedJobs = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _jobCtsMap = new();

    public CrawlerEngine(
        IHttpClientFactory httpClientFactory,
        IPublishEndpoint publishEndpoint,
        IStorageService storageService,
        ILogger<CrawlerEngine> logger)
    {
        _fetcher = new PageFetcher(httpClientFactory.CreateClient("CrawlClient"));
        _publishEndpoint = publishEndpoint;
        _storageService = storageService;
        _logger = logger;
    }

    public void PauseJob(string jobId) => _pausedJobs[jobId] = true;
    public void ResumeJob(string jobId) => _pausedJobs.TryRemove(jobId, out _);

    public void CancelJob(string jobId)
    {
        if (_jobCtsMap.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
        }
    }

    public async Task ProcessJobAsync(string jobId, string startUrl, int maxDepth, int maxPagesLimit = 200, CancellationToken ct = default)
    {
        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _jobCtsMap[jobId] = jobCts;
        var state = new CrawlJobState(maxPagesLimit);

        _logger.LogInformation("Starting parallel crawl job {JobId} for {Url} (MaxPages: {Limit})", jobId, startUrl, maxPagesLimit);

        try
        {
            await RunJobAsync(new CrawlContext(jobId, maxDepth, maxPagesLimit, state, jobCts.Token), startUrl);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Crawl job {JobId} canceled.", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing job {JobId}", jobId);
            await PublishJobFailedAsync(jobId, JobOutcomes.Crashed(jobId, ex, state));
        }
        finally
        {
            _jobCtsMap.TryRemove(jobId, out _);
            _pausedJobs.TryRemove(jobId, out _);
        }
    }

    private async Task RunJobAsync(CrawlContext ctx, string startUrl)
    {
        string? normalizedStartUrl = UrlNormalizer.Normalize(startUrl);
        if (normalizedStartUrl == null)
        {
            await PublishTerminalAsync(ctx, JobOutcomes.InvalidStartUrl(ctx.JobId));
            return;
        }

        uint startCrc = UrlNormalizer.ComputeCrc32(normalizedStartUrl);
        ctx.State.TryMarkDiscovered(startCrc);
        ctx.Queue.Enqueue(new CrawlWorkItem(normalizedStartUrl, startCrc, 0));

        await RunCrawlLoopAsync(ctx);

        if (ctx.Token.IsCancellationRequested)
        {
            _logger.LogInformation("Crawl job {JobId} canceled.", ctx.JobId);
            return;
        }

        await PublishTerminalAsync(ctx, JobOutcomes.Final(ctx.JobId, ctx.State));
        _logger.LogInformation("Crawl loop finished for job {JobId}. Crawled: {Count}/{Limit}", ctx.JobId, ctx.State.CrawledCount, ctx.MaxPagesLimit);
    }

    private async Task RunCrawlLoopAsync(CrawlContext ctx)
    {
        while (ctx.CanContinue && !ctx.Queue.IsEmpty)
        {
            await WaitWhilePausedAsync(ctx);
            if (ctx.Token.IsCancellationRequested) break;

            var batch = DequeueBatch(ctx);
            await Task.WhenAll(batch.Select(item => Task.Run(() => ProcessPageAsync(ctx, item), ctx.Token)));
        }
    }

    private async Task WaitWhilePausedAsync(CrawlContext ctx)
    {
        while (_pausedJobs.TryGetValue(ctx.JobId, out bool isPaused) && isPaused && !ctx.Token.IsCancellationRequested)
        {
            await Task.Delay(PausePollInterval, ctx.Token);
        }
    }

    private static List<CrawlWorkItem> DequeueBatch(CrawlContext ctx)
    {
        var batch = new List<CrawlWorkItem>(BatchSize);
        while (batch.Count < BatchSize && ctx.CanContinue && ctx.Queue.TryDequeue(out var item))
        {
            batch.Add(item);
        }
        return batch;
    }

    /// <summary>Crawls one page; any unexpected error is reported on the page instead of failing the whole job.</summary>
    private async Task ProcessPageAsync(CrawlContext ctx, CrawlWorkItem item)
    {
        var attempt = new PageAttempt();
        try
        {
            await CrawlPageAsync(ctx, item, attempt);
        }
        catch (OperationCanceledException) when (ctx.Token.IsCancellationRequested)
        {
            // Expected on job cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing page {Url} in job {JobId}", item.Url, ctx.JobId);
            if (!attempt.Reported)
            {
                await ReportFailureAsync(ctx, item, FailedFetchStatus, CrawlErrorClassifier.FromException(ex));
            }
        }
    }

    private async Task CrawlPageAsync(CrawlContext ctx, CrawlWorkItem item, PageAttempt attempt)
    {
        if (!ctx.CanContinue) return;

        FetchResult fetch = await _fetcher.FetchAsync(item.Url, ctx.Token);
        if (fetch.Outcome == FetchOutcome.Canceled || ctx.Token.IsCancellationRequested) return;

        if (fetch.Error is { } fetchError)
        {
            await ReportFailureAsync(ctx, item, fetch.StatusCode, fetchError);
            return;
        }

        await StoreAndPublishAsync(ctx, item, fetch, attempt);
    }

    private async Task StoreAndPublishAsync(CrawlContext ctx, CrawlWorkItem item, FetchResult fetch, PageAttempt attempt)
    {
        byte[] htmlBytes = Encoding.UTF8.GetBytes(fetch.Html);

        // The slot is claimed BEFORE saving, counting or queueing so the page limit is strict.
        if (ctx.Token.IsCancellationRequested || !ctx.State.TryReserveSlot(htmlBytes.Length)) return;

        string? storagePath = await TrySavePageAsync(UrlNormalizer.ExtractDomain(item.Url), item.Url, htmlBytes);
        if (storagePath == null)
        {
            // Page cannot be previewed or verified later: release its slot and report it as failed.
            ctx.State.ReleaseSlot(htmlBytes.Length);
            attempt.Reported = true;
            await ReportFailureAsync(ctx, item, FailedFetchStatus, CrawlErrorClassifier.FromStorageFailure());
            return;
        }

        ExtractedPage extracted = PageLinkExtractor.Extract(fetch.Html, item.Url);
        if (ctx.Token.IsCancellationRequested) return;

        await PublishPageCrawledAsync(ctx, item, fetch.StatusCode, htmlBytes.Length, storagePath, extracted);
        attempt.Reported = true;

        await DiscoverLinksAsync(ctx, item, extracted.Links);
    }

    private Task PublishPageCrawledAsync(
        CrawlContext ctx, CrawlWorkItem item, int statusCode, long byteSize, string storagePath, ExtractedPage extracted) =>
        _publishEndpoint.Publish(new PageCrawledEvent
        {
            JobId = ctx.JobId,
            Url = item.Url,
            UrlCrc32 = item.UrlCrc,
            StatusCode = statusCode,
            ByteSize = byteSize,
            SimHash = extracted.SimHash,
            StoragePath = storagePath,
            OutgoingLinksCount = extracted.Links.Count,
            CrawledAt = DateTime.UtcNow
        }, CancellationToken.None);

    /// <summary>Publishes every link of a page within the depth limit and queues the ones not seen before.</summary>
    private async Task DiscoverLinksAsync(CrawlContext ctx, CrawlWorkItem item, IReadOnlyList<CrawlLink> links)
    {
        if (item.Depth >= ctx.MaxDepth) return;

        foreach (var link in links)
        {
            if (ctx.Token.IsCancellationRequested) break;

            await _publishEndpoint.Publish(new LinkDiscoveredEvent
            {
                JobId = ctx.JobId,
                SourceUrl = item.Url,
                TargetUrl = link.Url,
                TargetUrlCrc32 = link.UrlCrc,
                Depth = item.Depth + 1,
                DiscoveredAt = DateTime.UtcNow
            }, CancellationToken.None);

            if (ctx.State.TryMarkDiscovered(link.UrlCrc) && ctx.State.HasCapacity)
            {
                ctx.Queue.Enqueue(new CrawlWorkItem(link.Url, link.UrlCrc, item.Depth + 1));
            }
        }
    }

    private async Task<string?> TrySavePageAsync(string domain, string url, byte[] content)
    {
        // One retry covers transient IO errors (file locks, brief network-share hiccups).
        for (int attempt = 1; attempt <= StorageAttempts; attempt++)
        {
            try
            {
                return await _storageService.SavePageAsync(domain, url, content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save page content for {Url} (attempt {Attempt}/{Max})", url, attempt, StorageAttempts);
            }
        }
        return null;
    }

    /// <summary>Records a failed page; a failed start page is also remembered so the job can be failed with its reason.</summary>
    private async Task ReportFailureAsync(CrawlContext ctx, CrawlWorkItem item, int statusCode, CrawlError error)
    {
        _logger.LogWarning("Fetch failed for {Url} [{Kind}]: {Message}", item.Url, error.Kind, error.Message);
        if (item.Depth == 0) ctx.State.RecordStartPageError(error.Message);
        if (ctx.Token.IsCancellationRequested) return;

        await _publishEndpoint.Publish(new PageCrawledEvent
        {
            JobId = ctx.JobId,
            Url = item.Url,
            UrlCrc32 = item.UrlCrc,
            StatusCode = statusCode,
            ByteSize = 0,
            OutgoingLinksCount = 0,
            CrawledAt = DateTime.UtcNow,
            ErrorKind = error.Kind,
            ErrorMessage = error.Message
        }, CancellationToken.None);
    }

    private async Task PublishTerminalAsync(CrawlContext ctx, JobProgressUpdatedEvent progress)
    {
        if (ctx.Token.IsCancellationRequested) return;
        await _publishEndpoint.Publish(progress, CancellationToken.None);
    }

    private async Task PublishJobFailedAsync(string jobId, JobProgressUpdatedEvent failed)
    {
        try
        {
            await _publishEndpoint.Publish(failed, CancellationToken.None);
        }
        catch (Exception publishEx)
        {
            _logger.LogError(publishEx, "Could not publish Failed status for job {JobId}", jobId);
        }
    }

    private readonly record struct CrawlWorkItem(string Url, uint UrlCrc, int Depth);

    /// <summary>Tracks whether a page already produced its PageCrawledEvent, so a late error is not reported twice.</summary>
    private sealed class PageAttempt
    {
        public bool Reported { get; set; }
    }

    private sealed class CrawlContext
    {
        public CrawlContext(string jobId, int maxDepth, int maxPagesLimit, CrawlJobState state, CancellationToken token)
        {
            JobId = jobId;
            MaxDepth = maxDepth;
            MaxPagesLimit = maxPagesLimit;
            State = state;
            Token = token;
        }

        public string JobId { get; }
        public int MaxDepth { get; }
        public int MaxPagesLimit { get; }
        public CrawlJobState State { get; }
        public CancellationToken Token { get; }
        public ConcurrentQueue<CrawlWorkItem> Queue { get; } = new();

        public bool CanContinue => State.HasCapacity && !Token.IsCancellationRequested;
    }
}
