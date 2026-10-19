namespace AltevaCrawler.Shared.Entities;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

[Table("Pages")]
[Index(nameof(UrlCrc32), Name = "IX_Pages_UrlCrc32")]
[Index(nameof(DomainHash), Name = "IX_Pages_DomainHash")]
[Index(nameof(IsCrawled), Name = "IX_Pages_IsCrawled")]
public class Page
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [MaxLength(2048)]
    public string Url { get; set; } = string.Empty;

    [Required]
    [MaxLength(2048)]
    public string NormalizedUrl { get; set; } = string.Empty;

    public uint UrlCrc32 { get; set; }

    public uint DomainHash { get; set; }

    [Required]
    [MaxLength(255)]
    public string Domain { get; set; } = string.Empty;

    public ulong SimHash { get; set; }

    [MaxLength(64)]
    public string? ContentHash { get; set; }

    [MaxLength(512)]
    public string? StoragePath { get; set; }

    public int StatusCode { get; set; }

    public int OutgoingLinksCount { get; set; }

    public bool IsCrawled { get; set; }

    public DateTime? CrawledAt { get; set; }

    [InverseProperty(nameof(JobPageLink.SourcePage))]
    public ICollection<JobPageLink> OutgoingLinks { get; set; } = new List<JobPageLink>();

    [InverseProperty(nameof(JobPageLink.TargetPage))]
    public ICollection<JobPageLink> IncomingLinks { get; set; } = new List<JobPageLink>();
}
