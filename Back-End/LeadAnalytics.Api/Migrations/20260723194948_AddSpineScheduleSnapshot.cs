using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSpineScheduleSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "spine_schedule_snapshot",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UnitId = table.Column<int>(type: "integer", nullable: false),
                    IdSchedule = table.Column<long>(type: "bigint", nullable: false),
                    IdTreatment = table.Column<long>(type: "bigint", nullable: true),
                    DateAttendanceUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DiaLocal = table.Column<DateOnly>(type: "date", nullable: false),
                    IdCategory = table.Column<int>(type: "integer", nullable: false),
                    Categoria = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Paciente = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Profissional = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IdStatus = table.Column<int>(type: "integer", nullable: false),
                    StatusName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ModifiedAtSpine = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedBySpine = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spine_schedule_snapshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_spine_schedule_snapshot_units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_spine_schedule_snapshot_UnitId_DiaLocal",
                table: "spine_schedule_snapshot",
                columns: new[] { "UnitId", "DiaLocal" });

            migrationBuilder.CreateIndex(
                name: "IX_spine_schedule_snapshot_UnitId_IdSchedule",
                table: "spine_schedule_snapshot",
                columns: new[] { "UnitId", "IdSchedule" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "spine_schedule_snapshot");
        }
    }
}
