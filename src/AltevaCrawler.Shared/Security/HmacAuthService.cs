namespace AltevaCrawler.Shared.Security;

using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public class SessionToken
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class HmacAuthService
{
    private readonly byte[] _signingKey;

    public HmacAuthService(byte[]? keySeed = null)
    {
        _signingKey = keySeed ?? RandomNumberGenerator.GetBytes(32);
    }

    public string CreateToken(string sessionId, TimeSpan validity)
    {
        var session = new SessionToken
        {
            SessionId = sessionId,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(validity)
        };

        string payloadJson = JsonSerializer.Serialize(session);
        string payloadB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson));

        using var hmac = new HMACSHA256(_signingKey);
        byte[] sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
        string sigB64 = Convert.ToBase64String(sigBytes);

        return $"{payloadB64}.{sigB64}";
    }

    public bool VerifyToken(string token, out SessionToken? session)
    {
        session = null;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var parts = token.Split('.');
        if (parts.Length != 2) return false;

        string payloadB64 = parts[0];
        string sigB64 = parts[1];

        try
        {
            using var hmac = new HMACSHA256(_signingKey);
            byte[] expectedSig = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
            byte[] actualSig = Convert.FromBase64String(sigB64);

            if (!CryptographicOperations.FixedTimeEquals(expectedSig, actualSig))
                return false;

            string payloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(payloadB64));
            session = JsonSerializer.Deserialize<SessionToken>(payloadJson);

            if (session == null || session.ExpiresAt < DateTime.UtcNow)
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }
}
