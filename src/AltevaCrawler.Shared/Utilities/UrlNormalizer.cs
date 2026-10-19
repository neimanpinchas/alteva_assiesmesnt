namespace AltevaCrawler.Shared.Utilities;

using System;
using System.IO.Hashing;
using System.Text;

public static class UrlNormalizer
{
    public static string? Normalize(string rawUrl, string? baseUrl = null)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return null;

        string trimmed = rawUrl.Trim();

        // Filter non-http schemes
        if (trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Auto-prepend https:// if no scheme is provided and no baseUrl exists
        if (string.IsNullOrEmpty(baseUrl) &&
            !trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("/") &&
            !trimmed.StartsWith("."))
        {
            trimmed = "https://" + trimmed;
        }

        Uri baseUri;
        Uri fullUri;

        try
        {
            if (!string.IsNullOrEmpty(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedBase))
            {
                baseUri = parsedBase;
                if (!Uri.TryCreate(baseUri, trimmed, out fullUri!))
                {
                    return null;
                }
            }
            else
            {
                if (!Uri.TryCreate(trimmed, UriKind.Absolute, out fullUri!))
                {
                    return null;
                }
            }
        }
        catch
        {
            return null;
        }

        if (fullUri.Scheme != Uri.UriSchemeHttp && fullUri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var builder = new UriBuilder(fullUri)
        {
            Fragment = string.Empty,
            Host = fullUri.Host.ToLowerInvariant(),
            Scheme = fullUri.Scheme.ToLowerInvariant()
        };

        string path = builder.Path;
        if (path.Length > 1 && path.EndsWith('/'))
        {
            builder.Path = path.TrimEnd('/');
        }

        return builder.Uri.AbsoluteUri;
    }

    public static string ExtractDomain(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Host.ToLowerInvariant();
        }
        return string.Empty;
    }

    public static uint ComputeCrc32(string input)
    {
        if (string.IsNullOrEmpty(input)) return 0;
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        return Crc32.HashToUInt32(bytes);
    }

    public static uint ComputeDomainHash(string domain)
    {
        return ComputeCrc32(domain.ToLowerInvariant());
    }
}
