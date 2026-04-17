using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Lead> Leads { get; set; }
    public DbSet<Unit> Units { get; set; }
    public DbSet<Attendant> Attendants { get; set; }
    public DbSet<LeadAssignment> LeadAssignments { get; set; }
    public DbSet<LeadStageHistory> LeadStageHistories { get; set; }
    public DbSet<LeadConversation> LeadConversations { get; set; }
    public DbSet<LeadInteraction> LeadInteractions { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<OriginEvent> OriginEvents { get; set; }
    public DbSet<LeadAttribution> LeadAttributions { get; set; }
    public DbSet<AppConfiguration> AppConfigurations { get; set; }
    public DbSet<User> Users { get; set; }

    public DbSet<WebhookEvent> WebhookEvents { get; set; }

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

            entity.HasIndex(e => e.Email)
                .IsUnique();
          });
    }
}