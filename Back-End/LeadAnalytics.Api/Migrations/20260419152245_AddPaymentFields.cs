using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "payments",
                type: "numeric(12,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "payments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<decimal>(
                name: "DownPayment",
                table: "payments",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InstallmentValue",
                table: "payments",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "Installments",
                table: "payments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "payments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "payments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "payments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Treatment",
                table: "payments",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TreatmentDurationMonths",
                table: "payments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TreatmentValue",
                table: "payments",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "UnitId",
                table: "payments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_TenantId_PaidAt",
                table: "payments",
                columns: new[] { "TenantId", "PaidAt" });

            migrationBuilder.CreateIndex(
                name: "IX_payments_TenantId_UnitId",
                table: "payments",
                columns: new[] { "TenantId", "UnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_payments_UnitId",
                table: "payments",
                column: "UnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_payments_units_UnitId",
                table: "payments",
                column: "UnitId",
                principalTable: "units",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payments_units_UnitId",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_payments_TenantId_PaidAt",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_payments_TenantId_UnitId",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_payments_UnitId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "DownPayment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "InstallmentValue",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "Installments",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "Treatment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "TreatmentDurationMonths",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "TreatmentValue",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "payments");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "payments",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)");
        }
    }
}
