namespace AltevaCrawler.Worker.Services;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MassTransit;
using Microsoft.Extensions.Logging;
using AltevaCrawler.Shared.Events;
using AltevaCrawler.Shared.Storage;
using AltevaCrawler.Shared.Utilities;

public class CrawlerEngine
{
    private readonly HttpClient _httpClient;
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
        _httpClient = httpClientFactory.CreateClient("CrawlClient");
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
        var token = jobCts.Token;

        _logger.LogInformation("Starting parallel crawl job {JobId} for {Url} (MaxPages: {Limit})", jobId, startUrl, maxPagesLimit);

        int crawledCount = 0;
        long totalBytes = 0;
        var jobUrlCache = new HashSet<uint>();

        try
        {
            string? normalizedStartUrl = UrlNormalizer.Normalize(startUrl);
            if (normalizedStartUrl == null)
            {
                if (!token.IsCancellationRequested)
                {
                    await _publishEndpoint.Publish(new JobProgressUpdatedEvent
                    {
                        JobId = jobId,
                        Status = "Failed",
                        CrawledPagesCount = 0,
                        DiscoveredPagesCount = 0,
                        TotalBytesDownloaded = 0,
                        FailureReason = "Invalid start URL.",
                        UpdatedAt = DateTime.UtcNow
                    }, CancellationToken.None);
                }
                return;
            }

            uint startCrc = UrlNormalizer.ComputeCrc32(normalizedStartUrl);

            var queue = new ConcurrentQueue<(string Url, uint UrlCrc, int Depth, string? ParentUrl)>();

            lock (jobUrlCache)
            {
                jobUrlCache.Add(startCrc);
            }
            queue.Enqueue((normalizedStartUrl, startCrc, 0, null));

            using var semaphore = new SemaphoreSlim(5);

            while (!queue.IsEmpty && Volatile.Read(ref crawledCount) < maxPagesLimit && !token.IsCancellationRequested)
            {
                while (_pausedJobs.TryGetValue(jobId, out bool isPaused) && isPaused && !token.IsCancellationRequested)
                {
                    await Task.Delay(1000, token);
                }

                if (token.IsCancellationRequested) break;

                var tasks = new List<Task>();

                while (!queue.IsEmpty && Volatile.Read(ref crawledCount) < maxPagesLimit && tasks.Count < 5 && !token.IsCancellationRequested)
                {
                    if (!queue.TryDequeue(out var item)) break;

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await semaphore.WaitAsync(token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }

                        bool pageReported = false;
                        try
                        {
                            if (Volatile.Read(ref crawledCount) >= maxPagesLimit || token.IsCancellationRequested)
                            {
                                return;
                            }

                            var (currentUrl, currentCrc, currentDepth, parentUrl) = item;

                            HttpResponseMessage response;
                            try
                            {
                                response = await _httpClient.GetAsync(currentUrl, token);
                            }
                            catch (OperationCanceledException)
                            {
                                return;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning("HTTP error for {Url}: {Message}", currentUrl, ex.Message);
                                if (!token.IsCancellationRequested)
                                {
                                    await _publishEndpoint.Publish(new PageCrawledEvent
                                    {
                                        JobId = jobId,
                                        Url = currentUrl,
                                        UrlCrc32 = currentCrc,
                                        StatusCode = 500,
                                        ByteSize = 0,
                                        OutgoingLinksCount = 0,
                                        CrawledAt = DateTime.UtcNow
                                    }, CancellationToken.None);
                                }
                                return;
                            }

                            if (token.IsCancellationRequested) return;

                            if (!response.IsSuccessStatusCode)
                            {
                                _logger.LogInformation("Fetch failed for {Url} ({StatusCode})", currentUrl, response.StatusCode);
                                if (!token.IsCancellationRequested)
                                {
                                    await _publishEndpoint.Publish(new PageCrawledEvent
                                    {
                                        JobId = jobId,
                                        Url = currentUrl,
                                        UrlCrc32 = currentCrc,
                                        StatusCode = (int)response.StatusCode,
                                        ByteSize = 0,
                                        OutgoingLinksCount = 0,
                                        CrawledAt = DateTime.UtcNow
                                    }, CancellationToken.None);
                                }
                                return;
                            }

                            string contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                            if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInformation("Skipping non-HTML page {Url} ({ContentType})", currentUrl, contentType);
                                if (!token.IsCancellationRequested)
                                {
                                    await _publishEndpoint.Publish(new PageCrawledEvent
                                    {
                                        JobId = jobId,
                                        Url = currentUrl,
                                        UrlCrc32 = currentCrc,
                                        StatusCode = (int)response.StatusCode,
                                        ByteSize = 0,
                                        OutgoingLinksCount = 0,
                                        CrawledAt = DateTime.UtcNow
                                    }, CancellationToken.None);
                                }
                                return;
                            }

                            string html = await response.Content.ReadAsStringAsync(token);
                            byte[] htmlBytes = Encoding.UTF8.GetBytes(html);
                            long byteSize = htmlBytes.Length;

                            // Atomic increment with strict limit check BEFORE saving, counting, or queueing
                            if (Volatile.Read(ref crawledCount) >= maxPagesLimit || token.IsCancellationRequested)
                            {
                                return;
                            }

                            int currentCount = Interlocked.Increment(ref crawledCount);
                            if (currentCount > maxPagesLimit)
                            {
                                Interlocked.Decrement(ref crawledCount);
                                return;
                            }

                            Interlocked.Add(ref totalBytes, byteSize);

                            string domain = UrlNormalizer.ExtractDomain(currentUrl);
                            string? relativeStoragePath = await TrySavePageAsync(domain, currentUrl, htmlBytes);
                            if (relativeStoragePath == null)
                            {
                                // Page cannot be previewed or verified later: release its slot and report it as failed.
                                Interlocked.Decrement(ref crawledCount);
                                Interlocked.Add(ref totalBytes, -byteSize);
                                pageReported = true;
                                if (!token.IsCancellationRequested)
                                {
                                    await PublishPageFailureAsync(jobId, currentUrl, currentCrc, 500);
                                }
                                return;
                            }

                            var doc = new HtmlDocument();
                            doc.LoadHtml(html);
                            string textContent = doc.DocumentNode.InnerText;
                            ulong simHash = SimHashCalculator.Compute64(textContent);

                            var hrefNodes = doc.DocumentNode.SelectNodes("//a[@href]");
                            var outgoingLinks = new List<(string Url, uint UrlCrc)>();

                            if (hrefNodes != null)
                            {
                                foreach (var node in hrefNodes)
                                {
                                    string rawHref = node.GetAttributeValue("href", string.Empty);
                                    string? normalizedHref = UrlNormalizer.Normalize(rawHref, currentUrl);
                                    if (normalizedHref != null)
                                    {
                                        uint linkCrc = UrlNormalizer.ComputeCrc32(normalizedHref);
                                        outgoingLinks.Add((normalizedHref, linkCrc));
                                    }
                                }
                            }

                            if (!token.IsCancellationRequested)
                            {
                                await _publishEndpoint.Publish(new PageCrawledEvent
                                {
                                    JobId = jobId,
                                    Url = currentUrl,
                                    UrlCrc32 = currentCrc,
                                    StatusCode = (int)response.StatusCode,
                                    ByteSize = byteSize,
                                    SimHash = simHash,
                                    StoragePath = relativeStoragePath,
                                    OutgoingLinksCount = outgoingLinks.Count,
                                    CrawledAt = DateTime.UtcNow
                                }, CancellationToken.None);
                                pageReported = true;
                            }

                            if (currentDepth < maxDepth && !token.IsCancellationRequested)
                            {
                                foreach (var (linkUrl, linkCrc) in outgoingLinks)
                                {
                                    if (token.IsCancellationRequested) break;

                                    await _publishEndpoint.Publish(new LinkDiscoveredEvent
                                    {
                                        JobId = jobId,
                                        SourceUrl = currentUrl,
                                        TargetUrl = linkUrl,
                                        TargetUrlCrc32 = linkCrc,
                                        Depth = currentDepth + 1,
                                        DiscoveredAt = DateTime.UtcNow
                                    }, CancellationToken.None);

                                    bool isNewLink;
                                    lock (jobUrlCache)
                                    {
                                        isNewLink = jobUrlCache.Add(linkCrc);
                                    }

                                    if (isNewLink && Volatile.Read(ref crawledCount) < maxPagesLimit)
                                    {
                                        queue.Enqueue((linkUrl, linkCrc, currentDepth + 1, currentUrl));
                                    }
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected on job cancellation
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing page {Url} in job {JobId}", item.Url, jobId);
                            if (!pageReported && !token.IsCancellationRequested)
                            {
                                await PublishPageFailureAsync(jobId, item.Url, item.UrlCrc, 500);
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, token));
                }

                await Task.WhenAll(tasks);
            }

            if (!token.IsCancellationRequested)
            {
                await _publishEndpoint.Publish(new JobProgressUpdatedEvent
                {
                    JobId = jobId,
                    Status = "Completed",
                    CrawledPagesCount = crawledCount,
                    DiscoveredPagesCount = jobUrlCache.Count,
                    TotalBytesDownloaded = totalBytes,
                    UpdatedAt = DateTime.UtcNow
                }, CancellationToken.None);

                _logger.LogInformation("Crawl loop finished for job {JobId}. Crawled: {Count}/{Limit}", jobId, crawledCount, maxPagesLimit);
            }
            else
            {
                _logger.LogInformation("Crawl job {JobId} canceled.", jobId);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Crawl job {JobId} canceled.", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing job {JobId}", jobId);
            await PublishJobFailedAsync(jobId, ex, crawledCount, jobUrlCache.Count, totalBytes);
        }
        finally
        {
            _jobCtsMap.TryRemove(jobId, out _);
            _pausedJobs.TryRemove(jobId, out _);
        }
    }

    private async Task<string?> TrySavePageAsync(string domain, string url, byte[] content)
    {
        // One retry covers transient IO errors (file locks, brief network-share hiccups).
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                return await _storageService.SavePageAsync(domain, url, content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save page content for {Url} (attempt {Attempt}/2)", url, attempt);
            }
        }
        return null;
    }

    private Task PublishPageFailureAsync(string jobId, string url, uint urlCrc, int statusCode) =>
        _publishEndpoint.Publish(new PageCrawledEvent
        {
            JobId = jobId,
            Url = url,
            UrlCrc32 = urlCrc,
            StatusCode = statusCode,
            ByteSize = 0,
            OutgoingLinksCount = 0,
            CrawledAt = DateTime.UtcNow
        }, CancellationToken.None);

    private async Task PublishJobFailedAsync(string jobId, Exception ex, int crawled, int discovered, long bytes)
    {
        try
        {
            await _publishEndpoint.Publish(new JobProgressUpdatedEvent
            {
                JobId = jobId,
                Status = "Failed",
                CrawledPagesCount = crawled,
                DiscoveredPagesCount = discovered,
                TotalBytesDownloaded = bytes,
                FailureReason = $"{ex.GetType().Name}: {ex.Message}",
                UpdatedAt = DateTime.UtcNow
            }, CancellationToken.None);
        }
        catch (Exception publishEx)
        {
            _logger.LogError(publishEx, "Could not publish Failed status for job {JobId}", jobId);
        }
    }
}
