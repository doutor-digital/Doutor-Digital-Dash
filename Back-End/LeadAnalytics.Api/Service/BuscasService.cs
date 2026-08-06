using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Saude;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service.Spine;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Perguntas prontas sobre a base — cada uma devolve a contagem e a lista de quem entra nela.
///
/// POR QUE PERGUNTAS, E NÃO UM CONSTRUTOR DE FILTRO
/// ------------------------------------------------
/// Filtro em branco exige que a pessoa já saiba o que procurar, e por isso quase ninguém usa.
/// Aqui cada item é uma pergunta escrita por extenso — "agendaram e não pagaram antecipado" —
/// com o número do lado. Quem abre a página descobre o que dava para perguntar.
///
/// TODAS SÃO AUDITÁVEIS
/// --------------------
/// Nenhuma busca inventa classificação: ou lê coluna do banco, ou lê o campo do cartão da
/// Kommo pelo mesmo caminho que os cards usam — inclusive a queda para o campo gêmeo. Se um
/// número aqui divergir do card, é defeito, e é para aparecer.
/// </summary>
public class BuscasService(AppDbContext db, KpiConfigService kpiConfig)
{
    private readonly AppDbContext _db = db;
    private readonly KpiConfigService _kpiConfig = kpiConfig;

    /// <summary>Teto de nomes devolvidos por busca. A contagem é sempre do total.</summary>
    private const int TetoLista = 200;

    public async Task<List<BuscaDto>> CatalogoAsync(
        int tenantId, int? unitId, DateTime de, DateTime ate, CancellationToken ct = default)
    {
        var mapa = unitId.HasValue
            ? await _kpiConfig.GetLeadProfileConfigAsync(unitId.Value, ct)
            : new KpiConfigService.LeadProfileFields();

        var leads = await _db.Leads.AsNoTracking().ExcludeDeleted()
            .Where(l => l.TenantId == tenantId
                        && (!unitId.HasValue || l.UnitId == unitId.Value)
                        && (l.OriginalCreatedAt ?? l.CreatedAt) >= de
                        && (l.OriginalCreatedAt ?? l.CreatedAt) <= ate)
            .Select(l => new Cru
            {
                Id = l.Id,
                Nome = l.Name,
                Telefone = l.Phone,
                Etapa = l.CurrentStage,
                Criado = l.OriginalCreatedAt ?? l.CreatedAt,
                Consulta = l.AppointmentScheduledAt,
                Qualificacao = l.Qualification,
                Json = l.CustomFieldsJson,
            })
            .ToListAsync(ct);

        string? Campo(Cru l, long? id, Func<string, bool> porNome) =>
            KpiConfigService.ExtractFieldPublic(l.Json, id, porNome);

        static bool Vazio(string? s) => string.IsNullOrWhiteSpace(s);
        static bool Agendado(Cru l) =>
            (l.Etapa ?? "").Contains("AGENDADO", StringComparison.OrdinalIgnoreCase);

        // Fim de semana pelo horário da clínica: sábado no fuso local, não no UTC.
        static bool FimDeSemana(DateTime utc)
        {
            var d = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utc, DateTimeKind.Utc), SpineApiClient.BrTz);
            return d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        }

        var buscas = new List<(string id, string titulo, string porque, Func<Cru, bool> filtro)>
        {
            ("fim_de_semana", "Entraram no fim de semana",
             "Ninguém atendeu na hora. É a fila de retomada da segunda.",
             l => FimDeSemana(l.Criado)),

            ("agendado_sem_antecipado", "Agendaram e não pagaram antecipado",
             "Consulta marcada sem pagamento na frente falta mais.",
             l => Agendado(l) && !Sim(Campo(l, null, n => n.Contains("antecipad")))),

            ("agendado_sem_data", "Agendados sem data de consulta",
             "Sem data não entra em lembrete nenhum.",
             l => Agendado(l) && l.Consulta is null),

            ("sem_origem", "Sem origem preenchida",
             "Some da conta por canal e estraga o custo por lead.",
             l => Vazio(Campo(l, mapa.OrigemFieldId, n => n.Contains("origem")))),

            ("sem_qualificacao", "Sem qualificação",
             "Ninguém disse se é quente, morno ou frio.",
             l => Vazio(l.Qualificacao)
                  && Vazio(Campo(l, mapa.QualificacaoFieldId, n => n.Contains("qualifica")))),

            ("perdido_sem_motivo", "Perdidos sem motivo escrito",
             "Perda sem motivo não vira aprendizado nenhum.",
             l => (l.Etapa ?? "").Contains("PERDIDO", StringComparison.OrdinalIgnoreCase)
                  && Vazio(Campo(l, mapa.MotivoNaoAgendamentoFieldId,
                                 n => n.Contains("motivo")))),

            ("sem_responsavel", "Sem responsável pelo agendamento",
             "Sem dono não há cobrança individual nem ranking.",
             l => Vazio(Campo(l, mapa.ResponsavelFieldId,
                              n => n.Contains("respons") && n.Contains("agendamento")))),

            ("tratamento_sem_valor", "Fecharam tratamento sem valor",
             "Receita que existe e não aparece em lugar nenhum.",
             l => !Vazio(Campo(l, mapa.TratamentoFechadoFieldId, n => n.Contains("fech")))
                  && Vazio(Campo(l, mapa.ValorTratamentoFieldId,
                                 n => n.Contains("valor") && n.Contains("tratamento")))),

            ("ia_pausada", "Com a IA pausada",
             "A Sofia não responde estes — o atendimento é humano.",
             l => Sim(Campo(l, mapa.PausarIaFieldId, n => n.Contains("pausar")))),

            ("sem_telefone", "Sem telefone gravado",
             "Não dá para ligar, nem juntar duplicado.",
             l => Vazio(l.Telefone) || l.Telefone == "AGUARDANDO_COLETA"),
        };

        return [.. buscas
            .Select(b =>
            {
                var achados = leads.Where(b.filtro).ToList();
                return new BuscaDto
                {
                    Id = b.id,
                    Titulo = b.titulo,
                    Porque = b.porque,
                    Quantidade = achados.Count,
                    Percentual = leads.Count == 0 ? 0
                        : Math.Round(100.0 * achados.Count / leads.Count, 1),
                    Itens = [.. achados
                        .OrderByDescending(l => l.Criado)
                        .Take(TetoLista)
                        .Select(l => new BuscaItemDto
                        {
                            LeadId = l.Id,
                            Nome = l.Nome,
                            Telefone = l.Telefone,
                            Etapa = l.Etapa,
                            Quando = l.Criado,
                        })],
                };
            })
            // Busca vazia continua na lista, mas no fim: saber que ninguém está sem origem
            // é informação, e some se a linha desaparecer.
            .OrderByDescending(b => b.Quantidade)];
    }

    /// <summary>Campo de "sim/não" da Kommo: aceita marcado, "Sim" e "1".</summary>
    private static bool Sim(string? v)
    {
        var s = (v ?? string.Empty).Trim();
        return s.Equals("true", StringComparison.OrdinalIgnoreCase)
            || s.Equals("sim", StringComparison.OrdinalIgnoreCase)
            || s == "1";
    }

    private sealed class Cru
    {
        public int Id { get; init; }
        public string? Nome { get; init; }
        public string? Telefone { get; init; }
        public string? Etapa { get; init; }
        public DateTime Criado { get; init; }
        public DateTime? Consulta { get; init; }
        public string? Qualificacao { get; init; }
        public string? Json { get; init; }
    }
}
