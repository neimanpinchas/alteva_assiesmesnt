namespace AltevaCrawler.Worker.Services;

using System;
using AltevaCrawler.Shared.Events;

/// <summary>Builds the terminal <see cref="JobProgressUpdatedEvent"/> for a job. Pure, so the rules are unit testable.</summary>
public static class JobOutcomes
{
    public const string CompletedStatus = "Completed";
    public const string FailedStatus = "Failed";

    /// <summary>Completed, or Failed when nothing was crawled because the start page itself failed.</summary>
    public static JobProgressUpdatedEvent Final(string jobId, CrawlJobState state)
    {
        bool startPageFailed = state.CrawledCount == 0 && state.StartPageError != null;
        if (startPageFailed)
        {
            return Build(jobId, FailedStatus, 0, state.DiscoveredCount, 0, $"Start page failed: {state.StartPageError}");
        }

        return Build(jobId, CompletedStatus, state.CrawledCount, state.DiscoveredCount, state.TotalBytes, null);
    }

    public static JobProgressUpdatedEvent InvalidStartUrl(string jobId) =>
        Build(jobId, FailedStatus, 0, 0, 0, "Invalid start URL.");

    public static JobProgressUpdatedEvent Crashed(string jobId, Exception ex, CrawlJobState state) =>
        Build(jobId, FailedStatus, state.CrawledCount, state.DiscoveredCount, state.TotalBytes, $"{ex.GetType().Name}: {ex.Message}");

    private static JobProgressUpdatedEvent Build(
        string jobId, string status, int crawled, int discovered, long bytes, string? failureReason) =>
        new()
        {
            JobId = jobId,
            Status = status,
            CrawledPagesCount = crawled,
            DiscoveredPagesCount = discovered,
            TotalBytesDownloaded = bytes,
            FailureReason = failureReason,
            UpdatedAt = DateTime.UtcNow
        };
}
