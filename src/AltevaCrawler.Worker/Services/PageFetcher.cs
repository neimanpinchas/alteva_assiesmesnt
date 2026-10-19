namespace AltevaCrawler.Worker.Services;

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AltevaCrawler.Shared.Utilities;

public enum FetchOutcome
{
    Success,
    Failed,
    Canceled
}

/// <summary>Result of fetching one URL: HTML on success, a classified error on failure, or a cancellation marker.</summary>
public sealed record FetchResult(FetchOutcome Outcome, int StatusCode, string Html, CrawlError? Error)
{
    public static FetchResult Succeeded(int statusCode, string html) => new(FetchOutcome.Success, statusCode, html, null);

    public static FetchResult Failed(int statusCode, CrawlError error) => new(FetchOutcome.Failed, statusCode, string.Empty, error);

    public static FetchResult Canceled { get; } = new(FetchOutcome.Canceled, 0, string.Empty, null);
}

/// <summary>Downloads a page and classifies every way it can go wrong; it never throws, cancellation is reported as a result.</summary>
public sealed class PageFetcher
{
    private const int UnknownFailureStatus = 500;

    private readonly HttpClient _httpClient;

    public PageFetcher(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<FetchResult> FetchAsync(string url, CancellationToken token)
    {
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(url, token);
            return await ReadResponseAsync(response, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return FetchResult.Canceled;
        }
        catch (Exception ex)
        {
            // Includes HttpClient timeouts (TaskCanceledException while the job is still live).
            return FetchResult.Failed(UnknownFailureStatus, CrawlErrorClassifier.FromException(ex));
        }
    }

    private static async Task<FetchResult> ReadResponseAsync(HttpResponseMessage response, CancellationToken token)
    {
        int status = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            return FetchResult.Failed(status, CrawlErrorClassifier.FromStatus(status, response.ReasonPhrase));
        }

        string contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return FetchResult.Failed(status, CrawlErrorClassifier.FromNonHtml(contentType));
        }

        return FetchResult.Succeeded(status, await response.Content.ReadAsStringAsync(token));
    }
}
