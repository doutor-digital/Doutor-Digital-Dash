using LeadAnalytics.Api.DTOs.Spine;
using LeadAnalytics.Api.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace LeadAnalytics.Api.Service.Spine;

/// <summary>
/// Resolve o clique num horário do calendário na ficha completa do paciente.
///
/// A agenda (schedules/search) só tem o NOME como texto, sem idClient. Então:
/// busca clientes por esse nome, filtra a correspondência EXATA e, se houver só
/// uma, abre a ficha (clients/{id}). Cadastro duplicado (nome igual em dois ids)
/// vira lista de candidatos para o usuário escolher — medido em 1 caso a cada ~39
/// pacientes na Imperatriz.
/// </summary>
public class SpinePacienteService(
    SpineApiClient client,
    SpineTokenStore tokens,
    IMemoryCache cache,
    IOptions<SpineOptions> options,
    ILogger<SpinePacienteService> logger)
{
    private readonly SpineApiClient _client = client;
    private readonly SpineTokenStore _tokens = tokens;
    private readonly IMemoryCache _cache = cache;
    private readonly SpineOptions _options = options.Value;
    private readonly ILogger<SpinePacienteService> _logger = logger;

    /// <summary>Resolve por NOME (o que o calendário tem em mãos).</summary>
    public async Task<SpinePacienteResolucaoDto?> PorNomeAsync(
        int unitId, string nome, CancellationToken ct = default)
    {
        var token = await _tokens.GetTokenAsync(unitId, ct);
        if (token is null) return null;

        nome = nome.Trim();
        var achados = await _client.SearchClientsByNameAsync(token, nome, ct);

        // A busca do Spine é "contém" (LIKE): filtramos para o nome idêntico.
        var exatos = achados
            .Where(c => string.Equals((c.Name ?? string.Empty).Trim(), nome, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exatos.Count == 1)
        {
            var detalhe = await MontarFichaAsync(token, exatos[0].IdClient, ct);
            return new SpinePacienteResolucaoDto(nome, detalhe, []);
        }

        // Colisão (cadastro duplicado) ou nenhum: devolve candidatos para escolha.
        var candidatos = exatos
            .Select(c => new SpinePacienteCandidatoDto(
                c.IdClient, (c.Name ?? "").Trim(), c.Whatsapp, c.AddressCity, c.AddressUf, c.SourceName))
            .ToList();
        return new SpinePacienteResolucaoDto(nome, null, candidatos);
    }

    /// <summary>Resolve por ID (quando o usuário escolheu um candidato da colisão).</summary>
    public async Task<SpinePacienteDto?> PorIdAsync(int unitId, long idClient, CancellationToken ct = default)
    {
        var token = await _tokens.GetTokenAsync(unitId, ct);
        if (token is null) return null;
        return await MontarFichaAsync(token, idClient, ct);
    }

    private async Task<SpinePacienteDto?> MontarFichaAsync(string token, long idClient, CancellationToken ct)
    {
        var cacheKey = $"spine:paciente:{idClient}";
        if (_cache.TryGetValue<SpinePacienteDto>(cacheKey, out var hit) && hit is not null)
            return hit;

        var c = await _client.GetClientAsync(token, idClient, ct);
        if (c is null) return null;

        var hist = (c.Schedules ?? [])
            .Where(s => s.DateAttendance.HasValue)
            .Select(s => new SpinePacienteHistoricoDto(
                s.IdSchedule,
                ParaLocal(s.DateAttendance!.Value),
                s.Category,
                (s.PhysicalTherapist ?? "").Trim(),
                s.IdStatus,
                s.StatusName,
                Grupo(s.IdStatus)))
            .OrderByDescending(h => h.QuandoLocal)
            .ToList();

        var atendidos = hist.Where(h => h.IdStatus == SpineApiClient.ScheduleStatus.Atendido).ToList();

        var dto = new SpinePacienteDto(
            IdClient: c.IdClient,
            Nome: (c.Name ?? "").Trim(),
            Origem: c.Source,
            Status: c.Status,
            Nascimento: c.Birthdate is { } b ? DateOnly.FromDateTime(b) : null,
            Idade: Idade(c.Birthdate),
            Sexo: Sexo(c.Gender),
            Telefone: string.IsNullOrWhiteSpace(c.Whatsapp) ? null : c.Whatsapp,
            Email: string.IsNullOrWhiteSpace(c.Email) ? null : c.Email,
            Endereco: MontarEndereco(c),
            Cidade: string.IsNullOrWhiteSpace(c.AddressCity) ? null : c.AddressCity,
            Uf: string.IsNullOrWhiteSpace(c.AddressUf) ? null : c.AddressUf,
            TotalAtendimentos: atendidos.Count,
            TotalFaltas: hist.Count(h => h.IdStatus == SpineApiClient.ScheduleStatus.NaoCompareceu),
            PrimeiroAtendimento: atendidos.Count > 0 ? atendidos.Min(h => h.QuandoLocal) : null,
            UltimoAtendimento: atendidos.Count > 0 ? atendidos.Max(h => h.QuandoLocal) : null,
            Historico: hist);

        _cache.Set(cacheKey, dto, TimeSpan.FromSeconds(_options.CacheSeconds));
        return dto;
    }

    private static DateTime ParaLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), SpineApiClient.BrTz);

    /// <summary>Mesma classificação de desfecho usada no card e no calendário.</summary>
    private static string Grupo(int idStatus) => idStatus switch
    {
        SpineApiClient.ScheduleStatus.Atendido => "realizado",
        SpineApiClient.ScheduleStatus.NaoCompareceu => "falta",
        SpineApiClient.ScheduleStatus.Desmarcado => "cancelado",
        SpineApiClient.ScheduleStatus.Remarcado => "cancelado",
        SpineApiClient.ScheduleStatus.Agendado => "pendente",
        SpineApiClient.ScheduleStatus.Confirmado => "pendente",
        _ => "desconhecido",
    };

    private static int? Idade(DateTime? nascimento)
    {
        if (nascimento is not { } b) return null;
        var hoje = DateTime.UtcNow;
        var idade = hoje.Year - b.Year;
        if (b.Date > hoje.AddYears(-idade)) idade--;
        return idade is >= 0 and < 130 ? idade : null;
    }

    private static string? Sexo(string? g) => g?.Trim().ToUpperInvariant() switch
    {
        "M" => "Masculino",
        "F" => "Feminino",
        _ => null,
    };

    private static string? MontarEndereco(SpineClientDetail c)
    {
        var rua = (c.Address ?? "").Trim();
        var num = (c.AddressNumber ?? "").Trim();
        if (rua.Length == 0) return null;
        return num.Length > 0 ? $"{rua}, {num}" : rua;
    }
}
