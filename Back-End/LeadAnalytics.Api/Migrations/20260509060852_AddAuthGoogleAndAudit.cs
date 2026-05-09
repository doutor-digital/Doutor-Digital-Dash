using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthGoogleAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "auth_method",
                table: "users",
                type: "text",
                nullable: false,
                defaultValue: "password");

            migrationBuilder.AddColumn<string>(
                name: "google_sub",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    email = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    user_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    tenant_id = table.Column<int>(type: "integer", nullable: true),
                    auth_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    path = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    query_string = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invitations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    email = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    tenant_id = table.Column<int>(type: "integer", nullable: false),
                    unit_id = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invitations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_units",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    unit_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_units", x => new { x.user_id, x.unit_id });
                    table.ForeignKey(
                        name: "FK_user_units_units_unit_id",
                        column: x => x.unit_id,
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_units_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_google_sub",
                table: "users",
                column: "google_sub",
                unique: true,
                filter: "\"google_sub\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_created_at",
                table: "audit_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_path_created_at",
                table: "audit_logs",
                columns: new[] { "path", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_user_id_created_at",
                table: "audit_logs",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_invitations_email_unit_id_accepted_at",
                table: "invitations",
                columns: new[] { "email", "unit_id", "accepted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_invitations_tenant_id",
                table: "invitations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_token_hash",
                table: "invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_units_unit_id",
                table: "user_units",
                column: "unit_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "invitations");

            migrationBuilder.DropTable(
                name: "user_units");

            migrationBuilder.DropIndex(
                name: "IX_users_google_sub",
                table: "users");

            migrationBuilder.DropColumn(
                name: "auth_method",
                table: "users");

            migrationBuilder.DropColumn(
                name: "google_sub",
                table: "users");
        }
    }
}
