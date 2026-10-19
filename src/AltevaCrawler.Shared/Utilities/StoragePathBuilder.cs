namespace AltevaCrawler.Shared.Utilities;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public static class StoragePathBuilder
{
    /// <summary>
    /// Converts a URL path and query string safely into a relative file path (e.g. {domain}/{safePath}.txt),
    /// handling query strings without collisions and sanitizing invalid characters.
    /// </summary>
    public static string ToRelativePath(Uri uri)
    {
        if (uri == null)
        {
            throw new ArgumentNullException(nameof(uri));
        }

        string domain = uri.Host;
        if (string.IsNullOrWhiteSpace(domain))
        {
            domain = "unknown";
        }
        domain = SanitizeSegment(domain);

        string rawPath = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrEmpty(rawPath))
        {
            rawPath = "index";
        }

        var segments = rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var sanitizedSegments = new List<string>();
        foreach (var segment in segments)
        {
            string s = SanitizeSegment(segment);
            if (!string.IsNullOrEmpty(s))
            {
                sanitizedSegments.Add(s);
            }
        }

        string safePath = sanitizedSegments.Count > 0
            ? string.Join("/", sanitizedSegments)
            : "index";

        if (!string.IsNullOrEmpty(uri.Query))
        {
            byte[] queryBytes = Encoding.UTF8.GetBytes(uri.Query);
            byte[] hash = SHA256.HashData(queryBytes);
            string hashHex = Convert.ToHexString(hash)[..8].ToLowerInvariant();

            string sanitizedQuery = SanitizeSegment(uri.Query.TrimStart('?'));
            if (sanitizedQuery.Length > 20)
            {
                sanitizedQuery = sanitizedQuery[..20];
            }

            if (!string.IsNullOrEmpty(sanitizedQuery))
            {
                safePath += $"_{sanitizedQuery}_{hashHex}";
            }
            else
            {
                safePath += $"_{hashHex}";
            }
        }

        if (!safePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            safePath += ".txt";
        }

        return $"{domain}/{safePath}";
    }

    /// <summary>
    /// Validates that the resolved fullPath stays within the specified storage rootPath.
    /// </summary>
    public static bool IsUnderRoot(string fullPath, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(rootPath))
        {
            return false;
        }

        try
        {
            string fullRoot = Path.GetFullPath(rootPath);
            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                fullRoot += Path.DirectorySeparatorChar;
            }

            string resolvedPath = Path.GetFullPath(fullPath);

            return resolvedPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ||
                   resolvedPath.Equals(fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string SanitizeSegment(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var invalidChars = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).Concat(new[] { '?', '*', ':', '\\', '<', '>', '|', '"' }).Distinct().ToArray();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (Array.IndexOf(invalidChars, c) >= 0)
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
