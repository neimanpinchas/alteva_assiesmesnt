namespace AltevaCrawler.Tests;

using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using AltevaCrawler.Shared.Contracts;
using AltevaCrawler.Shared.Security;
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

    #region Haxe Parity Tests

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

        // 2 out of 4 links are in example.com
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

        // With stripWww = true (default), www.example.com and example.com are the same domain
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

    #region StoragePathBuilder Tests (N8)

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
        string rootPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "storage");
        string validPath = System.IO.Path.Combine(rootPath, "example.com", "page.txt");
        string pathTraversal = System.IO.Path.Combine(rootPath, "..", "..", "etc", "passwd");

        Assert.True(StoragePathBuilder.IsUnderRoot(validPath, rootPath));
        Assert.False(StoragePathBuilder.IsUnderRoot(pathTraversal, rootPath));
    }

    #endregion
}
