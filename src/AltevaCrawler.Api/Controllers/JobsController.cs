namespace AltevaCrawler.Api.Controllers;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using AltevaCrawler.Shared.Data;
using AltevaCrawler.Shared.Entities;
using AltevaCrawler.Shared.Enums;
using AltevaCrawler.Shared.Events;
using AltevaCrawler.Shared.Models;
using AltevaCrawler.Shared.Utilities;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;

    public JobsController(AppDbContext dbContext, IPublishEndpoint publishEndpoint)
    {
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
    }

    [HttpPost]
    public async Task<IActionResult> CreateJob([FromBody] CreateJobRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest(new { error = "Url is required." });
        }

        string? normalizedUrl = UrlNormalizer.Normalize(request.Url);
        if (normalizedUrl == null)
        {
            return BadRequest(new { error = "Invalid URL format." });
        }

        string jobId = Guid.NewGuid().ToString();
        int maxDepth = request.MaxDepth ?? 2;
        int maxPagesLimit = request.MaxPagesLimit ?? 200;

        var job = new Job
        {
            Id = jobId,
            Url = normalizedUrl,
            MaxDepth = maxDepth,
            MaxPagesLimit = maxPagesLimit,
            Status = JobStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Jobs.Add(job);
        _dbContext.JobStatusTransitions.Add(new JobStatusTransition
        {
            JobId = jobId,
            FromStatus = 'P',
            ToStatus = 'P',
            TransitionedAt = DateTime.UtcNow,
            Reason = "Job created."
        });

        await _dbContext.SaveChangesAsync();

        await _publishEndpoint.Publish(new CrawlJobRequestedEvent
        {
            JobId = jobId,
            Url = normalizedUrl,
            UrlCrc32 = UrlNormalizer.ComputeCrc32(normalizedUrl),
            MaxDepth = maxDepth,
            MaxPagesLimit = maxPagesLimit,
            RequestedAt = DateTime.UtcNow
        });

        return Ok(new { jobId, status = job.Status.ToString() });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetJob(string id)
    {
        var job = await _dbContext.Jobs
            .Include(j => j.StatusTransitions)
            .FirstOrDefaultAsync(j => j.Id == id);

        if (job == null) return NotFound();

        int totalLinksCount = await _dbContext.JobPageLinks.CountAsync(l => l.JobId == id);

        string rootDomain = UrlNormalizer.ExtractDomain(job.Url);
        uint rootDomainHash = UrlNormalizer.ComputeDomainHash(rootDomain);

        var pageIds = await _dbContext.JobPageLinks
            .Where(l => l.JobId == id)
            .Select(l => l.TargetPageId)
            .Distinct()
            .ToListAsync();

        var pages = await _dbContext.Pages
            .Where(p => pageIds.Contains(p.Id))
            .ToListAsync();

        int internalCount = pages.Count(p => p.DomainHash == rootDomainHash);
        double domainRatio = pages.Count > 0 ? (double)internalCount / pages.Count : 0.0;

        var summary = new JobSummaryDto
        {
            Id = job.Id,
            Url = job.Url,
            MaxDepth = job.MaxDepth,
            MaxPagesLimit = job.MaxPagesLimit,
            Status = job.Status.ToString(),
            TotalPagesCrawled = job.TotalPagesCrawled,
            DiscoveredLinksCount = totalLinksCount,
            TotalBytesDownloaded = job.TotalBytesDownloaded,
            DomainLinkRatio = Math.Round(domainRatio, 4),
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            FailureReason = job.FailureReason,
            StatusHistory = job.StatusTransitions.Select(t => new JobStatusHistoryDto
            {
                FromStatus = t.FromStatus,
                ToStatus = t.ToStatus,
                TransitionedAt = t.TransitionedAt,
                Reason = t.Reason
            }).ToList()
        };

        return Ok(summary);
    }

    [HttpGet("{id}/tree")]
    public async Task<IActionResult> GetJobTree(string id, [FromQuery] long? parentPageId = null, [FromQuery] int depth = 3)
    {
        var job = await _dbContext.Jobs.FirstOrDefaultAsync(j => j.Id == id);
        if (job == null) return NotFound();

        string rootDomain = UrlNormalizer.ExtractDomain(job.Url);
        uint rootDomainHash = UrlNormalizer.ComputeDomainHash(rootDomain);

        if (!parentPageId.HasValue)
        {
            string rootNormalizedUrl = job.Url;
            uint rootUrlCrc = UrlNormalizer.ComputeCrc32(rootNormalizedUrl);

            var rootPage = await _dbContext.Pages
                .FirstOrDefaultAsync(p => p.UrlCrc32 == rootUrlCrc && p.NormalizedUrl == rootNormalizedUrl);

            if (rootPage == null)
            {
                return Ok(new List<TreeNodeDto>());
            }

            var rootNode = await BuildTreeNodeAsync(id, rootPage, depth, rootDomainHash);
            return Ok(new List<TreeNodeDto> { rootNode });
        }
        else
        {
            var parentPage = await _dbContext.Pages.FirstOrDefaultAsync(p => p.Id == parentPageId.Value);
            if (parentPage == null) return NotFound();

            var children = await GetChildNodesAsync(id, parentPageId.Value, depth, rootDomainHash);
            return Ok(children);
        }
    }

    private async Task<TreeNodeDto> BuildTreeNodeAsync(string jobId, Page page, int remainingDepth, uint rootDomainHash)
    {
        var childLinks = await _dbContext.JobPageLinks
            .Include(l => l.TargetPage)
            .Where(l => l.JobId == jobId && l.SourcePageId == page.Id)
            .ToListAsync();

        int totalOutgoing = childLinks.Count;
        int sameDomainCount = childLinks.Count(l => l.TargetPage != null && l.TargetPage.DomainHash == rootDomainHash);

        double onTheFlyDomainLinkRatio = totalOutgoing > 0 ? (double)sameDomainCount / totalOutgoing : 0.0;

        var node = new TreeNodeDto
        {
            Id = page.Id,
            Url = page.Url,
            UrlCrc32 = page.UrlCrc32,
            Domain = page.Domain,
            StatusCode = page.StatusCode,
            SimHash = page.SimHash,
            OutgoingLinksCount = page.OutgoingLinksCount,
            OnTheFlyDomainLinkRatio = Math.Round(onTheFlyDomainLinkRatio, 4),
            IsCrawled = page.IsCrawled
        };

        if (remainingDepth > 1 && childLinks.Count > 0)
        {
            foreach (var link in childLinks)
            {
                if (link.TargetPage != null)
                {
                    var childNode = await BuildTreeNodeAsync(jobId, link.TargetPage, remainingDepth - 1, rootDomainHash);
                    node.Children.Add(childNode);
                }
            }
        }

        return node;
    }

    private async Task<List<TreeNodeDto>> GetChildNodesAsync(string jobId, long parentPageId, int remainingDepth, uint rootDomainHash)
    {
        var childLinks = await _dbContext.JobPageLinks
            .Include(l => l.TargetPage)
            .Where(l => l.JobId == jobId && l.SourcePageId == parentPageId)
            .ToListAsync();

        var result = new List<TreeNodeDto>();
        foreach (var link in childLinks)
        {
            if (link.TargetPage != null)
            {
                var childNode = await BuildTreeNodeAsync(jobId, link.TargetPage, remainingDepth, rootDomainHash);
                result.Add(childNode);
            }
        }
        return result;
    }

    [HttpGet("{id}/metrics")]
    public async Task<IActionResult> GetJobMetrics(string id)
    {
        var job = await _dbContext.Jobs.FirstOrDefaultAsync(j => j.Id == id);
        if (job == null) return NotFound();

        string rootDomain = UrlNormalizer.ExtractDomain(job.Url);
        uint rootDomainHash = UrlNormalizer.ComputeDomainHash(rootDomain);

        var pageIds = await _dbContext.JobPageLinks
            .Where(l => l.JobId == id)
            .Select(l => l.TargetPageId)
            .Distinct()
            .ToListAsync();

        var pages = await _dbContext.Pages
            .Where(p => pageIds.Contains(p.Id))
            .ToListAsync();

        int internalCount = pages.Count(p => p.DomainHash == rootDomainHash);
        int externalCount = pages.Count - internalCount;

        // Optimized single query for page sharing counts
        var pageJobCounts = await _dbContext.JobPageLinks
            .Where(l => pageIds.Contains(l.TargetPageId))
            .GroupBy(l => l.TargetPageId)
            .Select(g => new { PageId = g.Key, Count = g.Select(x => x.JobId).Distinct().Count() })
            .ToListAsync();

        int uniquePagesCount = pageJobCounts.Count(x => x.Count <= 1);
        int sharedPagesCount = pageJobCounts.Count(x => x.Count > 1);

        var sharedPageIds = pageJobCounts.Where(x => x.Count > 1).Select(x => x.PageId).ToHashSet();
        long estimatedBytesSaved = pages.Where(p => sharedPageIds.Contains(p.Id)).Sum(p => p.ByteSize);

        int totalDiscovered = await _dbContext.JobPageLinks.CountAsync(l => l.JobId == id);

        DateTime? forecastedCompletion = null;
        if (job.Status == JobStatus.Running && job.StartedAt.HasValue && job.TotalPagesCrawled > 0)
        {
            double elapsedSeconds = (DateTime.UtcNow - job.StartedAt.Value).TotalSeconds;
            double pagesPerSecond = job.TotalPagesCrawled / Math.Max(1.0, elapsedSeconds);
            int remainingEstimate = Math.Max(0, totalDiscovered - job.TotalPagesCrawled);
            forecastedCompletion = DateTime.UtcNow.AddSeconds(remainingEstimate / Math.Max(0.1, pagesPerSecond));
        }

        var metrics = new JobMetricsDto
        {
            DomainLinkRatioPie = new DomainLinkRatioPieDto
            {
                InternalLinks = internalCount,
                ExternalLinks = externalCount,
                TotalPages = pages.Count,
                DomainLinkRatio = pages.Count > 0 ? Math.Round((double)internalCount / pages.Count, 4) : 0.0
            },
            PageSharing = new PageSharingDto
            {
                UniqueToThisJob = uniquePagesCount,
                SharedAcrossJobs = sharedPagesCount
            },
            BandwidthEfficiency = new BandwidthEfficiencyDto
            {
                TotalBytesDownloaded = job.TotalBytesDownloaded,
                EstimatedBytesSaved = estimatedBytesSaved
            },
            ForecastedCompletionTime = forecastedCompletion
        };

        return Ok(metrics);
    }

    [HttpGet("global-search")]
    public async Task<IActionResult> GlobalSearch([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest(new { error = "Query parameter 'q' is required." });
        }

        ulong querySimHash = SimHashCalculator.Compute64(q);

        var pages = await _dbContext.Pages
            .Where(p => p.IsCrawled)
            .Take(100)
            .ToListAsync();

        var results = pages
            .Select(p => new PageSearchResultDto
            {
                Id = p.Id,
                Url = p.Url,
                Domain = p.Domain,
                SimHash = p.SimHash,
                HammingDistance = SimHashCalculator.HammingDistance(p.SimHash, querySimHash)
            })
            .OrderBy(r => r.HammingDistance)
            .Take(25)
            .ToList();

        return Ok(results);
    }

    [HttpGet("{id}/search")]
    public async Task<IActionResult> SearchJobPages(string id, [FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest(new { error = "Query string 'q' is required." });
        }

        ulong querySimHash = SimHashCalculator.Compute64(q);

        var pageIds = await _dbContext.JobPageLinks
            .Where(l => l.JobId == id)
            .Select(l => l.TargetPageId)
            .Distinct()
            .ToListAsync();

        var pages = await _dbContext.Pages
            .Where(p => pageIds.Contains(p.Id))
            .ToListAsync();

        var results = pages
            .Select(p => new PageSearchResultDto
            {
                Id = p.Id,
                Url = p.Url,
                Domain = p.Domain,
                JobId = id,
                SimHash = p.SimHash,
                HammingDistance = SimHashCalculator.HammingDistance(p.SimHash, querySimHash)
            })
            .OrderBy(r => r.HammingDistance)
            .Take(25)
            .ToList();

        return Ok(results);
    }

    [HttpGet("{id}/pages/{pageId}/content")]
    public async Task<IActionResult> GetPageContent(string id, long pageId)
    {
        var page = await _dbContext.Pages.FirstOrDefaultAsync(p => p.Id == pageId);
        if (page == null || string.IsNullOrEmpty(page.StoragePath))
        {
            return NotFound(new { error = "Page content file not found on disk." });
        }

        string storageRoot = Path.Combine(Directory.GetCurrentDirectory(), "storage");
        string fullPath = Path.IsPathRooted(page.StoragePath)
            ? page.StoragePath
            : Path.Combine(storageRoot, page.StoragePath);

        if (!StoragePathBuilder.IsUnderRoot(fullPath, storageRoot) || !System.IO.File.Exists(fullPath))
        {
            return NotFound(new { error = "Page content file not found on disk." });
        }

        string rawContent = await System.IO.File.ReadAllTextAsync(fullPath);
        string text = ExtractPlainText(rawContent);

        return Ok(new PageContentDto
        {
            PageId = page.Id,
            Url = page.Url,
            TextContent = text
        });
    }

    private static string ExtractPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(html);

        var nodesToRemove = doc.DocumentNode.SelectNodes("//script|//style|//comment()");
        if (nodesToRemove != null)
        {
            foreach (var node in nodesToRemove)
            {
                node.Remove();
            }
        }

        string innerText = doc.DocumentNode.InnerText;
        return System.Net.WebUtility.HtmlDecode(innerText);
    }

    [HttpGet]
    public async Task<IActionResult> GetJobHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        int total = await _dbContext.Jobs.CountAsync();
        var jobs = await _dbContext.Jobs
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = new List<JobSummaryDto>();
        foreach (var j in jobs)
        {
            string rootDomain = UrlNormalizer.ExtractDomain(j.Url);
            uint rootDomainHash = UrlNormalizer.ComputeDomainHash(rootDomain);

            var pageIds = await _dbContext.JobPageLinks
                .Where(l => l.JobId == j.Id)
                .Select(l => l.TargetPageId)
                .Distinct()
                .ToListAsync();

            var pageDomains = await _dbContext.Pages
                .Where(p => pageIds.Contains(p.Id))
                .Select(p => p.DomainHash)
                .ToListAsync();

            int internalCount = pageDomains.Count(dh => dh == rootDomainHash);
            double domainRatio = pageDomains.Count > 0 ? (double)internalCount / pageDomains.Count : 0.0;

            items.Add(new JobSummaryDto
            {
                Id = j.Id,
                Url = j.Url,
                MaxDepth = j.MaxDepth,
                MaxPagesLimit = j.MaxPagesLimit,
                Status = j.Status.ToString(),
                TotalPagesCrawled = j.TotalPagesCrawled,
                TotalBytesDownloaded = j.TotalBytesDownloaded,
                DomainLinkRatio = Math.Round(domainRatio, 4),
                CreatedAt = j.CreatedAt,
                CompletedAt = j.CompletedAt
            });
        }

        return Ok(new
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = items
        });
    }

    [HttpPost("{id}/pause")]
    public async Task<IActionResult> PauseJob(string id)
    {
        var job = await _dbContext.Jobs.FirstOrDefaultAsync(j => j.Id == id);
        if (job == null) return NotFound();

        char oldStatus = job.Status.ToChar();
        job.Status = JobStatus.Paused;
        _dbContext.JobStatusTransitions.Add(new JobStatusTransition
        {
            JobId = id,
            FromStatus = oldStatus,
            ToStatus = JobStatus.Paused.ToChar(),
            TransitionedAt = DateTime.UtcNow,
            Reason = "User paused job"
        });
        await _dbContext.SaveChangesAsync();

        await _publishEndpoint.Publish(new PauseJobRequestedEvent
        {
            JobId = id,
            RequestedAt = DateTime.UtcNow
        });

        await _publishEndpoint.Publish(new JobProgressUpdatedEvent
        {
            JobId = id,
            Status = "Paused",
            CrawledPagesCount = job.TotalPagesCrawled,
            TotalBytesDownloaded = job.TotalBytesDownloaded,
            UpdatedAt = DateTime.UtcNow
        });

        return Ok(new { message = "Job paused successfully." });
    }

    [HttpPost("{id}/resume")]
    public async Task<IActionResult> ResumeJob(string id)
    {
        var job = await _dbContext.Jobs.FirstOrDefaultAsync(j => j.Id == id);
        if (job == null) return NotFound();

        char oldStatus = job.Status.ToChar();
        job.Status = JobStatus.Running;
        _dbContext.JobStatusTransitions.Add(new JobStatusTransition
        {
            JobId = id,
            FromStatus = oldStatus,
            ToStatus = JobStatus.Running.ToChar(),
            TransitionedAt = DateTime.UtcNow,
            Reason = "User resumed job"
        });
        await _dbContext.SaveChangesAsync();

        await _publishEndpoint.Publish(new ResumeJobRequestedEvent
        {
            JobId = id,
            RequestedAt = DateTime.UtcNow
        });

        return Ok(new { message = "Job resumed successfully." });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteJob(string id)
    {
        var job = await _dbContext.Jobs.FirstOrDefaultAsync(j => j.Id == id);
        if (job == null) return NotFound();

        var jobPageIds = await _dbContext.JobPageLinks
            .Where(l => l.JobId == id)
            .Select(l => l.TargetPageId)
            .Distinct()
            .ToListAsync();

        // Delete job and links
        _dbContext.Jobs.Remove(job);
        await _dbContext.SaveChangesAsync();

        // Prune pages exclusively linked to this deleted job
        foreach (var pageId in jobPageIds)
        {
            bool isLinkedToOtherJobs = await _dbContext.JobPageLinks.AnyAsync(l => l.TargetPageId == pageId);
            if (!isLinkedToOtherJobs)
            {
                var pageToPrune = await _dbContext.Pages.FirstOrDefaultAsync(p => p.Id == pageId);
                if (pageToPrune != null)
                {
                    if (!string.IsNullOrEmpty(pageToPrune.StoragePath) && System.IO.File.Exists(pageToPrune.StoragePath))
                    {
                        try { System.IO.File.Delete(pageToPrune.StoragePath); } catch { }
                    }
                    _dbContext.Pages.Remove(pageToPrune);
                }
            }
        }

        await _dbContext.SaveChangesAsync();

        return Ok(new { message = "Job and exclusive site pages pruned successfully." });
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> CancelJob(string id)
    {
        var job = await _dbContext.Jobs.FirstOrDefaultAsync(j => j.Id == id);
        if (job == null) return NotFound();

        char oldStatus = job.StatusChar;
        job.Status = JobStatus.Canceled;
        job.CompletedAt = DateTime.UtcNow;

        _dbContext.JobStatusTransitions.Add(new JobStatusTransition
        {
            JobId = id,
            FromStatus = oldStatus,
            ToStatus = 'X',
            TransitionedAt = DateTime.UtcNow,
            Reason = "User requested cancellation."
        });

        await _dbContext.SaveChangesAsync();

        await _publishEndpoint.Publish(new CancelJobRequestedEvent
        {
            JobId = id,
            RequestedAt = DateTime.UtcNow
        });

        await _publishEndpoint.Publish(new JobProgressUpdatedEvent
        {
            JobId = id,
            Status = "Canceled",
            CrawledPagesCount = job.TotalPagesCrawled,
            TotalBytesDownloaded = job.TotalBytesDownloaded,
            UpdatedAt = DateTime.UtcNow
        });

        return Ok(new { message = "Job canceled successfully." });
    }
}
