namespace AltevaCrawler.Shared.Models;

using System.Collections.Generic;

public class TreeNodeDto
{
    public long Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public uint UrlCrc32 { get; set; }
    public string Domain { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public ulong SimHash { get; set; }
    public string SimHashHex => SimHash.ToString("X16");
    public int OutgoingLinksCount { get; set; }
    public double OnTheFlyDomainLinkRatio { get; set; }
    public bool IsCrawled { get; set; }
    public List<TreeNodeDto> Children { get; set; } = new();
}
