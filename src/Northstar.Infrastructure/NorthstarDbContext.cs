using Microsoft.EntityFrameworkCore;
using Northstar.Domain;

namespace Northstar.Infrastructure;

public sealed class NorthstarDbContext(DbContextOptions<NorthstarDbContext> options) : DbContext(options)
{
    public DbSet<ApplicantRecord> Applicants => Set<ApplicantRecord>();
    public DbSet<ApplicationRecord> Applications => Set<ApplicationRecord>();
    public DbSet<CaseRecord> Cases => Set<CaseRecord>();
    public DbSet<CaseNote> CaseNotes => Set<CaseNote>();
    public DbSet<CaseDocument> CaseDocuments => Set<CaseDocument>();
    public DbSet<UserRecord> Users => Set<UserRecord>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<PolicySection> PolicySections => Set<PolicySection>();
    public DbSet<AIRequest> AIRequests => Set<AIRequest>();
    public DbSet<ReviewItem> ReviewItems => Set<ReviewItem>();
    public DbSet<AISystemRegistry> AISystemRegistry => Set<AISystemRegistry>();
    public DbSet<EvaluationRun> EvaluationRuns => Set<EvaluationRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicantRecord>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Email);
        });

        modelBuilder.Entity<ApplicationRecord>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.ApplicationNumber).IsUnique();
            entity.HasIndex(item => item.IdempotencyKey).IsUnique();
            entity.HasOne(item => item.Applicant).WithMany().HasForeignKey(item => item.ApplicantId);
        });

        modelBuilder.Entity<CaseRecord>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.CaseNumber).IsUnique();
            entity.HasOne(item => item.Application).WithMany().HasForeignKey(item => item.ApplicationId);
            entity.Property(item => item.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<CaseNote>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.CaseId);
            entity.Property(item => item.Content).HasMaxLength(4_000);
        });

        modelBuilder.Entity<CaseDocument>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.CaseId);
            entity.HasIndex(item => item.StorageKey).IsUnique();
            entity.Property(item => item.OriginalFileName).HasMaxLength(255);
            entity.Property(item => item.StorageKey).HasMaxLength(512);
        });

        modelBuilder.Entity<UserRecord>(entity => entity.HasKey(item => item.Id));

        modelBuilder.Entity<RoleAssignment>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.UserId, item.Role }).IsUnique();
            entity.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.EventId).IsUnique();
            entity.HasIndex(item => item.CorrelationId);
            entity.HasIndex(item => item.CaseId);
        });

        modelBuilder.Entity<PolicySection>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.DocumentTitle, item.SectionLabel }).IsUnique();
        });

        modelBuilder.Entity<AIRequest>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.RequestId).IsUnique();
            entity.HasIndex(item => item.CaseId);
            entity.Property(item => item.EstimatedCost).HasPrecision(12, 8);
        });

        modelBuilder.Entity<ReviewItem>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.AIRequestId).IsUnique();
            entity.HasIndex(item => new { item.AssignedReviewerId, item.Status });
        });

        modelBuilder.Entity<AISystemRegistry>(entity => entity.HasKey(item => item.Id));
        modelBuilder.Entity<EvaluationRun>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.RunId).IsUnique();
        });
    }
}
