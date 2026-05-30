using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook_executions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    unit_id = table.Column<int>(type: "integer", nullable: true),
                    tenant_id = table.Column<int>(type: "integer", nullable: true),
                    kommo_account_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    kommo_subdomain = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    path = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    content_length = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    error_stack = table.Column<string>(type: "text", nullable: true),
                    form_keys = table.Column<string>(type: "text", nullable: true),
                    form_key_count = table.Column<int>(type: "integer", nullable: false),
                    raw_payload = table.Column<string>(type: "text", nullable: true),
                    payload_truncated = table.Column<bool>(type: "boolean", nullable: false),
                    events_parsed = table.Column<int>(type: "integer", nullable: false),
                    events_summary = table.Column<string>(type: "jsonb", nullable: true),
                    leads_persisted = table.Column<int>(type: "integer", nullable: false),
                    response_body = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_executions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_executions_received_at",
                table: "webhook_executions",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_executions_status_received_at",
                table: "webhook_executions",
                columns: new[] { "status", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_executions_tenant_id_received_at",
                table: "webhook_executions",
                columns: new[] { "tenant_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_executions_unit_id_received_at",
                table: "webhook_executions",
                columns: new[] { "unit_id", "received_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_executions");
        }
    }
}
