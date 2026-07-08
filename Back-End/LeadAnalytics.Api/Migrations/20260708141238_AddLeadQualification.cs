using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadQualification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Qualification",
                table: "leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QualificationFilledAt",
                table: "leads",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill dos leads já existentes:
            //  • Qualification recebe o valor atual do custom field "Qualificação do lead"
            //    (lido do CustomFieldsJson). Sem isso, o próximo sync veria "null → valor" e
            //    re-carimbaria QualificationFilledAt = agora em TODOS de uma vez (pico falso).
            //  • QualificationFilledAt recebe a data de criação (OriginalCreatedAt ?? CreatedAt)
            //    como aproximação — não temos a data real de preenchimento do passado. Isso
            //    mantém o widget histórico populado; daqui pra frente, toda mudança do campo
            //    passa a carimbar a data real (produtividade do dia).
            migrationBuilder.Sql(@"
                UPDATE leads l
                SET ""Qualification"" = sub.val,
                    ""QualificationFilledAt"" = COALESCE(l.""OriginalCreatedAt"", l.""CreatedAt"")
                FROM (
                    SELECT DISTINCT ON (le.""Id"") le.""Id"",
                           trim(COALESCE(elem->>'value', (elem->'values'->0->>'value'))) AS val
                    FROM leads le,
                         LATERAL jsonb_array_elements(le.""CustomFieldsJson"") elem
                    WHERE le.""CustomFieldsJson"" IS NOT NULL
                      AND jsonb_typeof(le.""CustomFieldsJson"") = 'array'
                      AND elem->>'field_name' ILIKE '%qualifica%'
                      AND COALESCE(elem->>'value', (elem->'values'->0->>'value')) IS NOT NULL
                ) sub
                WHERE l.""Id"" = sub.""Id""
                  AND sub.val <> ''
                  AND l.""Qualification"" IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Qualification",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "QualificationFilledAt",
                table: "leads");
        }
    }
}
