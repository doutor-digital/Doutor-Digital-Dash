using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAdsIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ad_accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClinicId = table.Column<int>(type: "integer", nullable: false),
                    UnitId = table.Column<int>(type: "integer", nullable: true),
                    Provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExternalAccountId = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AccessTokenEnc = table.Column<string>(type: "text", nullable: true),
                    RefreshTokenEnc = table.Column<string>(type: "text", nullable: true),
                    TokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncNote = table.Column<string>(type: "text", nullable: true),
                    UpdatedByEmail = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ad_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "campaign_daily_spend",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClinicId = table.Column<int>(type: "integer", nullable: false),
                    AdAccountId = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CampaignId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CampaignName = table.Column<string>(type: "text", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Spend = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaign_daily_spend", x => x.Id);
                    table.ForeignKey(
                        name: "FK_campaign_daily_spend_ad_accounts_AdAccountId",
                        column: x => x.AdAccountId,
                        principalTable: "ad_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ad_accounts_ClinicId_Provider",
                table: "ad_accounts",
                columns: new[] { "ClinicId", "Provider" });

            migrationBuilder.CreateIndex(
                name: "IX_campaign_daily_spend_AdAccountId_CampaignId_Date",
                table: "campaign_daily_spend",
                columns: new[] { "AdAccountId", "CampaignId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_campaign_daily_spend_ClinicId_Date",
                table: "campaign_daily_spend",
                columns: new[] { "ClinicId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "campaign_daily_spend");

            migrationBuilder.DropTable(
                name: "ad_accounts");
        }
    }
}
