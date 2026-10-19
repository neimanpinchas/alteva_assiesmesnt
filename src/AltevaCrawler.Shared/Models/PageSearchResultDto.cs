namespace AltevaCrawler.Shared.Models;

public class PageSearchResultDto
{
    public long Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public ulong SimHash { get; set; }
    public string SimHashHex => SimHash.ToString("X16");
    public int HammingDistance { get; set; }
}
