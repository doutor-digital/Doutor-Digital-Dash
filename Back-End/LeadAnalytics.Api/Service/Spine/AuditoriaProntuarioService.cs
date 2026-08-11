using System.Globalization;
using LeadAnalytics.Api.Data;
using System.Text.RegularExpressions;
using LeadAnalytics.Api.DTOs.Spine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LeadAnalytics.Api.Service.Spine;

/// <summary>
/// Auditoria dos prontuários da unidade, com cache.
///
/// As regras saíram da revisão manual de um prontuário real (atendimento #2470019,
/// Imperatriz) e foram generalizadas. O que elas procuram não é erro clínico — é registro
/// que não sustenta auditoria: métrica criada depois do fato, alta sem teste nomeado,
/// numeração de protocolo que não fecha.
///
/// Cache longo de propósito: uma varredura abre uma ficha de ~290 KB por tratamento e
/// leva minutos. Não é algo para rodar a cada request.
/// </summary>
public partial class AuditoriaProntuarioService(
    AppDbContext db,
    FranquiaWebStore store,
    FranquiaAuditoriaClient client,
    IMemoryCache cache,
    ILogger<AuditoriaProntuarioService> logger)
{
    private const int CacheSegundos = 1800;

    private readonly AppDbContext _db = db;
    private readonly FranquiaWebStore _store = store;
    private readonly FranquiaAuditoriaClient _client = client;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<AuditoriaProntuarioService> _logger = logger;

    private static readonly Dictionary<string, int> Peso = new()
    {
        ["critico"] = 10,
        ["alerta"] = 3,
        ["info"] = 1,
    };

    /// <summary>Null quando a unidade não tem credencial do CRM web configurada.</summary>
    public async Task<AuditoriaDto?> GetAsync(
        int unitId, DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        var key = $"franquia:auditoria:{unitId}:{de:yyyyMMdd}:{ate:yyyyMMdd}";
        if (_cache.TryGetValue(key, out AuditoriaDto? cached) && cached is not null)
            return cached;

        // O nome da unidade é a única forma de conferir que o filtro pegou: quando
        // `created` vai vazio o controller PHP devolve a rede inteira sem sinalizar erro.
        var unidade = await _db.Units.AsNoTracking()
            .Where(u => u.Id == unitId).Select(u => u.Name).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(unidade)) return null;

        var creds = await _store.GetAsync(unitId, ct);
        if (creds is null) return null;

        var (email, pass, idCompany) = creds.Value;
        var brutos = await _client.GetProntuariosAsync(email, pass, idCompany, unidade, de, ate, ct);

        var dto = Montar(brutos, unidade, $"{de:dd/MM/yyyy} - {ate:dd/MM/yyyy}");
        _cache.Set(key, dto, TimeSpan.FromSeconds(CacheSegundos));

        _logger.LogInformation(
            "Auditoria: {Atend} atendimentos → {Fichas} fichas, {Crit} críticos, {Alert} alertas (unit {Unit}, {De}..{Ate})",
            dto.Atendimentos, dto.Total, dto.Criticos, dto.Alertas, unitId, de, ate);

        return dto;
    }

    /// <summary>
    /// Agrupa por tratamento e roda as regras. /acompanhar/{id} abre a MESMA ficha para
    /// todas as sessões de um tratamento — sem agrupar, cada achado seria contado uma vez
    /// por sessão.
    /// </summary>
    private static AuditoriaDto Montar(List<AuditoriaProntuarioDto> brutos, string unidade, string periodo)
    {
        var porChave = new Dictionary<string, AuditoriaProntuarioDto>();
        foreach (var p in brutos)
        {
            if (!porChave.TryGetValue(p.Chave, out var ja))
            {
                porChave[p.Chave] = p;
                continue;
            }

            ja.Atendimentos.AddRange(p.Atendimentos);

            // A leitura mais completa vira a canônica; as sessões acumulam.
            if (ja.Evolucoes.Count >= p.Evolucoes.Count) continue;
            p.Atendimentos = ja.Atendimentos;
            p.Principal = ja.Principal;
            porChave[p.Chave] = p;
        }

        var fichas = porChave.Values.ToList();
        foreach (var p in fichas)
        {
            p.Atendimentos = [.. p.Atendimentos.OrderByDescending(a => a.Id)];
            p.Achados = Auditar(p);
            p.Escore = p.Achados.Sum(a => Peso.GetValueOrDefault(a.Severidade, 1));
        }

        fichas = [.. fichas.OrderByDescending(p => p.Escore).ThenByDescending(p => p.Principal.Id)];

        var contaRegra = fichas
            .SelectMany(p => p.Achados)
            .GroupBy(a => a.Regra)
            .ToDictionary(g => g.Key, g => g.ToList());

        return new AuditoriaDto
        {
            Unidade = unidade,
            Periodo = periodo,
            Atendimentos = brutos.Sum(p => p.Atendimentos.Count),
            Total = fichas.Count,
            Avaliacoes = fichas.Count(p => p.Tipo == "avaliacao"),
            ComAchados = fichas.Count(p => p.Achados.Count > 0),
            Criticos = fichas.Sum(p => p.Achados.Count(a => a.Severidade == "critico")),
            Alertas = fichas.Sum(p => p.Achados.Count(a => a.Severidade == "alerta")),
            AtualizadoEm = DateTime.UtcNow,
            Prontuarios = fichas,
            PorRegra = [.. contaRegra
                .Select(kv => new AuditoriaRegraDto
                {
                    Regra = kv.Key,
                    Severidade = kv.Value[0].Severidade,
                    Titulo = kv.Value[0].Titulo,
                    Total = kv.Value.Count,
                })
                .OrderByDescending(r => r.Total)],
            PorProfissional = [.. fichas
                .GroupBy(p => string.IsNullOrWhiteSpace(p.Principal.Fisioterapeuta) ? "—" : p.Principal.Fisioterapeuta)
                .Select(g => new AuditoriaProfissionalDto
                {
                    Nome = g.Key,
                    Atendimentos = g.Sum(p => p.Atendimentos.Count),
                    Criticos = g.Sum(p => p.Achados.Count(a => a.Severidade == "critico")),
                    Alertas = g.Sum(p => p.Achados.Count(a => a.Severidade == "alerta")),
                })
                .OrderByDescending(x => x.Criticos * 10 + x.Alertas)],
        };
    }

    private static AuditoriaAchadoDto Achado(string regra, string sev, string titulo, string detalhe)
        => new() { Regra = regra, Severidade = sev, Titulo = titulo, Detalhe = detalhe };

    private static string Br(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static List<AuditoriaAchadoDto> Auditar(AuditoriaProntuarioDto p)
    {
        var a = new List<AuditoriaAchadoDto>();
        var alta = p.Evolucoes.FirstOrDefault(e => AltaRegex().IsMatch(e.Texto));

        // ── Críticos ────────────────────────────────────────────────────────────

        // Questionário "inicial" criado muito depois da 1ª consulta mede memória, não estado.
        if (p.Questionario?.CriadoEmIso is { } criado && p.PrimeiraIso is { } primeira)
        {
            var dias = criado.DayNumber - primeira.DayNumber;
            if (dias >= 7)
            {
                var naAlta = alta is not null && alta.DataIso == criado;
                a.Add(Achado("questionario-retroativo", "critico", "Questionário de incapacidade preenchido retroativamente",
                    $"A coluna \"Início\" mede o estado do paciente na 1ª consulta ({p.PrimeiraConsulta}), mas o registro só foi criado em {p.Questionario.CriadoEm} — {dias} dias depois"
                    + (naAlta ? ", e no mesmo dia da alta, junto com a coluna \"Final\"" : "")
                    + $". O escore inicial ({p.Questionario.EscoreInicial}/24) é reconstrução de memória, não medição."));
            }
        }

        // Alta que não nomeia teste não é auditável por terceiro.
        if (alta is not null && !TestesRegex().IsMatch(alta.Texto))
        {
            a.Add(Achado("alta-sem-testes", "critico", "Alta sem testes objetivos nomeados",
                CitaTesteRegex().IsMatch(alta.Texto)
                    ? $"A evolução de {alta.Data} afirma testes negativos mas não nomeia nenhum deles. Sem o teste identificado e seu resultado, a alta não é auditável."
                    : $"A evolução de {alta.Data} concede alta sem registrar nenhum teste objetivo de reavaliação."));
        }

        foreach (var s in p.Atendimentos.Where(s => s.DuracaoMin is > 0 and < 5))
        {
            a.Add(Achado("sessao-relampago", "critico", "Atendimento com duração implausível",
                $"Atendimento #{s.Id} ({s.Inicio}) registrado com {s.DuracaoMin} minuto(s). Sessão de fisioterapia não ocorre nesse tempo — indica abertura/fechamento indevido ou registro sem atendimento real."));
        }

        if (p.Tipo == "tratamento" && p.Evolucoes.Count == 0
            && p.Atendimentos.Any(s => s.Situacao.Contains("ATENDIDO", StringComparison.OrdinalIgnoreCase)))
        {
            a.Add(Achado("atendido-sem-evolucao", "critico", "Tratamento concluído sem evolução registrada",
                $"{p.Atendimentos.Count} atendimento(s) marcados como ATENDIDO mas a aba Evolução está vazia. Prontuário sem registro do que foi executado."));
        }

        // ── Alertas ─────────────────────────────────────────────────────────────

        var dupRotulo = p.Evolucoes.Where(e => e.DiaRotulo is not null)
            .GroupBy(e => e.DiaRotulo!.Value).Where(g => g.Count() > 1).ToList();
        if (dupRotulo.Count > 0)
        {
            a.Add(Achado("dia-duplicado", "alerta", "Numeração de dia duplicada na evolução",
                string.Join("; ", dupRotulo.Select(g => $"DIA {g.Key} em {string.Join(" e ", g.Select(e => e.Data))}"))
                + ". Com rótulos repetidos, o dia de protocolo correspondente nunca aparece — sessões cobradas não batem com dias de protocolo distintos aplicados."));
        }

        var divergentes = p.Evolucoes
            .Where(e => e.DiaRotulo is not null && e.DiaCorpo is not null && e.DiaRotulo != e.DiaCorpo).ToList();
        if (divergentes.Count > 0)
        {
            a.Add(Achado("cabecalho-x-corpo", "alerta", "Cabeçalho da evolução diverge do protocolo descrito",
                $"{divergentes.Count} registro(s) divergentes: "
                + string.Join("; ", divergentes.Select(e => $"{e.Data} (cabeçalho DIA {e.DiaRotulo}, corpo \"protocolo do DIA {e.DiaCorpo}\")")) + "."));
        }

        var dupCorpo = p.Evolucoes.Where(e => e.DiaCorpo is not null)
            .GroupBy(e => e.DiaCorpo!.Value).Where(g => g.Count() > 1).ToList();
        if (dupCorpo.Count > 0)
        {
            a.Add(Achado("protocolo-repetido", "alerta", "Mesmo dia de protocolo aplicado em sessões diferentes",
                string.Join("; ", dupCorpo.Select(g => $"protocolo do DIA {g.Key} em {string.Join(" e ", g.Select(e => e.Data))}"))
                + ". O dia de protocolo seguinte não chegou a ser executado."));
        }

        var datas = p.Evolucoes.Where(e => e.DataIso is not null).ToList();
        if (datas.Count >= 3)
        {
            for (var i = 1; i < datas.Count; i++)
            {
                var dias = datas[i].DataIso!.Value.DayNumber - datas[i - 1].DataIso!.Value.DayNumber;
                if (dias < 14 || JustificaRegex().IsMatch(datas[i].Texto)) continue;
                a.Add(Achado("gap-sessoes", "alerta", "Intervalo longo entre sessões sem justificativa",
                    $"{dias} dias sem sessão entre {Br(datas[i - 1].DataIso!.Value)} e {Br(datas[i].DataIso!.Value)}, sem nota de férias, falta ou pausa. Quebra a cadência do protocolo."));
            }
        }

        // Só cobra EVA de fecho onde o padrão do próprio prontuário é registrá-lo.
        var comEva = p.Evolucoes.Where(e => e.EvaInicial is not null || e.EvaFinal is not null).ToList();
        if (comEva.Count >= 3)
        {
            var faltando = comEva.Where(e => e.EvaFinal is null).ToList();
            if (faltando.Count > 0)
            {
                a.Add(Achado("eva-sem-final", "alerta", "Sessão sem EVA de encerramento",
                    $"{faltando.Count} de {comEva.Count} sessões não fecham com EVA numérico ({string.Join(", ", faltando.Select(e => e.Data))}), quebrando o padrão do próprio prontuário."));
            }
        }

        foreach (var e in p.Evolucoes.Where(e => e.EvaInicial is not null))
        {
            var t = e.Texto.ToUpperInvariant();
            var v = e.EvaInicial!.Value;

            if (LeveRegex().IsMatch(t) && v >= 4)
                a.Add(Achado("eva-classificacao", "alerta", "Adjetivo da dor incoerente com o valor de EVA",
                    $"{e.Data}: dor descrita como \"leve\" com EVA {v} (4–6 é moderada, 7–10 é intensa)."));

            if (ModeradaRegex().IsMatch(t) && v >= 7)
                a.Add(Achado("eva-classificacao", "alerta", "Adjetivo da dor incoerente com o valor de EVA",
                    $"{e.Data}: dor descrita como \"moderada\" com EVA {v} (7–10 é intensa)."));

            if (IntensaRegex().IsMatch(t) && v <= 3)
                a.Add(Achado("eva-classificacao", "alerta", "Adjetivo da dor incoerente com o valor de EVA",
                    $"{e.Data}: dor descrita como intensa com EVA {v} (0–3 é leve)."));
        }

        if (p.Realizados is { } realizados && p.Evolucoes.Count > 0)
        {
            if (p.Evolucoes.Count != realizados)
                a.Add(Achado("contador-divergente", "alerta", "Contador de atendimentos divergente da evolução",
                    $"O sistema informa {realizados} atendimentos realizados, mas há {p.Evolucoes.Count} registros de evolução."));

            if (p.EsteAtendimento is { } este && p.Previstos is { } previstos && este > previstos)
                a.Add(Achado("contador-divergente", "alerta", "Contador de atendimentos divergente da evolução",
                    $"\"Este atendimento: {este}\" excede o total previsto do plano ({previstos})."));
        }

        if (alta is not null)
        {
            if (p.Cbdf.Count == 0)
            {
                a.Add(Achado("cbdf-desatualizado", "alerta", "CBDF não revisado no encerramento",
                    "Alta concedida sem nenhuma classificação CBDF registrada."));
            }
            else if (NegouRegex().IsMatch(alta.Texto) && p.Cbdf.Any(c => CondicaoRegex().IsMatch(c)))
            {
                var rotulo = p.Cbdf[0].Split(" - ").Skip(1).FirstOrDefault() ?? p.Cbdf[0];
                a.Add(Achado("cbdf-desatualizado", "alerta", "CBDF não revisado no encerramento",
                    $"A evolução de alta declara quadro negativo, mas o CBDF segue registrado como \"{rotulo}\". A classificação não foi revisada no encerramento."));
            }
        }

        if (p.Evolucoes.Count >= 3 && string.IsNullOrWhiteSpace(p.Prognostico))
        {
            a.Add(Achado("prognostico-ausente", "alerta", "Prognóstico não registrado",
                "Tratamento em curso sem prognóstico registrado na aba correspondente."));
        }

        // Sessão de alta: cruza a data da evolução com o atendimento daquele mesmo dia.
        var ultima = p.Evolucoes.Count > 0 ? p.Evolucoes[^1] : null;
        if (ultima is not null && ultima.DataIso is not null && AltaRegex().IsMatch(ultima.Texto))
        {
            var sessao = p.Atendimentos.FirstOrDefault(s =>
                s.Inicio is not null && s.Inicio.StartsWith(Br(ultima.DataIso.Value), StringComparison.Ordinal));

            if (sessao?.DuracaoMin is > 0 and < 25)
            {
                a.Add(Achado("alta-sessao-curta", "alerta", "Sessão de alta mais curta que a média do prontuário",
                    $"A sessão de alta (#{sessao.Id}, {ultima.Data}) durou {sessao.DuracaoMin} min. Reavaliação, aplicação do questionário de 24 itens e orientações de encerramento não cabem nesse tempo."));
            }
        }

        if (p.Questionario?.EscoreInicial is >= 18)
        {
            var iniciais = p.Evolucoes.Take(4).Where(e => e.EvaInicial is not null).Select(e => e.EvaInicial!.Value).ToList();
            if (iniciais.Count > 0 && iniciais.Max() <= 6)
            {
                a.Add(Achado("questionario-contradiz-evolucao", "alerta", "Escore inicial de incapacidade contradiz a evolução",
                    $"Escore inicial {p.Questionario.EscoreInicial}/24 indica incapacidade grave, mas as primeiras sessões registram EVA máximo de {iniciais.Max()} e paciente em atividade. Sugere preenchimento em bloco, não item a item."));
            }
        }

        // ── Info ────────────────────────────────────────────────────────────────

        var mMeses = MesesRegex().Match(p.Plano);
        if (mMeses.Success && p.PrimeiraIso is { } inicio && p.Evolucoes.Count > 0 && p.Evolucoes[^1].DataIso is { } fim)
        {
            var meses = int.Parse(mMeses.Groups[1].Value, CultureInfo.InvariantCulture);
            var dias = fim.DayNumber - inicio.DayNumber;
            if (dias > meses * 30 + 7)
            {
                a.Add(Achado("protocolo-estourado", "info", "Protocolo encerrado fora do prazo previsto",
                    $"Plano de {meses} meses iniciado em {p.PrimeiraConsulta} e encerrado {dias} dias depois ({dias - meses * 30} dias além do previsto)."));
            }
        }

        return a;
    }

    [GeneratedRegex(@"\bALTA\b|RECEBE ALTA|CONCEDIDA ALTA|ALTA FISIOTERAP", RegexOptions.IgnoreCase)]
    private static partial Regex AltaRegex();
    [GeneratedRegex(@"LAS[ÈE]GUE|SLUMP|BRAGARD|VALSALVA|REFLEXO|PATELAR|AQUILEU|MIOT[ÓO]M|DERMAT[ÓO]M|SENSIBILIDADE|FOR[ÇC]A MUSCULAR|GRAU\s*[IV]+|TRENDELEMBURG|THOMAS|FABER|SLR", RegexOptions.IgnoreCase)]
    private static partial Regex TestesRegex();
    [GeneratedRegex(@"TESTE", RegexOptions.IgnoreCase)]
    private static partial Regex CitaTesteRegex();
    [GeneratedRegex(@"F[ÉE]RIAS|FALTA|VIAGEM|AFASTAD|ATESTADO|INTERNA|RETORNO AP[ÓO]S|AUS[ÊE]NCIA", RegexOptions.IgnoreCase)]
    private static partial Regex JustificaRegex();
    [GeneratedRegex(@"\bLEVE\b")]
    private static partial Regex LeveRegex();
    [GeneratedRegex(@"\bMODERAD")]
    private static partial Regex ModeradaRegex();
    [GeneratedRegex(@"\bINTENS|\bFORTE|\bSEVER")]
    private static partial Regex IntensaRegex();
    [GeneratedRegex(@"NEGATIV|SEM (H[ÉE]RNIA|CI[ÁA]TICA|COMPRESS)", RegexOptions.IgnoreCase)]
    private static partial Regex NegouRegex();
    [GeneratedRegex(@"H[ÉE]RNIA|CI[ÁA]TICA", RegexOptions.IgnoreCase)]
    private static partial Regex CondicaoRegex();
    [GeneratedRegex(@"(\d+)\s*MES", RegexOptions.IgnoreCase)]
    private static partial Regex MesesRegex();
}
