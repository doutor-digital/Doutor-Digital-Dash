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
/// NOME ACHA, TELEFONE CONFIRMA
/// ----------------------------
/// A API da franquia não expõe CPF, então o nome é o que aproxima os dois lados. Mas
/// nome sozinho erra: homônimo existe, e marcar falta no cartão de quem compareceu faz
/// o SDR ligar cobrando presença de quem esteve na clínica.
///
/// O telefone resolve, e ele existe dos dois lados — só não estava à mão. Na Kommo o
/// telefone não fica no lead: fica no CONTATO ligado a ele, que é por isso que a coluna
/// do lead aparece vazia nos 8.812 registros. Aqui buscamos o contato para confirmar.
///
/// A regra: nome bate e telefone bate → escreve. Nome bate e telefone diverge → NÃO
/// escreve, porque ou é homônimo ou o cadastro está velho, e as duas hipóteses pedem
/// olho humano. Sem telefone em algum dos lados, cai para nome único — e o relatório
/// diz quantos vieram por cada caminho, para você saber em quanto confiar.
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
    SpinePacienteService pacientes,
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

    /// <summary>
    /// Os 8 últimos dígitos do telefone. Um lado grava "+55 99 98199-1934", o outro
    /// "9998199 1934"; DDI e o nono dígito entram e saem conforme quem cadastrou.
    /// Oito dígitos é o que sobra sempre e ainda distingue pessoas.
    /// </summary>
    internal static string? ChaveTelefone(string? fone)
    {
        var digitos = new string((fone ?? string.Empty).Where(char.IsDigit).ToArray());
        return digitos.Length >= 8 ? digitos[^8..] : null;
    }

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
            return new ConsultaSyncResultado(dados.Total, 0, 0, 0, 0, 0, 0, []);

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
        int porTelefone = 0, conflitoTelefone = 0;
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
            // O telefone entra aqui: desempata nome repetido e confirma o nome único.
            var fonePaciente = await TelefoneDoPacienteAsync(unitId, item.Paciente, ct);
            var escolhidos = ids;

            if (fonePaciente is not null)
            {
                var fonesPorLead = await TelefonesDosLeadsAsync(unit.KommoSubdomain!, token!, ids, ct);
                var batem = ids.Where(id =>
                    fonesPorLead.TryGetValue(id, out var fones) && fones.Contains(fonePaciente)).ToList();

                // Nenhum bate: ou é homônimo, ou o telefone está desatualizado de um
                // dos lados. Escrever assim mesmo é apostar — e a aposta erra no
                // cartão de um paciente.
                if (batem.Count == 0)
                {
                    conflitoTelefone++;
                    problemas.Add($"{item.Paciente}: nome confere mas telefone não bate em nenhum dos {ids.Count} leads");
                    continue;
                }
                escolhidos = batem;
                if (batem.Count == 1) porTelefone++;
            }

            if (escolhidos.Count > 1)
            {
                ambiguos++;
                problemas.Add(fonePaciente is null
                    ? $"{item.Paciente}: {ids.Count} leads com este nome e sem telefone na franquia para desempatar"
                    : $"{item.Paciente}: {escolhidos.Count} leads com o mesmo nome E o mesmo telefone — provável duplicata");
                continue;
            }
            ids = escolhidos;

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
            "confirmados por telefone={Tel} conflito de telefone={Conf} sem lead={SemLead} ambíguos={Amb} " +
            "sem mapa={SemMapa}{Simulacao}",
            unitId, de, ate, desfechos.Count, escritos, porTelefone, conflitoTelefone,
            semLead, ambiguos, semMapa, simular ? " (simulação)" : "");

        return new ConsultaSyncResultado(
            dados.Total, desfechos.Count, escritos, porTelefone, conflitoTelefone,
            semLead, ambiguos, problemas);
    }

    /// <summary>
    /// Telefone do paciente no sistema da franquia. Só devolve quando há UM cadastro
    /// com aquele nome exato — dois cadastros homônimos dariam dois telefones, e
    /// escolher um deles é o palpite que este método existe para evitar.
    /// </summary>
    private async Task<string?> TelefoneDoPacienteAsync(int unitId, string nome, CancellationToken ct)
    {
        try
        {
            var r = await pacientes.PorNomeAsync(unitId, nome, ct);
            var fone = r?.Detalhe?.Telefone;
            return ChaveTelefone(fone);
        }
        catch (Exception ex)
        {
            // Sem telefone o casamento continua por nome único — degrada, não quebra.
            logger.LogDebug(ex, "Não consegui o telefone de {Nome} na franquia", nome);
            return null;
        }
    }

    /// <summary>Telefones dos contatos ligados a cada lead da Kommo (o lead não guarda telefone).</summary>
    private async Task<Dictionary<long, HashSet<string>>> TelefonesDosLeadsAsync(
        string subdominio, string token, List<int> leadIds, CancellationToken ct)
    {
        var saida = new Dictionary<long, HashSet<string>>();
        try
        {
            var pagina = await kommo.GetLeadsByIdsAsync(subdominio, token, leadIds.Select(i => (long)i), ct);
            var leads = pagina?.Embedded?.Leads ?? [];

            var contatoDoLead = leads.ToDictionary(
                l => l.Id,
                l => (l.Embedded?.Contacts ?? []).Select(c => c.Id).ToList());

            var todosContatos = contatoDoLead.Values.SelectMany(x => x).Distinct().ToList();
            if (todosContatos.Count == 0) return saida;

            var cPage = await kommo.GetContactsByIdsAsync(subdominio, token, todosContatos, ct);
            var fonePorContato = new Dictionary<long, HashSet<string>>();
            foreach (var c in cPage?.Embedded?.Contacts ?? [])
            {
                var fones = (c.CustomFieldsValues ?? [])
                    .Where(f => f.FieldCode == "PHONE")
                    .SelectMany(f => f.Values ?? [])
                    .Select(v => ChaveTelefone(v.Value?.ToString()))
                    .Where(x => x is not null)
                    .Select(x => x!)
                    .ToHashSet();
                if (fones.Count > 0) fonePorContato[c.Id] = fones;
            }

            foreach (var (leadId, contatos) in contatoDoLead)
            {
                var fones = contatos
                    .Where(fonePorContato.ContainsKey)
                    .SelectMany(id => fonePorContato[id])
                    .ToHashSet();
                if (fones.Count > 0) saida[leadId] = fones;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao ler telefones dos leads na Kommo");
        }
        return saida;
    }
}

/// <param name="NaAgenda">Horários na janela, de qualquer situação.</param>
/// <param name="ComDesfecho">Quantos já têm desfecho (atendeu, faltou, remarcou, desmarcou).</param>
/// <param name="Escritos">Cartões atualizados na Kommo.</param>
/// <param name="SemLeadNaKommo">Paciente da agenda sem lead de mesmo nome — costuma ser cadastro antigo.</param>
/// <param name="Ambiguos">Nome repetido na Kommo: deixados de fora de propósito.</param>
/// <param name="ConfirmadosPorTelefone">Casaram nome E telefone — o casamento forte.</param>
/// <param name="ConflitoDeTelefone">Nome bateu, telefone não: deixados de fora de propósito.</param>
public record ConsultaSyncResultado(
    int NaAgenda,
    int ComDesfecho,
    int Escritos,
    int ConfirmadosPorTelefone,
    int ConflitoDeTelefone,
    int SemLeadNaKommo,
    int Ambiguos,
    IReadOnlyList<string> Problemas);
