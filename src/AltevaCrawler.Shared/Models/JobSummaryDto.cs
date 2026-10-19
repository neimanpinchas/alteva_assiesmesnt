namespace AltevaCrawler.Shared.Models;

using System;
using System.Collections.Generic;

public class JobSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int MaxDepth { get; set; }
    public int MaxPagesLimit { get; set; } = 200;
    public string Status { get; set; } = string.Empty;
    public int TotalPagesCrawled { get; set; }
    public int DiscoveredLinksCount { get; set; }
    public long TotalBytesDownloaded { get; set; }
    public double DomainLinkRatio { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
    public List<JobStatusHistoryDto> StatusHistory { get; set; } = new();
}

public class JobStatusHistoryDto
{
    public char FromStatus { get; set; }
    public char ToStatus { get; set; }
    public DateTime TransitionedAt { get; set; }
    public string? Reason { get; set; }
}
