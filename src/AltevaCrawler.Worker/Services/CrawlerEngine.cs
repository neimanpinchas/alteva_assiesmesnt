namespace AltevaCrawler.Worker.Services;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MassTransit;
using Microsoft.Extensions.Logging;
using AltevaCrawler.Shared.Events;
using AltevaCrawler.Shared.Utilities;

public class CrawlerEngine
{
    private readonly HttpClient _httpClient;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<CrawlerEngine> _logger;
    private readonly string _storageBasePath;

    // In-memory URL cache using UrlCrc32
    private readonly ConcurrentDictionary<uint, string> _inMemoryUrlCache = new();

    // Pause state tracking per job
    private readonly ConcurrentDictionary<string, bool> _pausedJobs = new();

    public CrawlerEngine(
        IHttpClientFactory httpClientFactory,
        IPublishEndpoint publishEndpoint,
        ILogger<CrawlerEngine> logger)
    {
        _httpClient = httpClientFactory.CreateClient("CrawlClient");
        _publishEndpoint = publishEndpoint;
        _logger = logger;
        _storageBasePath = Path.Combine(Directory.GetCurrentDirectory(), "storage");
        Directory.CreateDirectory(_storageBasePath);
    }

    public void PauseJob(string jobId) => _pausedJobs[jobId] = true;
    public void ResumeJob(string jobId) => _pausedJobs.TryRemove(jobId, out _);

    public async Task ProcessJobAsync(string jobId, string startUrl, int maxDepth, int maxPagesLimit = 200, CancellationToken ct = default)
    {
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
            }, ct);
            return;
        }

        uint startCrc = UrlNormalizer.ComputeCrc32(normalizedStartUrl);

        var queue = new ConcurrentQueue<(string Url, uint UrlCrc, int Depth, string? ParentUrl)>();
        queue.Enqueue((normalizedStartUrl, startCrc, 0, null));
        _inMemoryUrlCache.TryAdd(startCrc, normalizedStartUrl);

        int crawledCount = 0;
        long totalBytes = 0;
        using var semaphore = new SemaphoreSlim(5); // Parallel concurrency = 5

        while (!queue.IsEmpty && crawledCount < maxPagesLimit && !ct.IsCancellationRequested)
        {
            // Check if job is paused
            while (_pausedJobs.TryGetValue(jobId, out bool isPaused) && isPaused && !ct.IsCancellationRequested)
            {
                await Task.Delay(1000, ct);
            }

            var tasks = new List<Task>();

            while (!queue.IsEmpty && crawledCount < maxPagesLimit && tasks.Count < 5 && !ct.IsCancellationRequested)
            {
                if (!queue.TryDequeue(out var item)) break;

                await semaphore.WaitAsync(ct);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var (currentUrl, currentCrc, currentDepth, parentUrl) = item;

                        HttpResponseMessage response;
                        try
                        {
                            response = await _httpClient.GetAsync(currentUrl, ct);
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
                            }, ct);
                            return;
                        }

                        // Do not retry 4xx errors; publish dead link event immediately
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
                            }, ct);
                            return;
                        }

                        string html = await response.Content.ReadAsStringAsync(ct);
                        byte[] htmlBytes = Encoding.UTF8.GetBytes(html);
                        long byteSize = htmlBytes.Length;

                        Interlocked.Add(ref totalBytes, byteSize);
                        Interlocked.Increment(ref crawledCount);

                        // Save HTML file replicating website URL path structure
                        string domain = UrlNormalizer.ExtractDomain(currentUrl);
                        Uri.TryCreate(currentUrl, UriKind.Absolute, out var uri);
                        string pathSegment = uri != null ? uri.AbsolutePath.Trim('/') : "index";
                        if (string.IsNullOrEmpty(pathSegment)) pathSegment = "index";
                        pathSegment = pathSegment.Replace('/', Path.DirectorySeparatorChar);

                        string domainFolder = Path.Combine(_storageBasePath, domain);
                        string fullFilePath = Path.Combine(domainFolder, pathSegment + ".html");
                        string? parentDirectory = Path.GetDirectoryName(fullFilePath);
                        if (parentDirectory != null) Directory.CreateDirectory(parentDirectory);

                        await File.WriteAllBytesAsync(fullFilePath, htmlBytes, ct);

                        // Parse outgoing links
                        var doc = new HtmlDocument();
                        doc.LoadHtml(html);
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
                            OutgoingLinksCount = outgoingLinks.Count,
                            CrawledAt = DateTime.UtcNow
                        }, ct);

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
                                }, ct);

                                // Fast in-memory deduplication check BEFORE queueing
                                if (_inMemoryUrlCache.TryAdd(linkCrc, linkUrl))
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
                }, ct));
            }

            await Task.WhenAll(tasks);
        }

        await _publishEndpoint.Publish(new JobProgressUpdatedEvent
        {
            JobId = jobId,
            Status = "Completed",
            CrawledPagesCount = crawledCount,
            DiscoveredPagesCount = _inMemoryUrlCache.Count,
            TotalBytesDownloaded = totalBytes,
            UpdatedAt = DateTime.UtcNow
        }, ct);

        _logger.LogInformation("Parallel crawl finished for job {JobId}. Total crawled: {Count}", jobId, crawledCount);
    }
}
