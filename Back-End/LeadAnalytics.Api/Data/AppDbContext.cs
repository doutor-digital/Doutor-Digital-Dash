using LeadAnalytics.Api.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }
    public DbSet<Lead> Leads { get; set; }
    public DbSet<Unit> Units { get; set; }
    public DbSet<Attendant> Attendants { get; set; }
    public DbSet<LeadAssignment> LeadAssignments { get; set; }
    public DbSet<LeadStageHistory> LeadStageHistories { get; set; }
    public DbSet<LeadConversation> LeadConversations { get; set; }
    public DbSet<LeadInteraction> LeadInteractions { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<PaymentSplit> PaymentSplits { get; set; }
    public DbSet<OriginEvent> OriginEvents { get; set; }
    public DbSet<LeadAttribution> LeadAttributions { get; set; }
    public DbSet<AppConfiguration> AppConfigurations { get; set; }
    public DbSet<User> Users { get; set; }

    public DbSet<WebhookEvent> WebhookEvents { get; set; }

    public DbSet<Contact> Contacts { get; set; }
    public DbSet<ImportBatch> ImportBatches { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ─── Lead ────────────────────────────────────────────────
        modelBuilder.Entity<Lead>(entity =>
        {
            entity.ToTable("leads");
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.ExternalId, e.TenantId }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.CreatedAt });

            entity.HasOne(l => l.Unit)
                  .WithMany(u => u.Leads)
                  .HasForeignKey(l => l.UnitId);

            entity.HasOne(l => l.Attendant)
                  .WithMany()
                  .HasForeignKey(l => l.AttendantId);

            entity.Property(l => l.AttendanceStatus).HasMaxLength(20);
            entity.HasIndex(l => new { l.TenantId, l.AttendanceStatus });
        });

        // ─── Unit ────────────────────────────────────────────────
        modelBuilder.Entity<Unit>(entity =>
        {
            entity.ToTable("units");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClinicId).IsUnique();
        });

        // ─── Attendant ───────────────────────────────────────────
        modelBuilder.Entity<Attendant>(entity =>
        {
            entity.ToTable("attendants");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ExternalId).IsUnique();
        });

        // ─── LeadAssignment ──────────────────────────────────────
        modelBuilder.Entity<LeadAssignment>(entity =>
        {
            entity.ToTable("lead_assignments");
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Lead)
                  .WithMany(l => l.Assignments)
                  .HasForeignKey(e => e.LeadId);

            entity.HasOne(e => e.Attendant)
                  .WithMany(a => a.Assignments)
                  .HasForeignKey(e => e.AttendantId);
        });

        // ─── LeadStageHistory ────────────────────────────────────
        modelBuilder.Entity<LeadStageHistory>(entity =>
        {
            entity.ToTable("lead_stage_histories");
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Lead)
                  .WithMany(l => l.StageHistory)
                  .HasForeignKey(e => e.LeadId);
        });

        // ─── LeadConversation ────────────────────────────────────
        modelBuilder.Entity<LeadConversation>(entity =>
        {
            entity.ToTable("lead_conversations");
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Lead)
                  .WithMany(l => l.Conversations)
                  .HasForeignKey(e => e.LeadId);
        });

        // ─── LeadInteraction ─────────────────────────────────────
        modelBuilder.Entity<LeadInteraction>(entity =>
        {
            entity.ToTable("lead_interactions");
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Conversation)
                  .WithMany(c => c.Interactions)
                  .HasForeignKey(e => e.LeadConversationId);
        });

        // ─── Payment ─────────────────────────────────────────────
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("payments");
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Lead)
                  .WithMany(l => l.Payments)
                  .HasForeignKey(e => e.LeadId);

            entity.HasOne(e => e.Unit)
                  .WithMany()
                  .HasForeignKey(e => e.UnitId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.Property(e => e.Treatment).HasMaxLength(80).IsRequired();
            entity.Property(e => e.PaymentMethod).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(500);

            entity.Property(e => e.Amount).HasColumnType("numeric(12,2)");
            entity.Property(e => e.TreatmentValue).HasColumnType("numeric(12,2)");
            entity.Property(e => e.DownPayment).HasColumnType("numeric(12,2)");
            entity.Property(e => e.InstallmentValue).HasColumnType("numeric(12,2)");

            entity.HasIndex(e => new { e.TenantId, e.PaidAt });
            entity.HasIndex(e => new { e.TenantId, e.UnitId });
            entity.HasIndex(e => e.LeadId);
        });

        // ─── PaymentSplit ────────────────────────────────────────
        modelBuilder.Entity<PaymentSplit>(entity =>
        {
            entity.ToTable("payment_splits");
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Payment)
                  .WithMany(p => p.Splits)
                  .HasForeignKey(e => e.PaymentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.PaymentMethod).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(500);

            entity.Property(e => e.Amount).HasColumnType("numeric(12,2)");
            entity.Property(e => e.InstallmentValue).HasColumnType("numeric(12,2)");

            entity.HasIndex(e => e.PaymentId);
            entity.HasIndex(e => new { e.PaymentId, e.PaymentMethod });
        });

          // ─── User ────────────────────────────────────────────────
          modelBuilder.Entity<User>(entity =>
          {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .HasMaxLength(120)
                .IsRequired();

            entity.Property(e => e.Email)
                .HasMaxLength(180)
                .IsRequired();

            entity.Property(e => e.PasswordHash)
                .IsRequired();

            entity.Property(e => e.Role)
                .HasMaxLength(30)
                .HasDefaultValue("user")
                .IsRequired();

            entity.Property(e => e.Phone).HasMaxLength(30);
            entity.Property(e => e.PhotoPath).HasMaxLength(300);

            entity.Property(e => e.ResetPasswordCodeHash).HasMaxLength(200);

            entity.HasIndex(e => e.Email)
                .IsUnique();
          });

        // ─── Contact ─────────────────────────────────────────────
        modelBuilder.Entity<Contact>(entity =>
        {
            entity.ToTable("contacts");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.PhoneNormalized).IsRequired();
            entity.Property(e => e.Origem).IsRequired().HasMaxLength(30);

            entity.Property(e => e.AttendanceStatus).HasMaxLength(20);

            entity.HasIndex(e => new { e.TenantId, e.PhoneNormalized }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.Origem });
            entity.HasIndex(e => new { e.TenantId, e.LastMessageAt });
            entity.HasIndex(e => new { e.TenantId, e.AttendanceStatus });
            // Usado pela detecção de duplicados cross-tenant (ignoreTenant=true) e pelas
            // window functions ROW_NUMBER() OVER (PARTITION BY PhoneNormalized ORDER BY CreatedAt, Id).
            entity.HasIndex(e => new { e.PhoneNormalized, e.CreatedAt, e.Id });

            entity.HasOne(e => e.ImportBatch)
                  .WithMany()
                  .HasForeignKey(e => e.ImportBatchId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ─── ImportBatch ─────────────────────────────────────────
        modelBuilder.Entity<ImportBatch>(entity =>
        {
            entity.ToTable("import_batches");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.HasIndex(e => new { e.TenantId, e.CreatedAt });
        });
    }
}