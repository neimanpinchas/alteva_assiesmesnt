namespace AltevaCrawler.Tests;

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AltevaCrawler.Worker.Services;
using Xunit;

public class CrawlJobStateTests
{
    [Fact]
    public void TryReserveSlot_StopsAtLimitAndLeavesCountersUntouched()
    {
        var state = new CrawlJobState(2);

        Assert.True(state.TryReserveSlot(10));
        Assert.True(state.TryReserveSlot(20));
        Assert.False(state.TryReserveSlot(40));

        Assert.Equal(2, state.CrawledCount);
        Assert.Equal(30, state.TotalBytes);
        Assert.False(state.HasCapacity);
    }

    [Fact]
    public void ReleaseSlot_RestoresCapacityAndBytes()
    {
        var state = new CrawlJobState(1);
        state.TryReserveSlot(10);

        state.ReleaseSlot(10);

        Assert.Equal(0, state.CrawledCount);
        Assert.Equal(0, state.TotalBytes);
        Assert.True(state.HasCapacity);
    }

    [Fact]
    public void TryMarkDiscovered_IsTrueOnlyTheFirstTime()
    {
        var state = new CrawlJobState(5);

        Assert.True(state.TryMarkDiscovered(7));
        Assert.False(state.TryMarkDiscovered(7));
        Assert.Equal(1, state.DiscoveredCount);
    }

    [Fact]
    public void TryReserveSlot_NeverExceedsLimitUnderConcurrency()
    {
        var state = new CrawlJobState(100);

        Parallel.For(0, 1000, _ => state.TryReserveSlot(1));

        Assert.Equal(100, state.CrawledCount);
        Assert.Equal(100, state.TotalBytes);
    }
}

public class JobOutcomesTests
{
    [Fact]
    public void Final_FailsWithReasonWhenNothingCrawledAndStartPageFailed()
    {
        var state = new CrawlJobState(10);
        state.RecordStartPageError("certificate expired");

        var result = JobOutcomes.Final("job", state);

        Assert.Equal(JobOutcomes.FailedStatus, result.Status);
        Assert.Equal("Start page failed: certificate expired", result.FailureReason);
    }

    [Fact]
    public void Final_CompletesWhenAtLeastOnePageWasCrawled()
    {
        var state = new CrawlJobState(10);
        state.RecordStartPageError("ignored once pages were crawled");
        state.TryReserveSlot(5);

        var result = JobOutcomes.Final("job", state);

        Assert.Equal(JobOutcomes.CompletedStatus, result.Status);
        Assert.Equal(1, result.CrawledPagesCount);
        Assert.Equal(5, result.TotalBytesDownloaded);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Crashed_CarriesExceptionTypeAndMessage()
    {
        var result = JobOutcomes.Crashed("job", new InvalidOperationException("boom"), new CrawlJobState(1));

        Assert.Equal("InvalidOperationException: boom", result.FailureReason);
    }
}

public class PageLinkExtractorTests
{
    [Fact]
    public void Extract_ResolvesRelativeLinksAndSkipsInvalidOnes()
    {
        const string html = "<html><body>hi<a href='/about'>a</a><a href='mailto:x@y.z'>m</a><a href=''>e</a></body></html>";

        var page = PageLinkExtractor.Extract(html, "https://example.com/");

        var link = Assert.Single(page.Links);
        Assert.Equal("https://example.com/about", link.Url);
    }

    [Fact]
    public void Extract_ReturnsNoLinksForPageWithoutAnchors()
    {
        var page = PageLinkExtractor.Extract("<p>plain</p>", "https://example.com/");

        Assert.Empty(page.Links);
    }
}

public class PageFetcherTests
{
    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond());
    }

    private static PageFetcher FetcherReturning(Func<HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)));

    [Fact]
    public async Task FetchAsync_ReturnsHtmlForSuccessfulHtmlResponse()
    {
        var fetcher = FetcherReturning(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<p>ok</p>", System.Text.Encoding.UTF8, "text/html")
        });

        var result = await fetcher.FetchAsync("https://example.com/", CancellationToken.None);

        Assert.Equal(FetchOutcome.Success, result.Outcome);
        Assert.Equal("<p>ok</p>", result.Html);
    }

    [Fact]
    public async Task FetchAsync_ClassifiesHttpErrorAndNonHtml()
    {
        var notFound = await FetcherReturning(() => new HttpResponseMessage(HttpStatusCode.NotFound))
            .FetchAsync("https://example.com/", CancellationToken.None);
        var json = await FetcherReturning(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            })
            .FetchAsync("https://example.com/", CancellationToken.None);

        Assert.Equal(FetchOutcome.Failed, notFound.Outcome);
        Assert.Equal(404, notFound.StatusCode);
        Assert.Equal(FetchOutcome.Failed, json.Outcome);
        Assert.NotNull(json.Error);
    }

    [Fact]
    public async Task FetchAsync_ReportsCancellationAsResultNotException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await FetcherReturning(() => new HttpResponseMessage(HttpStatusCode.OK))
            .FetchAsync("https://example.com/", cts.Token);

        Assert.Equal(FetchOutcome.Canceled, result.Outcome);
    }
}
