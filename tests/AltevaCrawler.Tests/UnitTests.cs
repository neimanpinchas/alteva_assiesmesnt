namespace AltevaCrawler.Tests;

using System;
using Xunit;
using AltevaCrawler.Shared.Security;
using AltevaCrawler.Shared.Utilities;

public class UnitTests
{
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
}
