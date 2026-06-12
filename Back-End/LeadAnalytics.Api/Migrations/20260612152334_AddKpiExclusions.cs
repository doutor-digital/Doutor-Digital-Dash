using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddKpiExclusions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "kpi_exclusions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<int>(type: "integer", nullable: false),
                    unit_id = table.Column<int>(type: "integer", nullable: true),
                    kpi_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    lead_id = table.Column<int>(type: "integer", nullable: false),
                    excluded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    excluded_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kpi_exclusions", x => x.id);
                    table.ForeignKey(
                        name: "FK_kpi_exclusions_leads_lead_id",
                        column: x => x.lead_id,
                        principalTable: "leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_kpi_exclusions_lead_id",
                table: "kpi_exclusions",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "IX_kpi_exclusions_tenant_id_kpi_key",
                table: "kpi_exclusions",
                columns: new[] { "tenant_id", "kpi_key" });

            migrationBuilder.CreateIndex(
                name: "IX_kpi_exclusions_tenant_id_unit_id_kpi_key_lead_id",
                table: "kpi_exclusions",
                columns: new[] { "tenant_id", "unit_id", "kpi_key", "lead_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kpi_exclusions");
        }
    }
}
