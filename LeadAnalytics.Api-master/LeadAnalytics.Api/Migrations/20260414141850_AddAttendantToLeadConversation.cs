using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendantToLeadConversation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastUpdatedAt",
                table: "leads",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "AttendantId",
                table: "lead_conversations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnitId",
                table: "attendants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_lead_conversations_AttendantId",
                table: "lead_conversations",
                column: "AttendantId");

            migrationBuilder.CreateIndex(
                name: "IX_attendants_UnitId",
                table: "attendants",
                column: "UnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_attendants_units_UnitId",
                table: "attendants",
                column: "UnitId",
                principalTable: "units",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_lead_conversations_attendants_AttendantId",
                table: "lead_conversations",
                column: "AttendantId",
                principalTable: "attendants",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_attendants_units_UnitId",
                table: "attendants");

            migrationBuilder.DropForeignKey(
                name: "FK_lead_conversations_attendants_AttendantId",
                table: "lead_conversations");

            migrationBuilder.DropIndex(
                name: "IX_lead_conversations_AttendantId",
                table: "lead_conversations");

            migrationBuilder.DropIndex(
                name: "IX_attendants_UnitId",
                table: "attendants");

            migrationBuilder.DropColumn(
                name: "LastUpdatedAt",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "AttendantId",
                table: "lead_conversations");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "attendants");
        }
    }
}
