using GradeLens.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace GradeLens.Api.Data;

public class GradeLensDbContext(DbContextOptions<GradeLensDbContext> options) : DbContext(options)
{
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<Rubric> Rubrics => Set<Rubric>();
    public DbSet<Criterion> Criteria => Set<Criterion>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Submission>()
            .Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        modelBuilder.Entity<Submission>()
            .HasIndex(s => new { s.AssignmentId, s.Status });

        modelBuilder.Entity<Grade>()
            .HasOne<Submission>()
            .WithOne(s => s.Grade)
            .HasForeignKey<Grade>(g => g.SubmissionId);

        modelBuilder.Entity<AuditEntry>()
            .HasIndex(a => a.SubmissionId);
    }
}
