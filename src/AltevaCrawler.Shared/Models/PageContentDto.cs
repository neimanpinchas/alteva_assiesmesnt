namespace AltevaCrawler.Shared.Models;

public class PageContentDto
{
    public long PageId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string TextContent { get; set; } = string.Empty;
}
