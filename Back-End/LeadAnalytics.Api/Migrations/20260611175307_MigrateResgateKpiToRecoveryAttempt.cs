using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <summary>
    /// Reconfigura cards "Resgate" existentes (source_type='custom_field_count' filtrando o
    /// custom field "Tipo do lead" por valores que contenham "resgate") pro novo source
    /// 'recovery_attempt', que conta pela DATA DO EVENTO (preenchimento do field
    /// "Tentativas de resgastes" na Kommo) em vez da data de criação do lead.
    ///
    /// Por que: contar resgate por data de criação faz com que (1) leads antigos recuperados
    /// hoje não apareçam no card do dia, e (2) mudar o tipo de um lead retroativamente altere
    /// relatórios passados. recovery_attempt resolve os dois — conta no dia da tentativa.
    /// </summary>
    public partial class MigrateResgateKpiToRecoveryAttempt : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE kpi_configurations
                SET    ""SourceType"" = 'recovery_attempt',
                       ""ConfigJson"" = '{}'::jsonb,
                       ""UpdatedAt""  = NOW()
                WHERE  ""KpiKey""     = 'resgate'
                  AND  ""SourceType"" = 'custom_field_count'
                  AND  EXISTS (
                           SELECT 1
                           FROM   jsonb_array_elements_text(""ConfigJson"" -> 'matchValues') AS v
                           WHERE  lower(v) LIKE '%resgate%'
                       );
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversível: o ConfigJson original (fieldId, matchValues etc.) foi descartado
            // pra forçar a nova semântica. Pra reverter, o analista reconfigura na UI.
        }
    }
}
