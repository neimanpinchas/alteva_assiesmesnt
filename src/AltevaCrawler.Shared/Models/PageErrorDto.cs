namespace AltevaCrawler.Shared.Models;

using System;

/// <summary>A page whose fetch failed in a job (SSL/TLS, DNS, timeout, HTTP error, non-HTML, storage).</summary>
public class PageErrorDto
{
    public long PageId { get; set; }
    public string Url { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime? OccurredAt { get; set; }
}
