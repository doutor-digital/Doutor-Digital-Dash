using LeadAnalytics.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Spine;

/// <summary>
/// Leva a situação da consulta do sistema da franquia para o cartão do lead na Kommo.
///
/// POR QUE ISTO EXISTE
/// -------------------
/// Quem marca a consulta é a Kommo; quem sabe se o paciente apareceu é a clínica. Sem
/// esse caminho de volta, o cartão fica parado em "agendado" para sempre — inclusive
/// para quem faltou —, e o SDR não tem como saber que precisa resgatar.
///
/// NOME É A ÚNICA CHAVE, E ISSO IMPÕE UMA REGRA
/// --------------------------------------------
/// A API da franquia não expõe CPF, e nenhum dos leads de Imperatriz tem telefone
/// gravado. Sobra o nome. Nome se repete: quando dois leads da Kommo têm o mesmo nome
/// do paciente da agenda, esta rotina NÃO escreve em nenhum. Marcar falta no cartão de
/// um paciente que compareceu é pior do que não marcar em ninguém — o SDR ligaria para
/// cobrar presença de quem esteve lá.
///
/// SÓ O DESFECHO VOLTA
/// -------------------
/// Consulta ainda "agendada" ou "confirmada" não é notícia: a Kommo já sabe disso, foi
/// ela quem marcou. Só atravessa o que a Kommo não tem como saber sozinha — atendeu,
/// não compareceu, remarcou, desmarcou.
/// </summary>
public class ConsultaSituacaoSyncService(
    AppDbContext db,
    SpineAgendaService agenda,
    KommoApiClient kommo,
    ProtectedTokenService protector,
    ILogger<ConsultaSituacaoSyncService> logger)
{
    // Campos criados no grupo COMERCIAL do cartão da ITZ (leads_97011783632936).
    private const long CampoSituacao = 2444735;   // select · SPINE_SCHEDULE_STATUS
    private const long CampoCategoria = 2444737;  // select · SPINE_SCHEDULE_CATEGORY

    internal static long? EnumSituacao(int idStatus) => idStatus switch
    {
        SpineApiClient.ScheduleStatus.Agendado => 1840173,
        SpineApiClient.ScheduleStatus.Confirmado => 1840175,
        SpineApiClient.ScheduleStatus.Atendido => 1840177,
        SpineApiClient.ScheduleStatus.NaoCompareceu => 1840179,
        SpineApiClient.ScheduleStatus.Remarcado => 1840181,
        SpineApiClient.ScheduleStatus.Desmarcado => 1840183,
        _ => null,
    };

    internal static long? EnumCategoria(int idCategoria) => idCategoria switch
    {
        SpineApiClient.ScheduleCategory.Avaliacao => 1840185,
        SpineApiClient.ScheduleCategory.Sessao => 1840187,
        SpineApiClient.ScheduleCategory.Retorno => 1840189,
        SpineApiClient.ScheduleCategory.RetornoComExames => 1840191,
        SpineApiClient.ScheduleCategory.RetornoAposTratamento => 1840193,
        _ => null,
    };

    /// <summary>Situações que a Kommo não descobre sozinha.</summary>
    internal static bool EhDesfecho(int idStatus) =>
        idStatus is SpineApiClient.ScheduleStatus.Atendido
                 or SpineApiClient.ScheduleStatus.NaoCompareceu
                 or SpineApiClient.ScheduleStatus.Remarcado
                 or SpineApiClient.ScheduleStatus.Desmarcado;

    /// <summary>
    /// Normaliza o nome para comparar: a agenda escreve "MARIA DA SILVA" e a Kommo
    /// "Maria da Silva  ". Acento fica — nomes diferentes por acento são pessoas
    /// diferentes com mais frequência do que são a mesma pessoa mal digitada.
    /// </summary>
    internal static string ChaveNome(string? nome) =>
        System.Text.RegularExpressions.Regex
            .Replace((nome ?? string.Empty).Trim(), @"\s+", " ")
            .ToLowerInvariant();

    public async Task<ConsultaSyncResultado> SincronizarAsync(
        int unitId, DateOnly de, DateOnly ate, bool simular, CancellationToken ct)
    {
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct)
            ?? throw new InvalidOperationException("Unidade não encontrada.");

        var token = protector.Unprotect(unit.KommoAccessToken);
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(unit.KommoSubdomain))
            throw new InvalidOperationException("Unidade sem token da Kommo configurado.");

        var dados = await agenda.GetAsync(unitId, de, ate, ct)
            ?? throw new InvalidOperationException("Unidade sem autorização no sistema da franquia.");

        // Nome em branco vira chave vazia, e chave vazia casaria com qualquer lead
        // sem nome na Kommo — escreveria falta num cartão aleatório.
        var desfechos = dados.Itens
            .Where(i => EhDesfecho(i.IdStatus) && ChaveNome(i.Paciente).Length > 0)
            .ToList();
        if (desfechos.Count == 0)
            return new ConsultaSyncResultado(dados.Total, 0, 0, 0, 0, []);

        // Um lead por nome. Nome repetido vira ambiguidade e sai da jogada — por isso
        // agrupamos em vez de pegar o primeiro.
        var nomes = desfechos.Select(i => ChaveNome(i.Paciente)).ToHashSet();
        var candidatos = await db.Leads.AsNoTracking()
            .Where(l => l.TenantId == unit.ClinicId && l.UnitId == unitId)
            .Select(l => new { l.Id, l.ExternalId, l.Name })
            .ToListAsync(ct);

        var porNome = candidatos
            .GroupBy(l => ChaveNome(l.Name))
            .Where(g => g.Key.Length > 0 && nomes.Contains(g.Key))
            .ToDictionary(g => g.Key, g => g.Select(x => x.ExternalId).Distinct().ToList());

        int escritos = 0, semLead = 0, ambiguos = 0, semMapa = 0;
        var problemas = new List<string>();

        // Uma consulta por paciente: se alguém tem avaliação e retorno na janela, o
        // cartão fica com a mais recente — é a que descreve onde o paciente está.
        foreach (var grupo in desfechos.GroupBy(i => ChaveNome(i.Paciente)))
        {
            var item = grupo.OrderByDescending(i => i.Inicio).First();

            if (!porNome.TryGetValue(grupo.Key, out var ids))
            {
                semLead++;
                continue;
            }
            if (ids.Count > 1)
            {
                ambiguos++;
                problemas.Add($"{item.Paciente}: {ids.Count} leads com este nome — não escrevi em nenhum");
                continue;
            }

            var situacao = EnumSituacao(item.IdStatus);
            if (situacao is null)
            {
                semMapa++;
                problemas.Add($"{item.Paciente}: situação {item.IdStatus} ({item.Status}) sem correspondência");
                continue;
            }

            if (simular) { escritos++; continue; }

            var campos = new List<KommoCustomFieldPatch>
            {
                new(CampoSituacao, "select", null, situacao),
            };
            if (EnumCategoria(item.IdCategoria) is long cat)
                campos.Add(new(CampoCategoria, "select", null, cat));

            try
            {
                await kommo.PatchLeadCustomFieldsAsync(unit.KommoSubdomain!, token!, ids[0], campos, ct);
                escritos++;
            }
            catch (HttpRequestException ex)
            {
                problemas.Add($"{item.Paciente}: Kommo recusou — {ex.Message}");
            }
        }

        logger.LogInformation(
            "🗓️ Consulta franquia → Kommo | unidade={Unit} janela={De}..{Ate} desfechos={Desf} escritos={Esc} " +
            "sem lead={SemLead} ambíguos={Amb} sem mapa={SemMapa}{Simulacao}",
            unitId, de, ate, desfechos.Count, escritos, semLead, ambiguos, semMapa,
            simular ? " (simulação)" : "");

        return new ConsultaSyncResultado(
            dados.Total, desfechos.Count, escritos, semLead, ambiguos, problemas);
    }
}

/// <param name="NaAgenda">Horários na janela, de qualquer situação.</param>
/// <param name="ComDesfecho">Quantos já têm desfecho (atendeu, faltou, remarcou, desmarcou).</param>
/// <param name="Escritos">Cartões atualizados na Kommo.</param>
/// <param name="SemLeadNaKommo">Paciente da agenda sem lead de mesmo nome — costuma ser cadastro antigo.</param>
/// <param name="Ambiguos">Nome repetido na Kommo: deixados de fora de propósito.</param>
public record ConsultaSyncResultado(
    int NaAgenda,
    int ComDesfecho,
    int Escritos,
    int SemLeadNaKommo,
    int Ambiguos,
    IReadOnlyList<string> Problemas);
