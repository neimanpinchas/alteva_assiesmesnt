namespace AltevaCrawler.Shared.Contracts;

public interface IUrlUtils
{
    uint ComputeCrc32(string input);
    string ExtractDomain(string url);
}

public interface ISimHash
{
    int ComputeSimHash32(string text);
    long ComputeSimHash64(string text);
    int HammingDistance(float h1, float h2);
    int HammingDistance64(long h1, long h2);
}

public interface ICrypto
{
    string CreateSimpleAuthToken(string sessionId, string secretKey);
    bool VerifySimpleAuthToken(string token, string secretKey);
}
