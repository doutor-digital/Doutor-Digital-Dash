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

    // ─── SDR · Cadastro unificado das secretárias ───────────────
    public DbSet<SdrLead> SdrLeads { get; set; }
    public DbSet<SdrConsulta> SdrConsultas { get; set; }
    public DbSet<SdrTratamento> SdrTratamentos { get; set; }
    public DbSet<SdrRecebimento> SdrRecebimentos { get; set; }
    public DbSet<SdrTarefa> SdrTarefas { get; set; }
    public DbSet<SdrAgendaEvento> SdrAgendaEventos { get; set; }
    public DbSet<SdrMeta> SdrMetas { get; set; }
    public DbSet<SdrAuditLog> SdrAuditLogs { get; set; }

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

        // ─── SdrLead ─────────────────────────────────────────────
        modelBuilder.Entity<SdrLead>(entity =>
        {
            entity.ToTable("sdr_leads");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Nome).HasMaxLength(180).IsRequired();
            entity.Property(e => e.Telefone).HasMaxLength(40).IsRequired();
            entity.Property(e => e.Tipo).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Origem).HasMaxLength(80).IsRequired();
            entity.Property(e => e.TipoResgate).HasMaxLength(60);
            entity.Property(e => e.MotivoNaoAgendamento).HasMaxLength(120);
            entity.Property(e => e.NomeResponsavel).HasMaxLength(120).IsRequired();
            entity.Property(e => e.Login).HasMaxLength(180);
            entity.Property(e => e.Observacao).HasMaxLength(2000);
            entity.Property(e => e.Situacao).HasMaxLength(80);
            entity.Property(e => e.Clinica).HasMaxLength(180);
            entity.Property(e => e.Source).HasMaxLength(20).IsRequired().HasDefaultValue("cloudia");
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired().HasDefaultValue("pendente_revisao");
            entity.Property(e => e.RejectionReason).HasMaxLength(500);
            entity.Property(e => e.CloudiaWebhookEvent).HasMaxLength(60);

            entity.HasIndex(e => new { e.TenantId, e.Status });
            entity.HasIndex(e => new { e.TenantId, e.Source });
            entity.HasIndex(e => new { e.TenantId, e.CreatedAt });
            entity.HasIndex(e => new { e.TenantId, e.ExternalId }).IsUnique()
                .HasFilter("\"ExternalId\" IS NOT NULL");

            entity.HasOne(e => e.Unit)
                  .WithMany()
                  .HasForeignKey(e => e.UnitId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Attendant)
                  .WithMany()
                  .HasForeignKey(e => e.AttendantId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ImportBatch)
                  .WithMany()
                  .HasForeignKey(e => e.ImportBatchId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ReviewedByUser)
                  .WithMany()
                  .HasForeignKey(e => e.ReviewedByUserId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ─── SdrConsulta ─────────────────────────────────────────
        modelBuilder.Entity<SdrConsulta>(entity =>
        {
            entity.ToTable("sdr_consultas");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.TipoTratamentoIndicado).HasMaxLength(180);
            entity.Property(e => e.MotivoNaoFechamento).HasMaxLength(180);
            entity.Property(e => e.Observacao).HasMaxLength(2000);

            entity.Property(e => e.ValorConsulta).HasColumnType("numeric(12,2)");
            entity.Property(e => e.ValorTratamento).HasColumnType("numeric(12,2)");

            entity.HasIndex(e => new { e.TenantId, e.DataConsulta });
            entity.HasIndex(e => e.SdrLeadId);

            entity.HasOne(e => e.Lead)
                  .WithMany(l => l.Consultas)
                  .HasForeignKey(e => e.SdrLeadId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── SdrTratamento ───────────────────────────────────────
        modelBuilder.Entity<SdrTratamento>(entity =>
        {
            entity.ToTable("sdr_tratamentos");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.Tipo).HasMaxLength(20);
            entity.Property(e => e.Descricao).HasMaxLength(500);
            entity.Property(e => e.Observacao).HasMaxLength(2000);
            entity.Property(e => e.Situacao).HasMaxLength(80);

            entity.Property(e => e.Valor).HasColumnType("numeric(12,2)");

            entity.HasIndex(e => new { e.TenantId, e.CreatedAt });
            entity.HasIndex(e => e.SdrConsultaId);
            entity.HasIndex(e => e.SdrLeadId);

            entity.HasOne(e => e.Consulta)
                  .WithMany(c => c.Tratamentos)
                  .HasForeignKey(e => e.SdrConsultaId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Lead)
                  .WithMany()
                  .HasForeignKey(e => e.SdrLeadId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // ─── SdrRecebimento ──────────────────────────────────────
        modelBuilder.Entity<SdrRecebimento>(entity =>
        {
            entity.ToTable("sdr_recebimentos");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.FormaPagamento).HasMaxLength(40).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.Valor).HasColumnType("numeric(12,2)");

            entity.HasIndex(e => e.SdrConsultaId);
            entity.HasIndex(e => e.SdrTratamentoId);
            entity.HasIndex(e => new { e.TenantId, e.DataRecebimento });

            entity.HasOne(e => e.Consulta)
                  .WithMany(c => c.Recebimentos)
                  .HasForeignKey(e => e.SdrConsultaId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Tratamento)
                  .WithMany(t => t.Recebimentos)
                  .HasForeignKey(e => e.SdrTratamentoId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── SdrTarefa ───────────────────────────────────────────
        modelBuilder.Entity<SdrTarefa>(entity =>
        {
            entity.ToTable("sdr_tarefas");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Nome).HasMaxLength(180).IsRequired();
            entity.Property(e => e.Descricao).HasMaxLength(2000);
            entity.Property(e => e.Prioridade).HasMaxLength(20).IsRequired().HasDefaultValue("media");
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired().HasDefaultValue("pendente");
            entity.Property(e => e.Observacao).HasMaxLength(2000);
            entity.Property(e => e.ResponsavelLogin).HasMaxLength(180);

            entity.HasIndex(e => new { e.TenantId, e.Status });
            entity.HasIndex(e => new { e.TenantId, e.DataVencimento });

            entity.HasOne(e => e.Lead)
                  .WithMany()
                  .HasForeignKey(e => e.SdrLeadId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ─── SdrAgendaEvento ─────────────────────────────────────
        modelBuilder.Entity<SdrAgendaEvento>(entity =>
        {
            entity.ToTable("sdr_agenda_eventos");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Nome).HasMaxLength(180).IsRequired();
            entity.Property(e => e.Descricao).HasMaxLength(500).IsRequired();
            entity.Property(e => e.HoraInicio).HasMaxLength(5).IsRequired();
            entity.Property(e => e.HoraFim).HasMaxLength(5).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired().HasDefaultValue("agendado");
            entity.Property(e => e.Observacao).HasMaxLength(2000);
            entity.Property(e => e.ResponsavelLogin).HasMaxLength(180);

            entity.HasIndex(e => new { e.TenantId, e.Data });
            entity.HasIndex(e => new { e.TenantId, e.Status });

            entity.HasOne(e => e.Lead)
                  .WithMany()
                  .HasForeignKey(e => e.SdrLeadId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ─── SdrMeta ─────────────────────────────────────────────
        modelBuilder.Entity<SdrMeta>(entity =>
        {
            entity.ToTable("sdr_metas");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Mes).HasMaxLength(7).IsRequired();
            entity.Property(e => e.Unidade).HasMaxLength(180).IsRequired();
            entity.Property(e => e.Login).HasMaxLength(180).IsRequired();
            entity.Property(e => e.Secretaria).HasMaxLength(120).IsRequired();
            entity.Property(e => e.MetaValor).HasColumnType("numeric(12,2)");

            entity.HasIndex(e => new { e.TenantId, e.Mes, e.Login }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.Mes });
        });

        // ─── SdrAuditLog ─────────────────────────────────────────
        modelBuilder.Entity<SdrAuditLog>(entity =>
        {
            entity.ToTable("sdr_audit_logs");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.UserName).HasMaxLength(120);
            entity.Property(e => e.UserEmail).HasMaxLength(180);
            entity.Property(e => e.Action).HasMaxLength(60).IsRequired();
            entity.Property(e => e.EntityType).HasMaxLength(40).IsRequired();
            entity.Property(e => e.Summary).HasMaxLength(500).IsRequired();
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(500);

            entity.HasIndex(e => new { e.TenantId, e.CreatedAt });
            entity.HasIndex(e => new { e.TenantId, e.EntityType, e.EntityId });
            entity.HasIndex(e => new { e.TenantId, e.Action });

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.SetNull);
        });
    }
}