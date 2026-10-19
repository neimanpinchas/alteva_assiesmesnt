namespace AltevaCrawler.Shared.Contracts;

public class HaxeUtilsImpl : IUrlUtils, ISimHash, ICrypto
{
    public uint ComputeCrc32(string input)
    {
        return (uint)global::UrlUtils.crc32(input ?? string.Empty);
    }

    public string ExtractDomain(string url)
    {
        return global::UrlUtils.extractDomain(url ?? string.Empty);
    }

    public int ComputeSimHash32(string text)
    {
        return global::SimHash.computeSimHash32(text ?? string.Empty);
    }

    public long ComputeSimHash64(string text)
    {
        return global::SimHash.computeSimHash64(text ?? string.Empty);
    }

    public int HammingDistance(float h1, float h2)
    {
        return global::SimHash.hammingDistance(h1, h2);
    }

    public int HammingDistance64(long h1, long h2)
    {
        return global::SimHash.hammingDistance64(h1, h2);
    }

    public string CreateSimpleAuthToken(string sessionId, string secretKey)
    {
        return global::Crypto.createSimpleAuthToken(sessionId ?? string.Empty, secretKey ?? string.Empty);
    }

    public bool VerifySimpleAuthToken(string token, string secretKey)
    {
        return global::Crypto.verifySimpleAuthToken(token ?? string.Empty, secretKey ?? string.Empty);
    }
}

public class HaxeUrlUtils : IUrlUtils
{
    public uint ComputeCrc32(string input) => (uint)global::UrlUtils.crc32(input ?? string.Empty);
    public string ExtractDomain(string url) => global::UrlUtils.extractDomain(url ?? string.Empty);
}

public class HaxeSimHash : ISimHash
{
    public int ComputeSimHash32(string text) => global::SimHash.computeSimHash32(text ?? string.Empty);
    public long ComputeSimHash64(string text) => global::SimHash.computeSimHash64(text ?? string.Empty);
    public int HammingDistance(float h1, float h2) => global::SimHash.hammingDistance(h1, h2);
    public int HammingDistance64(long h1, long h2) => global::SimHash.hammingDistance64(h1, h2);
}

public class HaxeCrypto : ICrypto
{
    public string CreateSimpleAuthToken(string sessionId, string secretKey) =>
        global::Crypto.createSimpleAuthToken(sessionId ?? string.Empty, secretKey ?? string.Empty);

    public bool VerifySimpleAuthToken(string token, string secretKey) =>
        global::Crypto.verifySimpleAuthToken(token ?? string.Empty, secretKey ?? string.Empty);
}
