using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class MapaDeEtapasKommo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "franquia_lead_link",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UnitId = table.Column<int>(type: "integer", nullable: false),
                    IdTreatment = table.Column<long>(type: "bigint", nullable: false),
                    DiaLancamento = table.Column<DateOnly>(type: "date", nullable: false),
                    Paciente = table.Column<string>(type: "text", nullable: true),
                    Telefone = table.Column<string>(type: "text", nullable: true),
                    PrecoFranquia = table.Column<decimal>(type: "numeric", nullable: true),
                    LeadId = table.Column<long>(type: "bigint", nullable: true),
                    ValorKommo = table.Column<decimal>(type: "numeric", nullable: true),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_franquia_lead_link", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_franquia_lead_link_UnitId_DiaLancamento",
                table: "franquia_lead_link",
                columns: new[] { "UnitId", "DiaLancamento" });

            migrationBuilder.CreateIndex(
                name: "IX_franquia_lead_link_UnitId_IdTreatment",
                table: "franquia_lead_link",
                columns: new[] { "UnitId", "IdTreatment" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "franquia_lead_link");
        }
    }
}
