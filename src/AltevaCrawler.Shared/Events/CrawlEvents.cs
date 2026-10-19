namespace AltevaCrawler.Shared.Events;

using System;

public record CrawlJobRequestedEvent
{
    public string JobId { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public uint UrlCrc32 { get; init; }
    public int MaxDepth { get; init; } = 2;
    public int MaxPagesLimit { get; init; } = 200;
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;
}

public record LinkDiscoveredEvent
{
    public string JobId { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public string TargetUrl { get; init; } = string.Empty;
    public uint TargetUrlCrc32 { get; init; }
    public int Depth { get; init; }
    public DateTime DiscoveredAt { get; init; } = DateTime.UtcNow;
}

public record PageCrawledEvent
{
    public string JobId { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public uint UrlCrc32 { get; init; }
    public ulong SimHash { get; init; }
    public string ContentHash { get; init; } = string.Empty;
    public string StoragePath { get; init; } = string.Empty;
    public int StatusCode { get; init; }
    public long ByteSize { get; init; }
    public int OutgoingLinksCount { get; init; }
    public DateTime CrawledAt { get; init; } = DateTime.UtcNow;
}

public record JobProgressUpdatedEvent
{
    public string JobId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int CrawledPagesCount { get; init; }
    public int DiscoveredPagesCount { get; init; }
    public long TotalBytesDownloaded { get; init; }
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

public record CancelJobRequestedEvent
{
    public string JobId { get; init; } = string.Empty;
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;
}

public record PauseJobRequestedEvent
{
    public string JobId { get; init; } = string.Empty;
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;
}

public record ResumeJobRequestedEvent
{
    public string JobId { get; init; } = string.Empty;
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;
}
