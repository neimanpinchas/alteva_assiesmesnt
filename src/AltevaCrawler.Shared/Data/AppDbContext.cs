namespace AltevaCrawler.Shared.Data;

using Microsoft.EntityFrameworkCore;
using AltevaCrawler.Shared.Entities;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobStatusTransition> JobStatusTransitions => Set<JobStatusTransition>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<JobPageLink> JobPageLinks => Set<JobPageLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<JobStatusTransition>()
            .HasOne(e => e.Job)
            .WithMany(j => j.StatusTransitions)
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<JobPageLink>()
            .HasOne(e => e.Job)
            .WithMany(j => j.PageLinks)
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<JobPageLink>()
            .HasOne(e => e.SourcePage)
            .WithMany(p => p.OutgoingLinks)
            .HasForeignKey(e => e.SourcePageId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<JobPageLink>()
            .HasOne(e => e.TargetPage)
            .WithMany(p => p.IncomingLinks)
            .HasForeignKey(e => e.TargetPageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
