using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttendanceStatus",
                table: "leads",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AttendanceStatusAt",
                table: "leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttendanceStatus",
                table: "contacts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AttendanceStatusAt",
                table: "contacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_leads_TenantId_AttendanceStatus",
                table: "leads",
                columns: new[] { "TenantId", "AttendanceStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_contacts_TenantId_AttendanceStatus",
                table: "contacts",
                columns: new[] { "TenantId", "AttendanceStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_leads_TenantId_AttendanceStatus",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "IX_contacts_TenantId_AttendanceStatus",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "AttendanceStatus",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "AttendanceStatusAt",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "AttendanceStatus",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "AttendanceStatusAt",
                table: "contacts");
        }
    }
}
