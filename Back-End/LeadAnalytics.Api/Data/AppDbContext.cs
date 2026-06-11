using System.Text.Json;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Data;

public class AppDbContext : DbContext, IDataProtectionKeyContext
{
    private readonly ICurrentUser? _currentUser;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
        : base(options) => _currentUser = currentUser;

    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }
    public DbSet<Lead> Leads { get; set; }
    public DbSet<Unit> Units { get; set; }
    public DbSet<KpiConfiguration> KpiConfigurations { get; set; }
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

    public DbSet<Invitation> Invitations { get; set; }
    public DbSet<UserUnit> UserUnits { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<LoginSession> LoginSessions { get; set; }
    public DbSet<EntityChangeLog> EntityChangeLogs { get; set; }

    public DbSet<Consultation> Consultations { get; set; }
    public DbSet<Treatment> Treatments { get; set; }
    public DbSet<TreatmentInstallment> TreatmentInstallments { get; set; }
    public DbSet<RecoveryAttempt> RecoveryAttempts { get; set; }
    public DbSet<LeadPaymentReceipt> LeadPaymentReceipts { get; set; }
    public DbSet<WebhookExecution> WebhookExecutions { get; set; }

    public DbSet<AgentConversation> AgentConversations { get; set; }
    public DbSet<AgentMessage> AgentMessages { get; set; }

    public DbSet<AdAccount> AdAccounts { get; set; }
    public DbSet<CampaignDailySpend> CampaignDailySpends { get; set; }
    public DbSet<AdsSetting> AdsSettings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ─── Lead ────────────────────────────────────────────────
        modelBuilder.Entity<Lead>(entity =>
        {
            entity.ToTable("leads");
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.ExternalId, e.TenantId }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.CreatedAt });
            // Índice pra agregações por OriginalCreatedAt (data real vinda da Kommo/CSV).
            entity.HasIndex(e => new { e.TenantId, e.OriginalCreatedAt });

            entity.HasOne(l => l.Unit)
                  .WithMany(u => u.Leads)
                  .HasForeignKey(l => l.UnitId);

            entity.HasOne(l => l.Attendant)
                  .WithMany()
                  .HasForeignKey(l => l.AttendantId);

            entity.Property(l => l.AttendanceStatus).HasMaxLength(20);
            entity.HasIndex(l => new { l.TenantId, l.AttendanceStatus });

            // Dados sincronizados da Kommo (custom_fields + tags) — JSONB pra
            // permitir queries com operadores @> / ? quando vierem filtros.
            entity.Property(l => l.CustomFieldsJson).HasColumnType("jsonb");
            entity.Property(l => l.TagsJson).HasColumnType("jsonb");
        });

        // ─── Unit ────────────────────────────────────────────────
        modelBuilder.Entity<Unit>(entity =>
        {
            entity.ToTable("units");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClinicId).IsUnique();

            // Slug compõe a URL do webhook (/webhooks/kommo/{slug}); precisa ser único.
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.Property(e => e.KommoStageMapJson).HasColumnType("jsonb");
        });

        // ─── KpiConfiguration ────────────────────────────────────
        modelBuilder.Entity<KpiConfiguration>(entity =>
        {
            entity.ToTable("kpi_configurations");
            entity.HasKey(e => e.Id);
            // Uma config por (unidade, KPI).
            entity.HasIndex(e => new { e.UnitId, e.KpiKey }).IsUnique();
            entity.Property(e => e.KpiKey).HasMaxLength(64);
            entity.Property(e => e.SourceType).HasMaxLength(48);
            entity.Property(e => e.ConfigJson).HasColumnType("jsonb");
            entity.HasOne(e => e.Unit)
                  .WithMany()
                  .HasForeignKey(e => e.UnitId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── AdAccount (Central de Integrações: Meta/Google Ads) ─
        modelBuilder.Entity<AdAccount>(entity =>
        {
            entity.ToTable("ad_accounts");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ClinicId, e.Provider });
            entity.Property(e => e.Provider).HasMaxLength(16);
            entity.Property(e => e.Status).HasMaxLength(16);
        });

        // ─── CampaignDailySpend (gasto por campanha/dia) ─────────
        modelBuilder.Entity<CampaignDailySpend>(entity =>
        {
            entity.ToTable("campaign_daily_spend");
            entity.HasKey(e => e.Id);
            // Um registro por (conta, campanha, dia) — o sync faz upsert por esta chave.
            entity.HasIndex(e => new { e.AdAccountId, e.CampaignId, e.Date }).IsUnique();
            entity.HasIndex(e => new { e.ClinicId, e.Date });
            entity.Property(e => e.Provider).HasMaxLength(16);
            entity.Property(e => e.CampaignId).HasMaxLength(64);
            entity.Property(e => e.Currency).HasMaxLength(8);
            entity.Property(e => e.Spend).HasColumnType("numeric(14,2)");
            entity.HasOne(e => e.AdAccount)
                  .WithMany()
                  .HasForeignKey(e => e.AdAccountId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── AdsSetting (credenciais do app por provedor) ────────
        modelBuilder.Entity<AdsSetting>(entity =>
        {
            entity.ToTable("ads_settings");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Provider).IsUnique();
            entity.Property(e => e.Provider).HasMaxLength(16);
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

            entity.Property(e => e.EntrySource).HasMaxLength(16).HasDefaultValue(LeadStageHistory.SourceWebhook);
            entity.Property(e => e.KommoEventId).HasMaxLength(32);

            // Dedup do backfill: o mesmo evento da Kommo nunca vira duas linhas pro mesmo lead.
            // Escopado por LeadId porque o id de evento é único POR CONTA Kommo (unidades
            // diferentes poderiam repetir o valor). Índice parcial — webhook/legado têm null.
            entity.HasIndex(e => new { e.LeadId, e.KommoEventId })
                  .IsUnique()
                  .HasFilter("\"KommoEventId\" IS NOT NULL");
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

        // ─── RecoveryAttempt ─────────────────────────────────────
        modelBuilder.Entity<RecoveryAttempt>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Lead)
                  .WithMany()
                  .HasForeignKey(e => e.LeadId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.LeadId, e.CreatedAt });
            entity.HasIndex(e => new { e.TenantId, e.CreatedAt });

            entity.Property(e => e.KommoEventId).HasMaxLength(32);
            entity.Property(e => e.EntrySource).HasMaxLength(16).HasDefaultValue("manual");
            // Dedup do backfill: o mesmo evento de mudança do campo não vira duas tentativas.
            entity.HasIndex(e => new { e.LeadId, e.KommoEventId })
                  .IsUnique()
                  .HasFilter("\"kommo_event_id\" IS NOT NULL");
        });

        // ─── LeadPaymentReceipt ──────────────────────────────────
        modelBuilder.Entity<LeadPaymentReceipt>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Lead)
                  .WithMany(l => l.PaymentReceipts)
                  .HasForeignKey(e => e.LeadId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.LeadId, e.Kind, e.Slot }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.IsAdvance });
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

        // ─── Invitation ──────────────────────────────────────────
        modelBuilder.Entity<Invitation>(entity =>
        {
            entity.ToTable("invitations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).HasMaxLength(180).IsRequired();
            entity.Property(e => e.Role).HasMaxLength(30).IsRequired();
            entity.Property(e => e.TokenHash).HasMaxLength(200).IsRequired();
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.Email, e.UnitId, e.AcceptedAt });
            entity.HasIndex(e => e.TenantId);
        });

        // ─── UserUnit ────────────────────────────────────────────
        modelBuilder.Entity<UserUnit>(entity =>
        {
            entity.ToTable("user_units");
            entity.HasKey(e => new { e.UserId, e.UnitId });
            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Unit)
                  .WithMany()
                  .HasForeignKey(e => e.UnitId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.UnitId);
        });

        // ─── AuditLog ────────────────────────────────────────────
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).HasMaxLength(180);
            entity.Property(e => e.UserName).HasMaxLength(120);
            entity.Property(e => e.Role).HasMaxLength(30);
            entity.Property(e => e.AuthMethod).HasMaxLength(20);
            entity.Property(e => e.Ip).HasMaxLength(64);
            entity.Property(e => e.UserAgent).HasMaxLength(400);
            entity.Property(e => e.Method).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Path).HasMaxLength(400).IsRequired();
            entity.Property(e => e.QueryString).HasMaxLength(1000);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });
            entity.HasIndex(e => new { e.Path, e.CreatedAt });
        });

        // ─── LoginSession ────────────────────────────────────────
        modelBuilder.Entity<LoginSession>(entity =>
        {
            entity.ToTable("login_sessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).HasMaxLength(180);
            entity.Property(e => e.UserName).HasMaxLength(120);
            entity.Property(e => e.Role).HasMaxLength(30);
            entity.Property(e => e.AuthMethod).HasMaxLength(20);
            entity.Property(e => e.Ip).HasMaxLength(64);
            entity.Property(e => e.UserAgent).HasMaxLength(400);
            entity.Property(e => e.Device).HasMaxLength(200);
            entity.Property(e => e.GeoCountry).HasMaxLength(80);
            entity.Property(e => e.GeoRegion).HasMaxLength(80);
            entity.Property(e => e.GeoCity).HasMaxLength(120);
            entity.Property(e => e.EndReason).HasMaxLength(40);
            entity.HasIndex(e => new { e.UserId, e.LoginAt });
            entity.HasIndex(e => e.IsActive);
        });

        // ─── EntityChangeLog ─────────────────────────────────────
        modelBuilder.Entity<EntityChangeLog>(entity =>
        {
            entity.ToTable("entity_change_logs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).HasMaxLength(180);
            entity.Property(e => e.Role).HasMaxLength(30);
            entity.Property(e => e.EntityType).HasMaxLength(80).IsRequired();
            entity.Property(e => e.EntityId).HasMaxLength(64);
            entity.Property(e => e.Action).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ChangesJson).HasColumnType("jsonb");
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });
            entity.HasIndex(e => e.CreatedAt);
        });

        // ─── User updates: GoogleSub unique índex ────────────────
        modelBuilder.Entity<User>()
            .HasIndex(u => u.GoogleSub)
            .IsUnique()
            .HasFilter("\"google_sub\" IS NOT NULL");

        // ─── Consultation ────────────────────────────────────────
        modelBuilder.Entity<Consultation>(entity =>
        {
            entity.ToTable("consultations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.PaymentMethod).HasMaxLength(30);
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.HasOne(e => e.Lead)
                  .WithMany()
                  .HasForeignKey(e => e.LeadId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Unit)
                  .WithMany()
                  .HasForeignKey(e => e.UnitId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => new { e.TenantId, e.ScheduledAt });
            entity.HasIndex(e => e.LeadId);
            entity.HasIndex(e => new { e.TenantId, e.Status });
        });

        // ─── Treatment ──────────────────────────────────────────
        modelBuilder.Entity<Treatment>(entity =>
        {
            entity.ToTable("treatments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.TreatmentType).HasMaxLength(120);
            entity.Property(e => e.RejectionReason).HasMaxLength(300);
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.HasOne(e => e.Lead)
                  .WithMany()
                  .HasForeignKey(e => e.LeadId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Consultation)
                  .WithMany(c => c.Treatments)
                  .HasForeignKey(e => e.ConsultationId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => new { e.TenantId, e.Status });
            entity.HasIndex(e => new { e.TenantId, e.CreatedAt });
            entity.HasIndex(e => e.LeadId);
            entity.HasIndex(e => e.PaymentId);
        });

        // ─── TreatmentInstallment ───────────────────────────────
        modelBuilder.Entity<TreatmentInstallment>(entity =>
        {
            entity.ToTable("treatment_installments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PaymentMethod).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.HasOne(e => e.Treatment)
                  .WithMany(t => t.Installments)
                  .HasForeignKey(e => e.TreatmentId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.TreatmentId, e.Sequence }).IsUnique();
        });

        // ─── WebhookExecution ───────────────────────────────────
        modelBuilder.Entity<WebhookExecution>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Provider).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Slug).HasMaxLength(120);
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Method).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Path).HasMaxLength(400).IsRequired();
            entity.Property(e => e.Ip).HasMaxLength(64);
            entity.Property(e => e.UserAgent).HasMaxLength(400);
            entity.Property(e => e.ContentType).HasMaxLength(120);
            entity.Property(e => e.KommoAccountId).HasMaxLength(40);
            entity.Property(e => e.KommoSubdomain).HasMaxLength(120);

            // Queries do painel — listagens por unidade/status ordenadas por data.
            entity.HasIndex(e => e.ReceivedAt);
            entity.HasIndex(e => new { e.UnitId, e.ReceivedAt });
            entity.HasIndex(e => new { e.TenantId, e.ReceivedAt });
            entity.HasIndex(e => new { e.Status, e.ReceivedAt });
        });

        // ─── AgentConversation (I.A. / agente-Dt) ────────────────
        modelBuilder.Entity<AgentConversation>(entity =>
        {
            entity.ToTable("agent_conversations");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ExternalId).HasMaxLength(160).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.AgentName).HasMaxLength(80);
            entity.Property(e => e.Channel).HasMaxLength(40);
            entity.Property(e => e.ContactName).HasMaxLength(200);
            entity.Property(e => e.ContactPhone).HasMaxLength(40);
            entity.Property(e => e.PhoneNormalized).HasMaxLength(40);
            entity.Property(e => e.Intent).HasMaxLength(80);
            entity.Property(e => e.Sentiment).HasMaxLength(40);
            entity.Property(e => e.MetadataJson).HasColumnType("jsonb");

            // Identidade estável da conversa por tenant (upsert do webhook).
            entity.HasIndex(e => new { e.TenantId, e.ExternalId }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.LastMessageAt });
            entity.HasIndex(e => new { e.TenantId, e.Status });
            entity.HasIndex(e => new { e.UnitId, e.StartedAt });
            entity.HasIndex(e => e.PhoneNormalized);

            entity.HasOne(e => e.Unit)
                  .WithMany()
                  .HasForeignKey(e => e.UnitId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Lead)
                  .WithMany()
                  .HasForeignKey(e => e.LeadId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Contact)
                  .WithMany()
                  .HasForeignKey(e => e.ContactId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ─── AgentMessage ────────────────────────────────────────
        modelBuilder.Entity<AgentMessage>(entity =>
        {
            entity.ToTable("agent_messages");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Role).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ExternalId).HasMaxLength(160);
            entity.Property(e => e.ToolName).HasMaxLength(120);
            entity.Property(e => e.MetadataJson).HasColumnType("jsonb");

            entity.HasOne(e => e.Conversation)
                  .WithMany(c => c.Messages)
                  .HasForeignKey(e => e.AgentConversationId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.AgentConversationId, e.SentAt });
        });

    }

    // ─── Trilha de alteração de entidades ────────────────────────────────
    // Entidades cujas mudanças são auditadas (quem mudou o quê, antes → depois).
    private static readonly HashSet<string> AuditedEntities =
        new(StringComparer.Ordinal) { "Lead", "User", "Unit", "Invitation" };

    // Campos de bookkeeping que não viram trilha (evita ruído de login/sync).
    private static readonly HashSet<string> IgnoredProps =
        new(StringComparer.Ordinal)
        {
            "UpdatedAt", "LastLoginAt", "RefreshToken", "RefreshTokenExpiresAt",
            "FailedLoginAttempts", "LockedUntil", "ResetPasswordCodeHash",
            "ResetPasswordCodeExpiresAt", "ResetPasswordAttempts", "ResetPasswordRequestedAt",
        };

    private bool _writingAudit;

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (_writingAudit)
            return await base.SaveChangesAsync(ct);

        var pending = CaptureChanges();
        var result = await base.SaveChangesAsync(ct);

        if (pending.Count > 0)
        {
            foreach (var p in pending)
                p.Log.EntityId ??= ResolveKey(p.Entry);

            _writingAudit = true;
            try
            {
                EntityChangeLogs.AddRange(pending.Select(p => p.Log));
                await base.SaveChangesAsync(ct);
            }
            finally { _writingAudit = false; }
        }

        return result;
    }

    public override int SaveChanges()
        => SaveChangesAsync().GetAwaiter().GetResult();

    private List<(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Entry, EntityChangeLog Log)> CaptureChanges()
    {
        var list = new List<(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry, EntityChangeLog)>();

        foreach (var entry in ChangeTracker.Entries())
        {
            var typeName = entry.Entity.GetType().Name;
            if (!AuditedEntities.Contains(typeName)) continue;
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            var changes = new Dictionary<string, object?>();

            if (entry.State == EntityState.Modified)
            {
                foreach (var prop in entry.Properties)
                {
                    if (!prop.IsModified) continue;
                    if (IgnoredProps.Contains(prop.Metadata.Name)) continue;
                    if (Equals(prop.OriginalValue, prop.CurrentValue)) continue;
                    changes[prop.Metadata.Name] = new
                    {
                        from = Redact(prop.Metadata.Name, prop.OriginalValue),
                        to = Redact(prop.Metadata.Name, prop.CurrentValue),
                    };
                }
                if (changes.Count == 0) continue; // nada relevante mudou
            }
            else if (entry.State == EntityState.Added)
            {
                foreach (var prop in entry.Properties)
                    changes[prop.Metadata.Name] = Redact(prop.Metadata.Name, prop.CurrentValue);
            }
            else // Deleted
            {
                foreach (var prop in entry.Properties)
                    changes[prop.Metadata.Name] = Redact(prop.Metadata.Name, prop.OriginalValue);
            }

            var log = new EntityChangeLog
            {
                UserId = _currentUser?.UserId,
                Email = _currentUser?.Email,
                Role = _currentUser?.Role,
                TenantId = _currentUser?.TenantId,
                EntityType = typeName,
                Action = entry.State.ToString(),
                ChangesJson = JsonSerializer.Serialize(changes),
                CreatedAt = DateTime.UtcNow,
            };

            // Para Modified/Deleted a PK já é conhecida; Added resolve após o save.
            if (entry.State != EntityState.Added)
                log.EntityId = ResolveKey(entry);

            list.Add((entry, log));
        }

        return list;
    }

    private static string? ResolveKey(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return null;
        var values = key.Properties
            .Select(p => entry.Property(p.Name).CurrentValue?.ToString())
            .Where(v => v is not null);
        return string.Join(":", values);
    }

    /// <summary>Não grava valores sensíveis (hashes/tokens) na trilha.</summary>
    private static object? Redact(string propName, object? value)
    {
        var n = propName.ToLowerInvariant();
        if (value is null) return null;
        if (n.Contains("password") || n.Contains("token") || n.Contains("hash") || n.Contains("secret"))
            return "***";
        return value;
    }
}