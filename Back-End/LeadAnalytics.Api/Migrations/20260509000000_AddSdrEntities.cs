using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSdrEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ─── sdr_leads ───────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "sdr_leads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    ExternalId = table.Column<int>(type: "integer", nullable: true),
                    Nome = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Telefone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Origem = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TipoResgate = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Interacao = table.Column<bool>(type: "boolean", nullable: false),
                    AgendouConsulta = table.Column<bool>(type: "boolean", nullable: false),
                    DataAgendamento = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MotivoNaoAgendamento = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    NomeResponsavel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Login = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    Observacao = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Situacao = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Clinica = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    DataOrigem = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataModificacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "cloudia"),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "pendente_revisao"),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<int>(type: "integer", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CloudiaFields = table.Column<string>(type: "text", nullable: true),
                    CloudiaReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CloudiaWebhookEvent = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    UnitId = table.Column<int>(type: "integer", nullable: true),
                    AttendantId = table.Column<int>(type: "integer", nullable: true),
                    ImportBatchId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sdr_leads", x => x.Id);
                    // FK para attendants intencionalmente OMITIDA — tabela tem IDs duplicados (sem PK).
                    table.ForeignKey(
                        name: "FK_sdr_leads_import_batches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "import_batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_sdr_leads_units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_sdr_leads_users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex("IX_sdr_leads_TenantId_Status", "sdr_leads", new[] { "TenantId", "Status" });
            migrationBuilder.CreateIndex("IX_sdr_leads_TenantId_Source", "sdr_leads", new[] { "TenantId", "Source" });
            migrationBuilder.CreateIndex("IX_sdr_leads_TenantId_CreatedAt", "sdr_leads", new[] { "TenantId", "CreatedAt" });
            migrationBuilder.CreateIndex(
                name: "IX_sdr_leads_TenantId_ExternalId",
                table: "sdr_leads",
                columns: new[] { "TenantId", "ExternalId" },
                unique: true,
                filter: "\"ExternalId\" IS NOT NULL");
            migrationBuilder.CreateIndex("IX_sdr_leads_AttendantId", "sdr_leads", "AttendantId");
            migrationBuilder.CreateIndex("IX_sdr_leads_ImportBatchId", "sdr_leads", "ImportBatchId");
            migrationBuilder.CreateIndex("IX_sdr_leads_UnitId", "sdr_leads", "UnitId");
            migrationBuilder.CreateIndex("IX_sdr_leads_ReviewedByUserId", "sdr_leads", "ReviewedByUserId");

            // ─── sdr_consultas ───────────────────────────────────
            migrationBuilder.CreateTable(
                name: "sdr_consultas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    SdrLeadId = table.Column<int>(type: "integer", nullable: false),
                    DataConsulta = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValorConsulta = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Pago = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    TipoTratamentoIndicado = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    ValorTratamento = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    FechouTratamento = table.Column<bool>(type: "boolean", nullable: true),
                    MotivoNaoFechamento = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    Observacao = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sdr_consultas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sdr_consultas_sdr_leads_SdrLeadId",
                        column: x => x.SdrLeadId,
                        principalTable: "sdr_leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("IX_sdr_consultas_TenantId_DataConsulta", "sdr_consultas", new[] { "TenantId", "DataConsulta" });
            migrationBuilder.CreateIndex("IX_sdr_consultas_SdrLeadId", "sdr_consultas", "SdrLeadId");

            // ─── sdr_tratamentos ─────────────────────────────────
            migrationBuilder.CreateTable(
                name: "sdr_tratamentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    SdrConsultaId = table.Column<int>(type: "integer", nullable: false),
                    SdrLeadId = table.Column<int>(type: "integer", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Observacao = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Situacao = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sdr_tratamentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sdr_tratamentos_sdr_consultas_SdrConsultaId",
                        column: x => x.SdrConsultaId,
                        principalTable: "sdr_consultas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sdr_tratamentos_sdr_leads_SdrLeadId",
                        column: x => x.SdrLeadId,
                        principalTable: "sdr_leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex("IX_sdr_tratamentos_TenantId_CreatedAt", "sdr_tratamentos", new[] { "TenantId", "CreatedAt" });
            migrationBuilder.CreateIndex("IX_sdr_tratamentos_SdrConsultaId", "sdr_tratamentos", "SdrConsultaId");
            migrationBuilder.CreateIndex("IX_sdr_tratamentos_SdrLeadId", "sdr_tratamentos", "SdrLeadId");

            // ─── sdr_recebimentos ────────────────────────────────
            migrationBuilder.CreateTable(
                name: "sdr_recebimentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    SdrConsultaId = table.Column<int>(type: "integer", nullable: true),
                    SdrTratamentoId = table.Column<int>(type: "integer", nullable: true),
                    Ordem = table.Column<int>(type: "integer", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    FormaPagamento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DataRecebimento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sdr_recebimentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sdr_recebimentos_sdr_consultas_SdrConsultaId",
                        column: x => x.SdrConsultaId,
                        principalTable: "sdr_consultas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sdr_recebimentos_sdr_tratamentos_SdrTratamentoId",
                        column: x => x.SdrTratamentoId,
                        principalTable: "sdr_tratamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("IX_sdr_recebimentos_SdrConsultaId", "sdr_recebimentos", "SdrConsultaId");
            migrationBuilder.CreateIndex("IX_sdr_recebimentos_SdrTratamentoId", "sdr_recebimentos", "SdrTratamentoId");
            migrationBuilder.CreateIndex("IX_sdr_recebimentos_TenantId_DataRecebimento", "sdr_recebimentos", new[] { "TenantId", "DataRecebimento" });

            // ─── sdr_tarefas ─────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "sdr_tarefas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    DataVencimento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Nome = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Prioridade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "media"),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "pendente"),
                    Observacao = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResponsavelLogin = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    SdrLeadId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcludedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sdr_tarefas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sdr_tarefas_sdr_leads_SdrLeadId",
                        column: x => x.SdrLeadId,
                        principalTable: "sdr_leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex("IX_sdr_tarefas_TenantId_Status", "sdr_tarefas", new[] { "TenantId", "Status" });
            migrationBuilder.CreateIndex("IX_sdr_tarefas_TenantId_DataVencimento", "sdr_tarefas", new[] { "TenantId", "DataVencimento" });
            migrationBuilder.CreateIndex("IX_sdr_tarefas_SdrLeadId", "sdr_tarefas", "SdrLeadId");

            // ─── sdr_agenda_eventos ──────────────────────────────
            migrationBuilder.CreateTable(
                name: "sdr_agenda_eventos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    Data = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HoraInicio = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    HoraFim = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Nome = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "agendado"),
                    Observacao = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResponsavelLogin = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    SdrLeadId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sdr_agenda_eventos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sdr_agenda_eventos_sdr_leads_SdrLeadId",
                        column: x => x.SdrLeadId,
                        principalTable: "sdr_leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex("IX_sdr_agenda_eventos_TenantId_Data", "sdr_agenda_eventos", new[] { "TenantId", "Data" });
            migrationBuilder.CreateIndex("IX_sdr_agenda_eventos_TenantId_Status", "sdr_agenda_eventos", new[] { "TenantId", "Status" });
            migrationBuilder.CreateIndex("IX_sdr_agenda_eventos_SdrLeadId", "sdr_agenda_eventos", "SdrLeadId");

            // ─── sdr_metas ───────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "sdr_metas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    Mes = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Unidade = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Login = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Secretaria = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    MetaValor = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    RealCadastro = table.Column<int>(type: "integer", nullable: false),
                    RealResgate = table.Column<int>(type: "integer", nullable: false),
                    QtdTotal = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sdr_metas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sdr_metas_TenantId_Mes_Login",
                table: "sdr_metas",
                columns: new[] { "TenantId", "Mes", "Login" },
                unique: true);
            migrationBuilder.CreateIndex("IX_sdr_metas_TenantId_Mes", "sdr_metas", new[] { "TenantId", "Mes" });

            // ─── sdr_audit_logs ──────────────────────────────────
            migrationBuilder.CreateTable(
                name: "sdr_audit_logs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    UserName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    UserEmail = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    Action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    BeforeJson = table.Column<string>(type: "text", nullable: true),
                    AfterJson = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sdr_audit_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sdr_audit_logs_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex("IX_sdr_audit_logs_TenantId_CreatedAt", "sdr_audit_logs", new[] { "TenantId", "CreatedAt" });
            migrationBuilder.CreateIndex("IX_sdr_audit_logs_TenantId_EntityType_EntityId", "sdr_audit_logs", new[] { "TenantId", "EntityType", "EntityId" });
            migrationBuilder.CreateIndex("IX_sdr_audit_logs_TenantId_Action", "sdr_audit_logs", new[] { "TenantId", "Action" });
            migrationBuilder.CreateIndex("IX_sdr_audit_logs_UserId", "sdr_audit_logs", "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "sdr_audit_logs");
            migrationBuilder.DropTable(name: "sdr_metas");
            migrationBuilder.DropTable(name: "sdr_agenda_eventos");
            migrationBuilder.DropTable(name: "sdr_tarefas");
            migrationBuilder.DropTable(name: "sdr_recebimentos");
            migrationBuilder.DropTable(name: "sdr_tratamentos");
            migrationBuilder.DropTable(name: "sdr_consultas");
            migrationBuilder.DropTable(name: "sdr_leads");
        }
    }
}
