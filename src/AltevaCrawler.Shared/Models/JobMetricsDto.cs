namespace AltevaCrawler.Shared.Models;

using System;

public class DomainLinkRatioPieDto
{
    public int InternalLinks { get; set; }
    public int ExternalLinks { get; set; }
    public int TotalPages { get; set; }
    public double DomainLinkRatio { get; set; }
}

public class PageSharingDto
{
    public int UniqueToThisJob { get; set; }
    public int SharedAcrossJobs { get; set; }
}

public class BandwidthEfficiencyDto
{
    public long TotalBytesDownloaded { get; set; }
    public long EstimatedBytesSaved { get; set; }
}

public class JobMetricsDto
{
    public DomainLinkRatioPieDto DomainLinkRatioPie { get; set; } = new();
    public PageSharingDto PageSharing { get; set; } = new();
    public BandwidthEfficiencyDto BandwidthEfficiency { get; set; } = new();
    public DateTime? ForecastedCompletionTime { get; set; }
}
