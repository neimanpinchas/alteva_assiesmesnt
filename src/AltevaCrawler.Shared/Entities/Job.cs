namespace AltevaCrawler.Shared.Entities;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using AltevaCrawler.Shared.Enums;
using AltevaCrawler.Shared.Utilities;

[Table("Jobs")]
[Index(nameof(StatusChar), Name = "IX_Jobs_Status")]
[Index(nameof(CreatedAt), Name = "IX_Jobs_CreatedAt")]
public class Job
{
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = SequentialGuid.NewSequentialGuid().ToString();

    [Required]
    [MaxLength(2048)]
    public string Url { get; set; } = string.Empty;

    public int MaxDepth { get; set; } = 2;

    public int MaxPagesLimit { get; set; } = 200;

    /// <summary>
    /// Concurrency token: consumers update jobs from several messages at once, and a stale writer
    /// (e.g. "Pending -> Running") must never overwrite a newer status such as Completed or Failed.
    /// </summary>
    [Required]
    [ConcurrencyCheck]
    [Column("status", TypeName = "char(1)")]
    public char StatusChar { get; set; } = (char)JobStatus.Pending;

    [NotMapped]
    public JobStatus Status
    {
        get => JobStatusExtensions.FromChar(StatusChar);
        set => StatusChar = value.ToChar();
    }

    public int TotalPagesCrawled { get; set; }

    public long TotalBytesDownloaded { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    [MaxLength(512)]
    public string? FailureReason { get; set; }

    public ICollection<JobStatusTransition> StatusTransitions { get; set; } = new List<JobStatusTransition>();
    public ICollection<JobPageLink> PageLinks { get; set; } = new List<JobPageLink>();
}
