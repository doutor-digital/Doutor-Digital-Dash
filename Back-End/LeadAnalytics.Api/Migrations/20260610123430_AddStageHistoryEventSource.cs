using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStageHistoryEventSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IMPORTANTE: operações SÓ DE METADADOS (instantâneas), pra não estourar o
            // timeout de comando do Migrate() no boot numa tabela grande.
            //
            // No Postgres 11+, ADD COLUMN com default CONSTANTE é metadado (não reescreve
            // linha por linha): adicionamos com default 'legacy' — assim TODA linha existente
            // passa a ler 'legacy' instantaneamente (eram gravadas pelo sync/heal com
            // ChangedAt = updated_at, que não é a data de entrada na etapa). Em seguida
            // trocamos o default pra 'webhook' (só afeta INSERTs futuros; não toca linhas
            // existentes). Evita o UPDATE de tabela inteira que falhava no deploy.
            migrationBuilder.Sql(
                "ALTER TABLE lead_stage_histories ADD COLUMN \"EntrySource\" character varying(16) NOT NULL DEFAULT 'legacy';");
            migrationBuilder.Sql(
                "ALTER TABLE lead_stage_histories ALTER COLUMN \"EntrySource\" SET DEFAULT 'webhook';");

            migrationBuilder.AddColumn<long>(
                name: "KommoEventId",
                table: "lead_stage_histories",
                type: "bigint",
                nullable: true);

            // Índice parcial sobre KommoEventId (todas NULL agora) → cobre 0 linhas → criação
            // instantânea, independente do tamanho da tabela. Dedup do backfill.
            migrationBuilder.CreateIndex(
                name: "IX_lead_stage_histories_LeadId_KommoEventId",
                table: "lead_stage_histories",
                columns: new[] { "LeadId", "KommoEventId" },
                unique: true,
                filter: "\"KommoEventId\" IS NOT NULL");

            // (StageLabel, ChangedAt) seria ótimo pro KPI por dia, mas construir índice sobre
            // a tabela inteira no boot pode estourar o timeout. Fica pra um índice CONCURRENTLY
            // separado, fora da migration transacional. Sem ele não há regressão: a query já
            // fazia esse scan antes.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_lead_stage_histories_LeadId_KommoEventId",
                table: "lead_stage_histories");

            migrationBuilder.DropColumn(
                name: "EntrySource",
                table: "lead_stage_histories");

            migrationBuilder.DropColumn(
                name: "KommoEventId",
                table: "lead_stage_histories");
        }
    }
}
