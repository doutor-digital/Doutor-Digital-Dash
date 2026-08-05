using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Saude;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Problemas na base e na configuração que inflam ou zeram números em silêncio.
///
/// O QUE ISTO PEGA, E POR QUE CADA UM ESTÁ AQUI
/// --------------------------------------------
/// Todos os três já aconteceram nesta operação, e nenhum deu erro em tela:
///
/// • Leads apagados na Kommo que continuam no nosso banco. Em Imperatriz eram 769 — 9% da
///   base —, todos com etapa de um funil que não existe mais. O sync traz o que existe e
///   nunca marca o que sumiu, então eles ficam contando para sempre.
///
/// • Etapa órfã: lead apontando para um status que não está em nenhuma pipeline da conta.
///   Não entra em card nenhum, mas entra no total.
///
/// • Campo mapeado para outra conta. O mapeamento de Imperatriz apontava para ids da faixa
///   24244xx, de outra unidade. Dez campos ficaram sem mapa e três apontando para o lugar
///   errado — e o painel acusava 0% de preenchimento em campo preenchido em 87%.
///
/// A VALIDAÇÃO DE CONFIGURAÇÃO É CONTRA O QUE O LEAD TEM
/// -----------------------------------------------------
/// Não perguntamos à Kommo se o campo existe: seria uma chamada por unidade a cada
/// carregamento. Comparamos o id mapeado com os ids que aparecem de fato no
/// CustomFieldsJson dos leads da unidade. Se nenhum lead tem aquele campo, ou ele não
/// existe na conta, ou ninguém preenche — e os dois merecem aparecer.
/// </summary>
public class HigieneService(AppDbContext db, KpiConfigService kpiConfig)
{
    private readonly AppDbContext _db = db;
    private readonly KpiConfigService _kpiConfig = kpiConfig;

    public async Task<HigieneDto> GetAsync(int tenantId, int? unitId, CancellationToken ct = default)
    {
        var achados = new List<HigieneAchadoDto>();

        var escopo = _db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && (!unitId.HasValue || l.UnitId == unitId.Value));

        var total = await escopo.CountAsync(ct);

        // ── Etapas que não existem em nenhuma pipeline conhecida ─────────────
        // Etapa canônica é texto ("QUALIFICACAO"); id numérico cru significa que o
        // resolvedor não encontrou o status — quase sempre pipeline apagada.
        var orfaos = await escopo
            .CountAsync(l => l.CurrentStage != null
                             && l.CurrentStage != ""
                             && l.CurrentStage.Length >= 6
                             && l.CurrentStage.All(c => c >= '0' && c <= '9'), ct);

        if (orfaos > 0)
            achados.Add(new HigieneAchadoDto
            {
                Id = "etapa_orfa",
                Titulo = "Leads em etapa que não existe mais",
                Quantidade = orfaos,
                Percentual = total == 0 ? 0 : Math.Round(100.0 * orfaos / total, 1),
                Impacto = "Não entram em nenhum card por etapa, mas entram no total. "
                        + "Costumam ser leads de uma pipeline que foi apagada.",
                Acao = "Verificar se ainda existem na Kommo; se não, marcar como excluídos.",
            });

        // ── Duplicados por telefone ──────────────────────────────────────────
        var duplicados = await escopo
            .Where(l => l.Phone != null && l.Phone != "")
            .GroupBy(l => l.Phone)
            .Where(g => g.Count() > 1)
            .CountAsync(ct);

        if (duplicados > 0)
            achados.Add(new HigieneAchadoDto
            {
                Id = "duplicado_telefone",
                Titulo = "Telefones com mais de um lead",
                Quantidade = duplicados,
                Percentual = total == 0 ? 0 : Math.Round(100.0 * duplicados / total, 1),
                Impacto = "O mesmo paciente conta várias vezes no funil e infla a taxa de perda.",
                Acao = "Usar a tela de duplicados para mesclar.",
            });

        // ── Configuração: campo mapeado que nenhum lead tem ──────────────────
        var camposConfig = new List<HigieneCampoDto>();
        if (unitId is int uid)
        {
            var mapa = await _kpiConfig.GetLeadProfileConfigAsync(uid, ct);

            var amostra = await escopo
                .Where(l => l.CustomFieldsJson != null)
                .OrderByDescending(l => l.CreatedAt)
                .Take(300)
                .Select(l => l.CustomFieldsJson!)
                .ToListAsync(ct);

            void Conferir(string rotulo, long? fieldId)
            {
                if (fieldId is null)
                {
                    camposConfig.Add(new HigieneCampoDto
                    {
                        Campo = rotulo, Situacao = "sem_mapeamento",
                        Detalhe = "Nenhum campo escolhido em Configurações Técnicas.",
                    });
                    return;
                }

                var marca = $"\"field_id\": {fieldId}";
                var marcaCompacta = $"\"field_id\":{fieldId}";
                var achou = amostra.Any(j => j.Contains(marca) || j.Contains(marcaCompacta));

                camposConfig.Add(new HigieneCampoDto
                {
                    Campo = rotulo,
                    Situacao = achou ? "ok" : "nao_encontrado",
                    Detalhe = achou
                        ? null
                        : $"O campo {fieldId} não aparece em nenhum dos últimos 300 leads. "
                          + "Ou não é desta conta Kommo, ou ninguém preenche.",
                });
            }

            Conferir("Origem", mapa.OrigemFieldId);
            Conferir("Qualificação", mapa.QualificacaoFieldId);
            Conferir("Tipo de lead", mapa.TipoFieldId);
            Conferir("Motivo do não agendamento", mapa.MotivoNaoAgendamentoFieldId);
            Conferir("Data de agendamento", mapa.AppointmentFieldId);
            Conferir("Valor do tratamento", mapa.ValorTratamentoFieldId);
            Conferir("Valor da consulta", mapa.ValorConsultaFieldId);
            Conferir("Responsável", mapa.ResponsavelFieldId);
        }

        return new HigieneDto
        {
            TotalLeads = total,
            Achados = [.. achados.OrderByDescending(a => a.Quantidade)],
            Configuracao = camposConfig,
            ConfiguracaoComProblema = camposConfig.Count(c => c.Situacao != "ok"),
        };
    }
}
