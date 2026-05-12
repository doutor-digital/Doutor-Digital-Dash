using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadReviewFieldsAndReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AppointmentScheduledAt",
                table: "leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ClosedTreatment",
                table: "leads",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ConsultationValue",
                table: "leads",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HadInteraction",
                table: "leads",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IndicatedTreatment",
                table: "leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeadType",
                table: "leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NoAppointmentCity",
                table: "leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NoAppointmentReason",
                table: "leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NoCloseReason",
                table: "leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RescueType",
                table: "leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ScheduledConsultation",
                table: "leads",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TreatmentBudget",
                table: "leads",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TreatmentPlanCategory",
                table: "leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TreatmentPlanValue",
                table: "leads",
                type: "numeric",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "lead_payment_receipts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    lead_id = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: true),
                    method = table.Column<string>(type: "text", nullable: true),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_advance = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lead_payment_receipts", x => x.id);
                    table.ForeignKey(
                        name: "FK_lead_payment_receipts_leads_lead_id",
                        column: x => x.lead_id,
                        principalTable: "leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lead_payment_receipts_lead_id_kind_slot",
                table: "lead_payment_receipts",
                columns: new[] { "lead_id", "kind", "slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lead_payment_receipts_tenant_id_is_advance",
                table: "lead_payment_receipts",
                columns: new[] { "tenant_id", "is_advance" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lead_payment_receipts");

            migrationBuilder.DropColumn(
                name: "AppointmentScheduledAt",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "ClosedTreatment",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "ConsultationValue",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "HadInteraction",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "IndicatedTreatment",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "LeadType",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "NoAppointmentCity",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "NoAppointmentReason",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "NoCloseReason",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "RescueType",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "ScheduledConsultation",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "TreatmentBudget",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "TreatmentPlanCategory",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "TreatmentPlanValue",
                table: "leads");
        }
    }
}
