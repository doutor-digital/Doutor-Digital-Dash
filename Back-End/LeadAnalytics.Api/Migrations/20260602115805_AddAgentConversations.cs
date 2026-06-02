using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_conversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    UnitId = table.Column<int>(type: "integer", nullable: true),
                    LeadId = table.Column<int>(type: "integer", nullable: true),
                    ContactId = table.Column<int>(type: "integer", nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    AgentName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    PhoneNormalized = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HandedOff = table.Column<bool>(type: "boolean", nullable: false),
                    HandoffAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Intent = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Sentiment = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    MessageCount = table.Column<int>(type: "integer", nullable: false),
                    TokensIn = table.Column<int>(type: "integer", nullable: true),
                    TokensOut = table.Column<int>(type: "integer", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstMessageAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastMessageAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_conversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_conversations_contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_agent_conversations_leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_agent_conversations_units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "agent_messages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AgentConversationId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ToolName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_messages_agent_conversations_AgentConversationId",
                        column: x => x.AgentConversationId,
                        principalTable: "agent_conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_conversations_ContactId",
                table: "agent_conversations",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_conversations_LeadId",
                table: "agent_conversations",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_conversations_PhoneNormalized",
                table: "agent_conversations",
                column: "PhoneNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_agent_conversations_TenantId_ExternalId",
                table: "agent_conversations",
                columns: new[] { "TenantId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_conversations_TenantId_LastMessageAt",
                table: "agent_conversations",
                columns: new[] { "TenantId", "LastMessageAt" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_conversations_TenantId_Status",
                table: "agent_conversations",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_conversations_UnitId_StartedAt",
                table: "agent_conversations",
                columns: new[] { "UnitId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_messages_AgentConversationId_SentAt",
                table: "agent_messages",
                columns: new[] { "AgentConversationId", "SentAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_messages");

            migrationBuilder.DropTable(
                name: "agent_conversations");
        }
    }
}
