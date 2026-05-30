using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadCustomFieldsAndTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomFieldsJson",
                table: "leads",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TagsJson",
                table: "leads",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomFieldsJson",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "TagsJson",
                table: "leads");
        }
    }
}
