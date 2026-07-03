using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardPerformanceIndexes : Migration
    {
        // Índices que aceleram o GetDashboardOverviewAsync (e os KPIs/breakdowns que
        // reutilizam os mesmos filtros). O filtro dominante do dashboard é a janela por
        // COALESCE("OriginalCreatedAt","CreatedAt") dentro de tenant+unidade — que NENHUM
        // índice atendia (os índices em CreatedAt e OriginalCreatedAt separados não são
        // usados sob COALESCE, forçando seq scan da tabela inteira em CADA contagem).
        //
        // Todos são parciais em Status <> 'deleted' porque o dashboard sempre aplica
        // ExcludeDeleted() — deixa o índice menor e 100% alinhado ao predicado.
        // CREATE INDEX (não CONCURRENTLY) porque migrations rodam em transação; o lock é
        // breve e acontece uma vez no boot do deploy.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Janela por data efetiva de criação, escopada por tenant+unidade.
            //    Serve totalLeads, funil, origens, etapas, estados — a maioria das contagens.
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_leads_Tenant_Unit_EffCreated""
                ON leads (""TenantId"", ""UnitId"", (COALESCE(""OriginalCreatedAt"", ""CreatedAt"")))
                WHERE ""Status"" <> 'deleted';");

            // 2) Contagens/agrupamentos por etapa (CurrentStage == X, GROUP BY CurrentStage).
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_leads_Tenant_Unit_Stage""
                ON leads (""TenantId"", ""UnitId"", ""CurrentStage"")
                WHERE ""Status"" <> 'deleted';");

            // 3) Consultas: filtro por Data de agendamento preenchida dentro da janela.
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_leads_Tenant_Unit_ApptFilled""
                ON leads (""TenantId"", ""UnitId"", ""AppointmentScheduledAtFilledAt"")
                WHERE ""AppointmentScheduledAtFilledAt"" IS NOT NULL AND ""Status"" <> 'deleted';");

            // 4) Agendados / No-show por DATA DE ENTRADA na etapa (histórico). Filtro:
            //    StageLabel IN (...) AND COALESCE(CorrectedChangedAt,ChangedAt) na janela.
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_lsh_Stage_EffChanged""
                ON lead_stage_histories (""StageLabel"", (COALESCE(""CorrectedChangedAt"", ""ChangedAt"")), ""LeadId"");");

            // 5) Resgate: recovery_attempts por EntrySource + janela de CreatedAt.
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_recovery_EntrySource_Created""
                ON recovery_attempts (""EntrySource"", ""CreatedAt"", ""LeadId"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_recovery_EntrySource_Created"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_lsh_Stage_EffChanged"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_leads_Tenant_Unit_ApptFilled"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_leads_Tenant_Unit_Stage"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_leads_Tenant_Unit_EffCreated"";");
        }
    }
}
