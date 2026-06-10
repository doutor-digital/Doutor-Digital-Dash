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
            migrationBuilder.AddColumn<string>(
                name: "EntrySource",
                table: "lead_stage_histories",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "webhook");

            // Toda linha que já existe foi gravada pelo sync/heal com ChangedAt = updated_at
            // (NÃO é a data de entrada na etapa) — marca como 'legacy' pra sair das contagens
            // por data. O backfill da API de eventos repõe as datas reais como 'events_api'.
            migrationBuilder.Sql(
                "UPDATE lead_stage_histories SET \"EntrySource\" = 'legacy';");

            migrationBuilder.AddColumn<long>(
                name: "KommoEventId",
                table: "lead_stage_histories",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_lead_stage_histories_LeadId_KommoEventId",
                table: "lead_stage_histories",
                columns: new[] { "LeadId", "KommoEventId" },
                unique: true,
                filter: "\"KommoEventId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_lead_stage_histories_StageLabel_ChangedAt",
                table: "lead_stage_histories",
                columns: new[] { "StageLabel", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_lead_stage_histories_LeadId_KommoEventId",
                table: "lead_stage_histories");

            migrationBuilder.DropIndex(
                name: "IX_lead_stage_histories_StageLabel_ChangedAt",
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
