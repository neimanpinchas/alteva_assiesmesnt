namespace AltevaCrawler.Shared.Entities;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

[Table("JobStatusTransitions")]
[Index(nameof(JobId), Name = "IX_JobTransitions_JobId")]
public class JobStatusTransition
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [MaxLength(36)]
    public string JobId { get; set; } = string.Empty;

    [Required]
    [Column(TypeName = "char(1)")]
    public char FromStatus { get; set; }

    [Required]
    [Column(TypeName = "char(1)")]
    public char ToStatus { get; set; }

    public DateTime TransitionedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(255)]
    public string? Reason { get; set; }

    [ForeignKey(nameof(JobId))]
    public Job? Job { get; set; }
}
