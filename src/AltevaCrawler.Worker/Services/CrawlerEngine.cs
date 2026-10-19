namespace AltevaCrawler.Worker.Services;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        _logger.LogInformation("Starting parallel crawl job {JobId} for {Url}", jobId, startUrl);

        string? normalizedStartUrl = UrlNormalizer.Normalize(startUrl);
        if (normalizedStartUrl == null)
        {
            await _publishEndpoint.Publish(new JobProgressUpdatedEvent
            {
                JobId = jobId,
                Status = "Failed",
                CrawledPagesCount = 0,
                DiscoveredPagesCount = 0,
                TotalBytesDownloaded = 0,
                UpdatedAt = DateTime.UtcNow
            }, token);
            return;
        }

        uint startCrc = UrlNormalizer.ComputeCrc32(normalizedStartUrl);

        var queue = new ConcurrentQueue<(string Url, uint UrlCrc, int Depth, string? ParentUrl)>();
        var jobUrlCache = new HashSet<uint>();

        lock (jobUrlCache)
        {
            jobUrlCache.Add(startCrc);
        }
        queue.Enqueue((normalizedStartUrl, startCrc, 0, null));

        int crawledCount = 0;
        long totalBytes = 0;
        using var semaphore = new SemaphoreSlim(5);

        while (!queue.IsEmpty && crawledCount < maxPagesLimit && !token.IsCancellationRequested)
        {
            while (_pausedJobs.TryGetValue(jobId, out bool isPaused) && isPaused && !token.IsCancellationRequested)
            {
                await Task.Delay(1000, token);
            }

            var tasks = new List<Task>();

            while (!queue.IsEmpty && crawledCount < maxPagesLimit && tasks.Count < 5 && !token.IsCancellationRequested)
            {
                if (!queue.TryDequeue(out var item)) break;

                await semaphore.WaitAsync(token);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var (currentUrl, currentCrc, currentDepth, parentUrl) = item;

                        HttpResponseMessage response;
                        try
                        {
                            response = await _httpClient.GetAsync(currentUrl, token);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("HTTP error for {Url}: {Message}", currentUrl, ex.Message);
                            await _publishEndpoint.Publish(new PageCrawledEvent
                            {
                                JobId = jobId,
                                Url = currentUrl,
                                UrlCrc32 = currentCrc,
                                StatusCode = 500,
                                ByteSize = 0,
                                OutgoingLinksCount = 0,
                                CrawledAt = DateTime.UtcNow
                            }, token);
                            return;
                        }

                        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                        {
                            _logger.LogInformation("Dead link (4xx) encountered: {Url} ({StatusCode})", currentUrl, response.StatusCode);
                            await _publishEndpoint.Publish(new PageCrawledEvent
                            {
                                JobId = jobId,
                                Url = currentUrl,
                                UrlCrc32 = currentCrc,
                                StatusCode = (int)response.StatusCode,
                                ByteSize = 0,
                                OutgoingLinksCount = 0,
                                CrawledAt = DateTime.UtcNow
                            }, token);
                            return;
                        }

                        string html = await response.Content.ReadAsStringAsync(token);
                        byte[] htmlBytes = Encoding.UTF8.GetBytes(html);
                        long byteSize = htmlBytes.Length;

                        Interlocked.Add(ref totalBytes, byteSize);
                        Interlocked.Increment(ref crawledCount);

                        string domain = UrlNormalizer.ExtractDomain(currentUrl);
                        string relativeStoragePath = await _storageService.SavePageAsync(domain, currentUrl, htmlBytes);

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
                        }, token);

                        if (currentDepth < maxDepth)
                        {
                            foreach (var (linkUrl, linkCrc) in outgoingLinks)
                            {
                                await _publishEndpoint.Publish(new LinkDiscoveredEvent
                                {
                                    JobId = jobId,
                                    SourceUrl = currentUrl,
                                    TargetUrl = linkUrl,
                                    TargetUrlCrc32 = linkCrc,
                                    Depth = currentDepth + 1,
                                    DiscoveredAt = DateTime.UtcNow
                                }, token);

                                bool isNewLink;
                                lock (jobUrlCache)
                                {
                                    isNewLink = jobUrlCache.Add(linkCrc);
                                }

                                if (isNewLink)
                                {
                                    queue.Enqueue((linkUrl, linkCrc, currentDepth + 1, currentUrl));
                                }
                            }
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

        _jobCtsMap.TryRemove(jobId, out _);

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
        }

        _logger.LogInformation("Crawl loop completed for job {JobId}.", jobId);
    }
}
