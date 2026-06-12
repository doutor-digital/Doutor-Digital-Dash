using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeadAnalytics.Api.Migrations
{
    /// <summary>
    /// Backfill: marca como "tem pagamento" todos os leads que estão (ou passaram) na
    /// etapa "05_AGENDADO_COM_PAGAMENTO" — o stage por definição significa que o lead
    /// já pagou antecipado. Antes só o PaymentService setava HasPayment, então o card
    /// "Agendados" mostrava "Sem pagamento antecipado" pra esses leads. Sem schema change.
    /// </summary>
    public partial class BackfillHasPaymentForScheduledStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Leads ATUALMENTE na etapa "Agendado com pagamento".
            migrationBuilder.Sql(
                @"UPDATE leads
                    SET ""HasPayment"" = TRUE
                  WHERE ""CurrentStage"" = '05_AGENDADO_COM_PAGAMENTO'
                    AND ""HasPayment"" = FALSE;");

            // Leads que JÁ passaram por 05_* (têm Consultation com paid_in_advance=true).
            // Eles continuam pagantes mesmo depois de subir o funil — usado pelo
            // KPI drill-down do Agendados pra mostrar o chip "Pagamento antecipado".
            // Identifiers da tabela leads são PascalCase quoted ("Id"); consultations
            // usa snake_case via [Column(...)] no model (lead_id / paid_in_advance).
            migrationBuilder.Sql(
                @"UPDATE leads
                    SET ""HasPayment"" = TRUE
                  WHERE ""HasPayment"" = FALSE
                    AND ""Id"" IN (
                      SELECT DISTINCT lead_id FROM consultations WHERE paid_in_advance = TRUE
                    );");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Backfill é puro UPDATE em estado existente — não há como reverter sem
            // perder informações legítimas. Deixar como no-op (já estava assim antes).
        }
    }
}
