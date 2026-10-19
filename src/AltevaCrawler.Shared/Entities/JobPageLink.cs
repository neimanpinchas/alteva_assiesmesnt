namespace AltevaCrawler.Shared.Entities;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

[Table("JobPageLinks")]
[Index(nameof(JobId), nameof(SourcePageId), Name = "IX_JobPageLinks_Job_Source")]
[Index(nameof(JobId), nameof(TargetPageId), Name = "IX_JobPageLinks_Job_Target")]
[Index(nameof(JobId), nameof(SourcePageId), nameof(TargetPageId), Name = "UX_JobPageLinks_Edge", IsUnique = true)]
public class JobPageLink
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [MaxLength(36)]
    public string JobId { get; set; } = string.Empty;

    public long SourcePageId { get; set; }

    public long TargetPageId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(JobId))]
    public Job? Job { get; set; }

    [ForeignKey(nameof(SourcePageId))]
    public Page? SourcePage { get; set; }

    [ForeignKey(nameof(TargetPageId))]
    public Page? TargetPage { get; set; }
}
