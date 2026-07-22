using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAdsDeliveryMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Clicks",
                table: "campaign_daily_spend",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "Conversations",
                table: "campaign_daily_spend",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "Impressions",
                table: "campaign_daily_spend",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Reach",
                table: "campaign_daily_spend",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Clicks",
                table: "campaign_daily_spend");

            migrationBuilder.DropColumn(
                name: "Conversations",
                table: "campaign_daily_spend");

            migrationBuilder.DropColumn(
                name: "Impressions",
                table: "campaign_daily_spend");

            migrationBuilder.DropColumn(
                name: "Reach",
                table: "campaign_daily_spend");
        }
    }
}
