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
                name: "kommo_stages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UnitId = table.Column<int>(type: "integer", nullable: false),
                    PipelineId = table.Column<long>(type: "bigint", nullable: false),
                    PipelineName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    StatusId = table.Column<long>(type: "bigint", nullable: false),
                    StatusName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Sort = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kommo_stages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_kommo_stages_UnitId_PipelineId_StatusId",
                table: "kommo_stages",
                columns: new[] { "UnitId", "PipelineId", "StatusId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kommo_stages_UnitId_StatusId",
                table: "kommo_stages",
                columns: new[] { "UnitId", "StatusId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kommo_stages");
        }
    }
}
