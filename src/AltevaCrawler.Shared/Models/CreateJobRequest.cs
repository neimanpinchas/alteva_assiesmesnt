namespace AltevaCrawler.Shared.Models;

public record CreateJobRequest(string Url, int? MaxDepth, int? MaxPagesLimit);
