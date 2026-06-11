using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadOriginalCreatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OriginalCreatedAt",
                table: "leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_leads_TenantId_OriginalCreatedAt",
                table: "leads",
                columns: new[] { "TenantId", "OriginalCreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_leads_TenantId_OriginalCreatedAt",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "OriginalCreatedAt",
                table: "leads");
        }
    }
}
