using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitProfileAndSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressLine",
                table: "units",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "units",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cnpj",
                table: "units",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "units",
                type: "text",
                nullable: true);

            // Unidades já existentes nascem ativas (senão o webhook delas seria recusado).
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "units",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "KommoAccountId",
                table: "units",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KommoStageMapJson",
                table: "units",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KommoSubdomain",
                table: "units",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "units",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoUrl",
                table: "units",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponsibleName",
                table: "units",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "units",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "units",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "units",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill: unidades já existentes (Slug NULL) ganham um slug estável
            // baseado no ClinicId, pra que a URL do webhook delas também funcione.
            migrationBuilder.Sql(
                "UPDATE units SET \"Slug\" = 'unidade-' || \"ClinicId\" WHERE \"Slug\" IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_units_Slug",
                table: "units",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_units_Slug",
                table: "units");

            migrationBuilder.DropColumn(
                name: "AddressLine",
                table: "units");

            migrationBuilder.DropColumn(
                name: "City",
                table: "units");

            migrationBuilder.DropColumn(
                name: "Cnpj",
                table: "units");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "units");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "units");

            migrationBuilder.DropColumn(
                name: "KommoAccountId",
                table: "units");

            migrationBuilder.DropColumn(
                name: "KommoStageMapJson",
                table: "units");

            migrationBuilder.DropColumn(
                name: "KommoSubdomain",
                table: "units");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "units");

            migrationBuilder.DropColumn(
                name: "PhotoUrl",
                table: "units");

            migrationBuilder.DropColumn(
                name: "ResponsibleName",
                table: "units");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "units");

            migrationBuilder.DropColumn(
                name: "State",
                table: "units");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "units");
        }
    }
}
