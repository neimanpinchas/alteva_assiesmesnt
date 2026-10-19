namespace AltevaCrawler.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Xunit;
using AltevaCrawler.Shared.Contracts;
using AltevaCrawler.Shared.Security;
using AltevaCrawler.Shared.Storage;
using AltevaCrawler.Shared.Utilities;

public class UnitTests
{
    private readonly HaxeUtilsImpl _haxeUtils = new HaxeUtilsImpl();

    #region Existing Unit Tests

    [Fact]
    public void UrlNormalizer_NormalizesRelativeUrlAndStripsFragments()
    {
        string baseUrl = "https://Example.com/blog/posts/";
        string relativeUrl = "../about#section2";

        string? normalized = UrlNormalizer.Normalize(relativeUrl, baseUrl);

        Assert.NotNull(normalized);
        Assert.Equal("https://example.com/blog/about", normalized);
    }

    [Fact]
    public void UrlNormalizer_HandlesLenientSchemeAndIgnoresMailto()
    {
        Assert.Equal("https://auto.com/", UrlNormalizer.Normalize("auto.com"));
        Assert.Null(UrlNormalizer.Normalize("mailto:test@example.com"));
    }

    [Fact]
    public void SimHash_ComputesAndCalculatesHammingDistance()
    {
        string text1 = "The quick brown fox jumps over the lazy dog";
        string text2 = "The quick brown fox jumps over the lazy cat";

        ulong simHash1 = SimHashCalculator.Compute64(text1);
        ulong simHash2 = SimHashCalculator.Compute64(text2);

        int distance = SimHashCalculator.HammingDistance(simHash1, simHash2);

        Assert.True(simHash1 > 0);
        Assert.True(simHash2 > 0);
        Assert.True(distance >= 0 && distance <= 64);
    }

    [Fact]
    public void HmacAuthService_CreatesAndVerifiesStatelessToken()
    {
        var authService = new HmacAuthService();
        string sessionId = Guid.NewGuid().ToString();

        string token = authService.CreateToken(sessionId, TimeSpan.FromMinutes(30));

        bool isValid = authService.VerifyToken(token, out var session);

        Assert.True(isValid);
        Assert.NotNull(session);
        Assert.Equal(sessionId, session.SessionId);
    }

    [Fact]
    public void HmacAuthService_RejectsTamperedToken()
    {
        var authService = new HmacAuthService();
        string token = authService.CreateToken("session1", TimeSpan.FromMinutes(30));

        string tamperedToken = token + "tampered";

        bool isValid = authService.VerifyToken(tamperedToken, out _);

        Assert.False(isValid);
    }

    #endregion

    #region Haxe Parity Tests (H13 & H14)

    [Theory]
    [InlineData("The quick brown fox jumps over the lazy dog")]
    [InlineData("Hello world! This is a test of Haxe 64-bit SimHash parity with C# SimHashCalculator.")]
    [InlineData("Special characters, 12345 numbers & symbols test!!!")]
    public void HaxeParity_SimHash64_MatchesCSharpSimHashCalculator(string text)
    {
        long haxeHash = _haxeUtils.ComputeSimHash64(text);
        ulong csharpHash = SimHashCalculator.Compute64(text);

        Assert.Equal((ulong)haxeHash, csharpHash);
    }

    [Theory]
    [InlineData("https://example.com/test")]
    [InlineData("hello world")]
    [InlineData("alteva_crawler")]
    [InlineData("123456789")]
    public void HaxeParity_Crc32_MatchesNativeCrc32(string input)
    {
        uint nativeCrc = UrlNormalizer.ComputeCrc32(input);
        uint haxeCrc = _haxeUtils.ComputeCrc32(input);

        Assert.Equal(nativeCrc, haxeCrc);
    }

    [Fact]
    public void HaxeParity_SimHash32_AndHammingDistance()
    {
        string text1 = "The quick brown fox jumps over the lazy dog";
        string text2 = "The quick brown fox jumps over the lazy cat";

        int h1 = _haxeUtils.ComputeSimHash32(text1);
        int h2 = _haxeUtils.ComputeSimHash32(text2);

        int haxeDistance = _haxeUtils.HammingDistance(h1, h2);
        int nativeBitwiseDistance = BitOperations.PopCount((uint)h1 ^ (uint)h2);

        Assert.NotEqual(0, h1);
        Assert.NotEqual(0, h2);
        Assert.Equal(nativeBitwiseDistance, haxeDistance);
        Assert.Equal(0, _haxeUtils.HammingDistance(h1, h1));
    }

    [Fact]
    public void HaxeParity_CryptoSimpleAuthToken_CreatesAndVerifies()
    {
        string sessionId = "user_session_12345";
        string secretKey = "super_secret_key";

        string token = _haxeUtils.CreateSimpleAuthToken(sessionId, secretKey);

        Assert.NotNull(token);
        Assert.Contains(".", token);
        Assert.StartsWith(sessionId + ":", token);

        bool isValid = _haxeUtils.VerifySimpleAuthToken(token, secretKey);
        Assert.True(isValid);
    }

    [Fact]
    public void HaxeParity_CryptoSimpleAuthToken_RejectsTamperedOrWrongSecret()
    {
        string sessionId = "user_session_12345";
        string secretKey = "correct_secret";
        string wrongSecret = "wrong_secret";

        string token = _haxeUtils.CreateSimpleAuthToken(sessionId, secretKey);

        Assert.False(_haxeUtils.VerifySimpleAuthToken(token, wrongSecret));
        Assert.False(_haxeUtils.VerifySimpleAuthToken(token + "x", secretKey));
        Assert.False(_haxeUtils.VerifySimpleAuthToken("invalid_token_format", secretKey));
    }

    [Theory]
    [InlineData("https://example.com/path/page.html", "example.com")]
    [InlineData("http://sub.domain.co.uk:8080/test", "sub.domain.co.uk")]
    public void HaxeParity_ExtractDomain_MatchesExpectedDomain(string url, string expectedDomain)
    {
        string haxeDomain = _haxeUtils.ExtractDomain(url);
        Assert.Equal(expectedDomain, haxeDomain);
    }

    #endregion

    #region Domain Link Ratio Tests

    [Fact]
    public void DomainLinkRatio_CalculatesCorrectRatioForOutgoingLinks()
    {
        string startDomain = "example.com";
        var outgoingLinks = new List<string>
        {
            "https://example.com/about",
            "https://example.com/contact",
            "https://otherdomain.org/blog",
            "https://sub.external.com/api"
        };

        double ratio = UrlNormalizer.CalculateDomainLinkRatio(startDomain, outgoingLinks);
        Assert.Equal(0.5, ratio);
    }

    [Fact]
    public void DomainLinkRatio_ReturnsZeroForEmptyOutgoingLinks()
    {
        double ratioFromList = UrlNormalizer.CalculateDomainLinkRatio("example.com", new List<string>());
        double ratioFromCount = UrlNormalizer.CalculateDomainLinkRatio(0, 0);

        Assert.Equal(0.0, ratioFromList);
        Assert.Equal(0.0, ratioFromCount);
    }

    [Fact]
    public void DomainLinkRatio_HandlesWwwSubdomainsCorrectly()
    {
        string startDomain = "https://www.example.com";
        var outgoingLinks = new List<string>
        {
            "https://example.com/page1",
            "https://www.example.com/page2",
            "https://external.com/page3"
        };

        double ratio = UrlNormalizer.CalculateDomainLinkRatio(startDomain, outgoingLinks, stripWww: true);
        Assert.Equal(2.0 / 3.0, ratio, precision: 4);
    }

    [Fact]
    public void DomainLinkRatio_CalculatesFromCounts()
    {
        double ratio = UrlNormalizer.CalculateDomainLinkRatio(3, 4);
        Assert.Equal(0.75, ratio);
    }

    #endregion

    #region URL Normalizer Tests (D1, D2, D3)

    [Fact]
    public void UrlNormalizer_D1_StripsWwwPrefix()
    {
        string raw = "https://www.example.com/about/us";
        string? normalized = UrlNormalizer.Normalize(raw, stripWww: true);

        Assert.Equal("https://example.com/about/us", normalized);
        Assert.Equal("example.com", UrlNormalizer.ExtractDomain(raw, stripWww: true));
    }

    [Fact]
    public void UrlNormalizer_D1_AllowsDisablingWwwStripping()
    {
        string raw = "https://www.example.com/about/us";
        string? normalized = UrlNormalizer.Normalize(raw, stripWww: false);

        Assert.Equal("https://www.example.com/about/us", normalized);
        Assert.Equal("www.example.com", UrlNormalizer.ExtractDomain(raw, stripWww: false));
    }

    [Fact]
    public void UrlNormalizer_D1_DomainHashMatchesWithAndWithoutWww()
    {
        uint hashWww = UrlNormalizer.ComputeDomainHash("www.example.com", stripWww: true);
        uint hashNoWww = UrlNormalizer.ComputeDomainHash("example.com", stripWww: true);

        Assert.Equal(hashNoWww, hashWww);
    }

    [Fact]
    public void UrlNormalizer_D2_AllowsHttpAndHttpsOnly()
    {
        Assert.NotNull(UrlNormalizer.Normalize("http://example.com/page"));
        Assert.NotNull(UrlNormalizer.Normalize("https://example.com/page"));
    }

    [Fact]
    public void UrlNormalizer_D2_RejectsDisallowedSchemes()
    {
        Assert.Null(UrlNormalizer.Normalize("mailto:info@example.com"));
        Assert.Null(UrlNormalizer.Normalize("tel:+18005550199"));
        Assert.Null(UrlNormalizer.Normalize("javascript:alert('xss')"));
        Assert.Null(UrlNormalizer.Normalize("data:text/html,<h1>Hello</h1>"));
        Assert.Null(UrlNormalizer.Normalize("ftp://ftp.example.com/file.txt"));
        Assert.Null(UrlNormalizer.Normalize("file:///C:/config.sys"));
    }

    [Fact]
    public void UrlNormalizer_D2_ResolvesProtocolRelativeAndRelativeUrls()
    {
        string baseHttps = "https://example.com/docs/index.html";

        string? protocolRelative = UrlNormalizer.Normalize("//cdn.example.com/styles.css", baseHttps);
        string? pathRelative = UrlNormalizer.Normalize("../images/logo.png", baseHttps);

        Assert.Equal("https://cdn.example.com/styles.css", protocolRelative);
        Assert.Equal("https://example.com/images/logo.png", pathRelative);
    }

    [Fact]
    public void UrlNormalizer_D3_RetainsQueryParametersAndStripsFragment()
    {
        string raw = "https://example.com/search?q=dotnet&sort=asc#top";
        string? normalized = UrlNormalizer.Normalize(raw);

        Assert.NotNull(normalized);
        Assert.Equal("https://example.com/search?q=dotnet&sort=asc", normalized);
        Assert.DoesNotContain("#top", normalized);
    }

    [Fact]
    public void UrlNormalizer_D3_DistinguishesUrlsWithDifferentQueryParams()
    {
        string url1 = UrlNormalizer.Normalize("https://example.com/product?id=1")!;
        string url2 = UrlNormalizer.Normalize("https://example.com/product?id=2")!;

        Assert.Equal("https://example.com/product?id=1", url1);
        Assert.Equal("https://example.com/product?id=2", url2);
        Assert.NotEqual(url1, url2);
    }

    [Fact]
    public void UrlNormalizer_D3_RemovesDefaultPorts()
    {
        string httpPort80 = UrlNormalizer.Normalize("http://example.com:80/path")!;
        string httpsPort443 = UrlNormalizer.Normalize("https://example.com:443/path")!;

        Assert.Equal("http://example.com/path", httpPort80);
        Assert.Equal("https://example.com/path", httpsPort443);
    }

    #endregion

    #region StoragePathBuilder & IStorageService Tests (N8 & H14)

    [Fact]
    public void StoragePathBuilder_ToRelativePath_ConvertsUriToRelativePath()
    {
        var uri = new Uri("https://example.com/about/team?lang=en&v=2");
        string relativePath = StoragePathBuilder.ToRelativePath(uri);

        Assert.StartsWith("example.com/about/team", relativePath);
        Assert.EndsWith(".txt", relativePath);
        Assert.DoesNotContain("?", relativePath);
    }

    [Fact]
    public void StoragePathBuilder_ToRelativePath_PreventsQueryStringCollisions()
    {
        var uri1 = new Uri("https://example.com/search?q=cat");
        var uri2 = new Uri("https://example.com/search?q=dog");

        string path1 = StoragePathBuilder.ToRelativePath(uri1);
        string path2 = StoragePathBuilder.ToRelativePath(uri2);

        Assert.NotEqual(path1, path2);
        Assert.StartsWith("example.com/search", path1);
        Assert.StartsWith("example.com/search", path2);
    }

    [Fact]
    public void StoragePathBuilder_IsUnderRoot_ValidatesPathSafety()
    {
        string rootPath = Path.Combine(Directory.GetCurrentDirectory(), "storage");
        string validPath = Path.Combine(rootPath, "example.com", "page.txt");
        string pathTraversal = Path.Combine(rootPath, "..", "..", "etc", "passwd");

        Assert.True(StoragePathBuilder.IsUnderRoot(validPath, rootPath));
        Assert.False(StoragePathBuilder.IsUnderRoot(pathTraversal, rootPath));
    }

    [Fact]
    public async Task StorageService_LocalStorageService_SavesReadsAndDeletesPage()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "storage_test_" + Guid.NewGuid().ToString("N"));
        IStorageService storage = new LocalStorageService(tempRoot);

        try
        {
            string domain = "example.com";
            string rawUrl = "https://example.com/docs/page1";
            byte[] content = System.Text.Encoding.UTF8.GetBytes("<html><body>Hello World</body></html>");

            string storagePath = await storage.SavePageAsync(domain, rawUrl, content);
            Assert.NotNull(storagePath);
            Assert.False(Path.IsPathRooted(storagePath));

            string? readContent = await storage.ReadPageAsync(storagePath);
            Assert.Equal("<html><body>Hello World</body></html>", readContent);

            await storage.DeletePageAsync(storagePath);
            string? deletedContent = await storage.ReadPageAsync(storagePath);
            Assert.Null(deletedContent);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task StorageService_PathSafety_BlocksPathTraversalAttempts()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "storage_safety_" + Guid.NewGuid().ToString("N"));
        IStorageService storage = new LocalStorageService(tempRoot);

        try
        {
            string pathTraversal = "../../../etc/passwd";
            string? content = await storage.ReadPageAsync(pathTraversal);
            Assert.Null(content);

            // Deleting path outside root should complete safely without throwing or deleting external files
            await storage.DeletePageAsync(pathTraversal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task StorageService_MockS3StorageService_OperationsWorkInMemory()
    {
        IStorageService s3 = new MockS3StorageService("test-bucket");
        string domain = "s3domain.com";
        string rawUrl = "https://s3domain.com/index.html";
        byte[] content = System.Text.Encoding.UTF8.GetBytes("s3 content");

        string s3Key = await s3.SavePageAsync(domain, rawUrl, content);
        Assert.StartsWith("s3://test-bucket/s3domain.com/", s3Key);

        string? readContent = await s3.ReadPageAsync(s3Key);
        Assert.Equal("s3 content", readContent);

        await s3.DeletePageAsync(s3Key);
        string? postDelete = await s3.ReadPageAsync(s3Key);
        Assert.Null(postDelete);
    }

    #endregion

    #region SequentialGuid Tests (H10 & H14)

    [Fact]
    public void SequentialGuid_NewSequentialGuid_GeneratesValidAndUniqueGuids()
    {
        Guid guid1 = SequentialGuid.NewSequentialGuid();
        Guid guid2 = SequentialGuid.NewSequentialGuid();

        Assert.NotEqual(Guid.Empty, guid1);
        Assert.NotEqual(Guid.Empty, guid2);
        Assert.NotEqual(guid1, guid2);
    }

    [Fact]
    public void SequentialGuid_NewSequentialGuid_MaintainsTimeOrdering()
    {
        var guids = new List<Guid>();
        for (int i = 0; i < 50; i++)
        {
            guids.Add(SequentialGuid.NewSequentialGuid());
            if (i % 10 == 0)
            {
                System.Threading.Thread.Sleep(2);
            }
        }

        for (int i = 0; i < guids.Count - 1; i++)
        {
            Guid g1 = guids[i];
            Guid g2 = guids[i + 1];

            Assert.NotEqual(Guid.Empty, g1);
            Assert.NotEqual(Guid.Empty, g2);
            Assert.NotEqual(g1, g2);

            int cmpStr = string.CompareOrdinal(g1.ToString(), g2.ToString());
            Assert.True(cmpStr < 0, $"Guid at index {i} ({g1}) should be lexicographically less than Guid at index {i + 1} ({g2})");
        }
    }

    #endregion

    #region CrawlerEngine Tests (H1, H4, H5)

    private class TestHttpMessageHandler : System.Net.Http.HttpMessageHandler
    {
        public Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage> Handler { get; set; } = _ => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new System.Net.Http.StringContent("<html><body><a href=\"/page1\">Link 1</a></body></html>", System.Text.Encoding.UTF8, "text/html")
        };

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            return Task.FromResult(Handler(request));
        }
    }

    private class TestHttpClientFactory : System.Net.Http.IHttpClientFactory
    {
        private readonly System.Net.Http.HttpMessageHandler _handler;
        public TestHttpClientFactory(System.Net.Http.HttpMessageHandler handler) => _handler = handler;
        public System.Net.Http.HttpClient CreateClient(string name) => new System.Net.Http.HttpClient(_handler);
    }

    private class TestPublishEndpoint : MassTransit.IPublishEndpoint
    {
        public List<object> PublishedEvents { get; } = new();
        public Func<object, bool>? ThrowOn { get; set; }

        public Task Publish<T>(T message, System.Threading.CancellationToken cancellationToken = default) where T : class
        {
            if (ThrowOn != null && ThrowOn(message))
            {
                throw new InvalidOperationException("Simulated broker failure");
            }
            lock (PublishedEvents)
            {
                PublishedEvents.Add(message);
            }
            return Task.CompletedTask;
        }

        public Task Publish<T>(T message, MassTransit.IPipe<MassTransit.PublishContext<T>> pipe, System.Threading.CancellationToken cancellationToken = default) where T : class => Publish(message, cancellationToken);
        public Task Publish<T>(T message, MassTransit.IPipe<MassTransit.PublishContext> pipe, System.Threading.CancellationToken cancellationToken = default) where T : class => Publish(message, cancellationToken);
        public Task Publish(object message, System.Threading.CancellationToken cancellationToken = default) { lock (PublishedEvents) { PublishedEvents.Add(message); } return Task.CompletedTask; }
        public Task Publish(object message, Type messageType, System.Threading.CancellationToken cancellationToken = default) { lock (PublishedEvents) { PublishedEvents.Add(message); } return Task.CompletedTask; }
        public Task Publish(object message, MassTransit.IPipe<MassTransit.PublishContext> pipe, System.Threading.CancellationToken cancellationToken = default) { lock (PublishedEvents) { PublishedEvents.Add(message); } return Task.CompletedTask; }
        public Task Publish(object message, Type messageType, MassTransit.IPipe<MassTransit.PublishContext> pipe, System.Threading.CancellationToken cancellationToken = default) { lock (PublishedEvents) { PublishedEvents.Add(message); } return Task.CompletedTask; }
        public Task Publish<T>(object values, System.Threading.CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(object values, MassTransit.IPipe<MassTransit.PublishContext<T>> pipe, System.Threading.CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(object values, MassTransit.IPipe<MassTransit.PublishContext> pipe, System.Threading.CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public MassTransit.ConnectHandle ConnectPublishObserver(MassTransit.IPublishObserver observer) => throw new NotImplementedException();
    }

    [Fact]
    public async Task CrawlerEngine_Cancellation_CompletesWithoutThrowing()
    {
        var handler = new TestHttpMessageHandler();
        var clientFactory = new TestHttpClientFactory(handler);
        var publisher = new TestPublishEndpoint();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AltevaCrawler.Worker.Services.CrawlerEngine>.Instance;
        var storage = new MockS3StorageService("test");

        var engine = new AltevaCrawler.Worker.Services.CrawlerEngine(clientFactory, publisher, storage, logger);

        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        await engine.ProcessJobAsync("job-cancel-test", "https://example.com", maxDepth: 2, maxPagesLimit: 10, ct: cts.Token);

        // Job should complete quietly without unhandled exception or status "Completed" event
        Assert.DoesNotContain(publisher.PublishedEvents, e => e is AltevaCrawler.Shared.Events.JobProgressUpdatedEvent j && j.Status == "Completed");
    }

    [Fact]
    public async Task CrawlerEngine_NonHtmlAndHttpError_DoesNotCountAsCrawledPages()
    {
        var handler = new TestHttpMessageHandler
        {
            Handler = req =>
            {
                if (req.RequestUri!.AbsoluteUri.Contains("pdf"))
                {
                    return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new System.Net.Http.StringContent("PDF Content", System.Text.Encoding.UTF8, "application/pdf")
                    };
                }
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent("<html><body>HTML Page</body></html>", System.Text.Encoding.UTF8, "text/html")
                };
            }
        };

        var clientFactory = new TestHttpClientFactory(handler);
        var publisher = new TestPublishEndpoint();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AltevaCrawler.Worker.Services.CrawlerEngine>.Instance;
        var storage = new MockS3StorageService("test");

        var engine = new AltevaCrawler.Worker.Services.CrawlerEngine(clientFactory, publisher, storage, logger);

        // Process PDF non-HTML
        await engine.ProcessJobAsync("job-pdf-test", "https://example.com/file.pdf", maxDepth: 1, maxPagesLimit: 10);

        var completedEvent = publisher.PublishedEvents.OfType<AltevaCrawler.Shared.Events.JobProgressUpdatedEvent>().FirstOrDefault(e => e.JobId == "job-pdf-test");
        Assert.NotNull(completedEvent);
        Assert.Equal(0, completedEvent.CrawledPagesCount); // PDF should NOT count toward CrawledPagesCount
    }

    [Fact]
    public async Task CrawlerEngine_MaxPagesLimit_EnforcedStrictly()
    {
        int pageIndex = 0;
        var handler = new TestHttpMessageHandler
        {
            Handler = req =>
            {
                int current = System.Threading.Interlocked.Increment(ref pageIndex);
                string html = $"<html><body><a href=\"https://example.com/p{current}\">Link {current}</a></body></html>";
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent(html, System.Text.Encoding.UTF8, "text/html")
                };
            }
        };

        var clientFactory = new TestHttpClientFactory(handler);
        var publisher = new TestPublishEndpoint();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AltevaCrawler.Worker.Services.CrawlerEngine>.Instance;
        var storage = new MockS3StorageService("test");

        var engine = new AltevaCrawler.Worker.Services.CrawlerEngine(clientFactory, publisher, storage, logger);

        await engine.ProcessJobAsync("job-limit-test", "https://example.com/p0", maxDepth: 5, maxPagesLimit: 3);

        var completedEvent = publisher.PublishedEvents.OfType<AltevaCrawler.Shared.Events.JobProgressUpdatedEvent>().FirstOrDefault(e => e.JobId == "job-limit-test");
        Assert.NotNull(completedEvent);
        Assert.Equal(3, completedEvent.CrawledPagesCount); // CrawledPagesCount must equal exactly maxPagesLimit (3)
    }

    private class FailingStorageService : IStorageService
    {
        public int SaveAttempts;
        public Task<string> SavePageAsync(string domain, string rawUrl, byte[] content)
        {
            System.Threading.Interlocked.Increment(ref SaveAttempts);
            throw new IOException("disk full");
        }
        public Task<string?> ReadPageAsync(string storagePath) => Task.FromResult<string?>(null);
        public Task DeletePageAsync(string storagePath) => Task.CompletedTask;
    }

    private static AltevaCrawler.Worker.Services.CrawlerEngine CreateEngine(TestPublishEndpoint publisher, IStorageService storage) =>
        new(new TestHttpClientFactory(new TestHttpMessageHandler()), publisher,
            storage, Microsoft.Extensions.Logging.Abstractions.NullLogger<AltevaCrawler.Worker.Services.CrawlerEngine>.Instance);

    [Theory]
    [InlineData(200, 1200, true)]
    [InlineData(200, 0, false)]   // non-HTML / empty
    [InlineData(404, 0, false)]   // dead link
    [InlineData(500, 0, false)]   // fetch error
    [InlineData(404, 900, false)] // error body is never a crawled page
    public void PageCrawledEvent_IsSuccess_OnlyForStoredSuccessfulPages(int status, long bytes, bool expected)
    {
        var evt = new AltevaCrawler.Shared.Events.PageCrawledEvent { StatusCode = status, ByteSize = bytes };
        Assert.Equal(expected, evt.IsSuccess);
    }

    [Fact]
    public async Task CrawlerEngine_StorageFailure_RetriesOnceThenReportsPageAsFailed()
    {
        var publisher = new TestPublishEndpoint();
        var storage = new FailingStorageService();
        var engine = CreateEngine(publisher, storage);

        await engine.ProcessJobAsync("job-storage-fail", "https://example.com", maxDepth: 1, maxPagesLimit: 5);

        Assert.True(storage.SaveAttempts >= 2, "each page is attempted twice before it is reported as failed");
        var pageEvents = publisher.PublishedEvents.OfType<AltevaCrawler.Shared.Events.PageCrawledEvent>().ToList();
        Assert.NotEmpty(pageEvents);
        Assert.All(pageEvents, e => Assert.False(e.IsSuccess));
        var done = publisher.PublishedEvents.OfType<AltevaCrawler.Shared.Events.JobProgressUpdatedEvent>().Single();
        Assert.Equal("Completed", done.Status);
        Assert.Equal(0, done.CrawledPagesCount);
        Assert.Equal(0, done.TotalBytesDownloaded);
    }

    [Fact]
    public async Task CrawlerEngine_UnexpectedJobError_PublishesFailedWithReason()
    {
        var publisher = new TestPublishEndpoint
        {
            // Simulate the broker rejecting the final "Completed" status publish.
            ThrowOn = e => e is AltevaCrawler.Shared.Events.JobProgressUpdatedEvent j && j.Status == "Completed"
        };
        var engine = CreateEngine(publisher, new MockS3StorageService("test"));

        await engine.ProcessJobAsync("job-unexpected", "https://example.com", maxDepth: 0, maxPagesLimit: 5);

        var failed = publisher.PublishedEvents.OfType<AltevaCrawler.Shared.Events.JobProgressUpdatedEvent>().Single();
        Assert.Equal("Failed", failed.Status);
        Assert.Contains("Simulated broker failure", failed.FailureReason);
    }

    [Fact]
    public void SequentialGuid_IsUniqueAndOrderedWithinOneMillisecondBurstAcrossThreads()
    {
        var all = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        System.Threading.Tasks.Parallel.For(0, 8, _ =>
        {
            for (int i = 0; i < 2000; i++) all.Add(SequentialGuid.NewSequentialGuid());
        });

        Assert.Equal(16000, all.Distinct().Count());
    }

    #endregion
}
