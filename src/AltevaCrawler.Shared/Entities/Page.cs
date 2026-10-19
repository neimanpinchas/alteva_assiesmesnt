namespace AltevaCrawler.Shared.Entities;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

[Table("Pages")]
[Index(nameof(UrlCrc32), Name = "IX_Pages_UrlCrc32")]
[Index(nameof(UrlHash), Name = "UX_Pages_UrlHash", IsUnique = true)]
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

    /// <summary>
    /// CRC32 of <see cref="NormalizedUrl"/>, computed by MySQL as a persisted (STORED) generated column
    /// (see <c>AppDbContext</c>) and indexed. Never assigned from code: lookups filter on this integer first,
    /// then compare the exact URL (CRC32 is not collision free).
    /// </summary>
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public uint UrlCrc32 { get; private set; }

    public uint DomainHash { get; set; }

    /// <summary>
    /// SHA-256 of <see cref="NormalizedUrl"/>, a persisted generated column (see <c>AppDbContext</c>) with a UNIQUE index.
    /// It is the collision-safe identity of a page: <see cref="UrlCrc32"/> is only a fast lookup (CRC32 collides),
    /// while this key makes "same URL twice" impossible and lets the consumers upsert idempotently.
    /// </summary>
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    [Column(TypeName = "binary(32)")]
    public byte[]? UrlHash { get; private set; }

    [Required]
    [MaxLength(255)]
    public string Domain { get; set; } = string.Empty;

    public ulong SimHash { get; set; }

    [MaxLength(64)]
    public string? ContentHash { get; set; }

    [MaxLength(512)]
    public string? StoragePath { get; set; }

    public long ByteSize { get; set; }

    public int StatusCode { get; set; }

    /// <summary>Category of the last failed fetch (ssl, timeout, dns, ...); null when the last fetch succeeded.</summary>
    [MaxLength(32)]
    public string? ErrorKind { get; set; }

    /// <summary>Readable reason for the last failed fetch, surfaced in the UI.</summary>
    [MaxLength(512)]
    public string? ErrorMessage { get; set; }

    public int OutgoingLinksCount { get; set; }

    public bool IsCrawled { get; set; }

    public DateTime? CrawledAt { get; set; }

    [InverseProperty(nameof(JobPageLink.SourcePage))]
    public ICollection<JobPageLink> OutgoingLinks { get; set; } = new List<JobPageLink>();

    [InverseProperty(nameof(JobPageLink.TargetPage))]
    public ICollection<JobPageLink> IncomingLinks { get; set; } = new List<JobPageLink>();
}
