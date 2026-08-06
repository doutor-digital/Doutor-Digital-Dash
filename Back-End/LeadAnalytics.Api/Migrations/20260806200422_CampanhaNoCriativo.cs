using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class CampanhaNoCriativo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CampaignId",
                table: "ad_creatives",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CampaignName",
                table: "ad_creatives",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CampaignId",
                table: "ad_creatives");

            migrationBuilder.DropColumn(
                name: "CampaignName",
                table: "ad_creatives");
        }
    }
}
