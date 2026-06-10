using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecoveryAttemptEventSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_lead_stage_histories_LeadId",
                table: "lead_stage_histories");

            migrationBuilder.AddColumn<string>(
                name: "entry_source",
                table: "recovery_attempts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AddColumn<string>(
                name: "kommo_event_id",
                table: "recovery_attempts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_recovery_attempts_lead_id_kommo_event_id",
                table: "recovery_attempts",
                columns: new[] { "lead_id", "kommo_event_id" },
                unique: true,
                filter: "\"kommo_event_id\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_recovery_attempts_lead_id_kommo_event_id",
                table: "recovery_attempts");

            migrationBuilder.DropColumn(
                name: "entry_source",
                table: "recovery_attempts");

            migrationBuilder.DropColumn(
                name: "kommo_event_id",
                table: "recovery_attempts");

            migrationBuilder.CreateIndex(
                name: "IX_lead_stage_histories_LeadId",
                table: "lead_stage_histories",
                column: "LeadId");
        }
    }
}
