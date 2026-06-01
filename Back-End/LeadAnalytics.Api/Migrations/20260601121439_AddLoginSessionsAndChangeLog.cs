using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginSessionsAndChangeLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "location_consent",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "location_consent_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "entity_change_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    email = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    tenant_id = table.Column<int>(type: "integer", nullable: true),
                    entity_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    changes_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_change_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "login_sessions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    email = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    user_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    tenant_id = table.Column<int>(type: "integer", nullable: true),
                    auth_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    device = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    geo_country = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    geo_region = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    geo_city = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    accuracy = table.Column<double>(type: "double precision", nullable: true),
                    geo_consent = table.Column<bool>(type: "boolean", nullable: false),
                    geo_consent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    active_seconds = table.Column<long>(type: "bigint", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    end_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_login_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_entity_change_logs_created_at",
                table: "entity_change_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_entity_change_logs_entity_type_entity_id",
                table: "entity_change_logs",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_entity_change_logs_user_id_created_at",
                table: "entity_change_logs",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_login_sessions_is_active",
                table: "login_sessions",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_login_sessions_user_id_login_at",
                table: "login_sessions",
                columns: new[] { "user_id", "login_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_change_logs");

            migrationBuilder.DropTable(
                name: "login_sessions");

            migrationBuilder.DropColumn(
                name: "location_consent",
                table: "users");

            migrationBuilder.DropColumn(
                name: "location_consent_at",
                table: "users");
        }
    }
}
