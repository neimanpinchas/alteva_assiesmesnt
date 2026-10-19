namespace AltevaCrawler.Worker.Services;

using System.Collections.Generic;
using HtmlAgilityPack;
using AltevaCrawler.Shared.Utilities;

/// <summary>A normalized outgoing link together with its URL hash.</summary>
public readonly record struct CrawlLink(string Url, uint UrlCrc);

/// <summary>The data the crawler derives from one HTML document.</summary>
public sealed record ExtractedPage(ulong SimHash, IReadOnlyList<CrawlLink> Links);

/// <summary>Pure HTML analysis: text fingerprint and normalized outgoing links.</summary>
public static class PageLinkExtractor
{
    public static ExtractedPage Extract(string html, string pageUrl)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        ulong simHash = SimHashCalculator.Compute64(doc.DocumentNode.InnerText);
        return new ExtractedPage(simHash, ExtractLinks(doc, pageUrl));
    }

    private static List<CrawlLink> ExtractLinks(HtmlDocument doc, string pageUrl)
    {
        var links = new List<CrawlLink>();
        var hrefNodes = doc.DocumentNode.SelectNodes("//a[@href]");
        if (hrefNodes == null) return links;

        foreach (var node in hrefNodes)
        {
            string? normalized = UrlNormalizer.Normalize(node.GetAttributeValue("href", string.Empty), pageUrl);
            if (normalized != null)
            {
                links.Add(new CrawlLink(normalized, UrlNormalizer.ComputeCrc32(normalized)));
            }
        }

        return links;
    }
}
