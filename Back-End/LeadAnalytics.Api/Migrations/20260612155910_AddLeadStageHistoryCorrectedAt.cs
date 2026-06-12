using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadStageHistoryCorrectedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CorrectedAt",
                table: "lead_stage_histories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectedByEmail",
                table: "lead_stage_histories",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CorrectedByUserId",
                table: "lead_stage_histories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CorrectedChangedAt",
                table: "lead_stage_histories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionReason",
                table: "lead_stage_histories",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrectedAt",
                table: "lead_stage_histories");

            migrationBuilder.DropColumn(
                name: "CorrectedByEmail",
                table: "lead_stage_histories");

            migrationBuilder.DropColumn(
                name: "CorrectedByUserId",
                table: "lead_stage_histories");

            migrationBuilder.DropColumn(
                name: "CorrectedChangedAt",
                table: "lead_stage_histories");

            migrationBuilder.DropColumn(
                name: "CorrectionReason",
                table: "lead_stage_histories");
        }
    }
}
