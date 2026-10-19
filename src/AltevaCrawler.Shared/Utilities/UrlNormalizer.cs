namespace AltevaCrawler.Shared.Utilities;

using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Text;

public static class UrlNormalizer
{
    public static string? Normalize(string rawUrl, string? baseUrl = null, bool stripWww = true)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return null;

        string trimmed = rawUrl.Trim();

        // Protocol-relative links
        if (trimmed.StartsWith("//"))
        {
            string scheme = "https:";
            if (!string.IsNullOrEmpty(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUriScheme))
            {
                scheme = baseUriScheme.Scheme + ":";
            }
            trimmed = scheme + trimmed;
        }
        // Auto-prepend https:// if no scheme is provided and no baseUrl exists
        else if (string.IsNullOrEmpty(baseUrl) &&
                 !trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                 !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                 !trimmed.StartsWith("/") &&
                 !trimmed.StartsWith("."))
        {
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out _))
            {
                trimmed = "https://" + trimmed;
            }
        }

        Uri fullUri;

        try
        {
            if (!string.IsNullOrEmpty(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedBase))
            {
                if (!Uri.TryCreate(parsedBase, trimmed, out fullUri!))
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

        // D2: Allow-list for http and https only
        if (fullUri.Scheme != Uri.UriSchemeHttp && fullUri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        string host = fullUri.Host.ToLowerInvariant();
        if (stripWww && host.StartsWith("www.") && host.Length > 4)
        {
            host = host.Substring(4);
        }

        var builder = new UriBuilder(fullUri)
        {
            Fragment = string.Empty,
            Host = host,
            Scheme = fullUri.Scheme.ToLowerInvariant()
        };

        if ((builder.Scheme == Uri.UriSchemeHttp && builder.Port == 80) ||
            (builder.Scheme == Uri.UriSchemeHttps && builder.Port == 443))
        {
            builder.Port = -1;
        }

        string path = builder.Path;
        if (path.Length > 1 && path.EndsWith('/'))
        {
            builder.Path = path.TrimEnd('/');
        }

        return builder.Uri.AbsoluteUri;
    }

    public static string ExtractDomain(string url, bool stripWww = true)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;

        string host;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            host = uri.Host.ToLowerInvariant();
        }
        else
        {
            string tempUrl = url.Contains("://") ? url : "https://" + url;
            if (Uri.TryCreate(tempUrl, UriKind.Absolute, out var tempUri))
            {
                host = tempUri.Host.ToLowerInvariant();
            }
            else
            {
                host = url.Trim().ToLowerInvariant();
            }
        }

        if (stripWww && host.StartsWith("www.") && host.Length > 4)
        {
            host = host.Substring(4);
        }

        return host;
    }

    public static uint ComputeCrc32(string input)
    {
        if (string.IsNullOrEmpty(input)) return 0;
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        return Crc32.HashToUInt32(bytes);
    }

    public static uint ComputeDomainHash(string domain, bool stripWww = true)
    {
        string extracted = ExtractDomain(domain, stripWww);
        return ComputeCrc32(extracted);
    }

    public static double CalculateDomainLinkRatio(string startDomain, IEnumerable<string> outgoingLinkUrls, bool stripWww = true)
    {
        if (outgoingLinkUrls == null) return 0.0;

        Dictionary<string, int> countsByDomain = outgoingLinkUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => ExtractDomain(url, stripWww))
            .GroupBy(domain => domain, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        countsByDomain.TryGetValue(ExtractDomain(startDomain, stripWww), out int sameDomainCount);
        return CalculateDomainLinkRatio(sameDomainCount, countsByDomain.Values.Sum());
    }

    public static double CalculateDomainLinkRatio(int outgoingInDomainCount, int totalOutgoingCount)
    {
        if (totalOutgoingCount <= 0) return 0.0;
        return (double)outgoingInDomainCount / totalOutgoingCount;
    }
}
