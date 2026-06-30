using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitSegment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Segment",
                table: "units",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "saude");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Segment",
                table: "units");
        }
    }
}
