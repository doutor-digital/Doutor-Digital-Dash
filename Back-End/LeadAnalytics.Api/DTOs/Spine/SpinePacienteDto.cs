namespace LeadAnalytics.Api.DTOs.Spine;

/// <summary>
/// Resposta ao clicar num horário do calendário. A agenda só tem o NOME do
/// paciente, então resolvemos nome → ficha por trás:
///  • 1 correspondência exata  → <see cref="Detalhe"/> preenchido, sem candidatos;
///  • &gt;1 (cadastro duplicado) → <see cref="Detalhe"/> null e <see cref="Candidatos"/>
///    com as opções para o usuário escolher;
///  • 0 → ambos vazios (paciente não encontrado no cadastro).
/// </summary>
public record SpinePacienteResolucaoDto(
    string NomeBuscado,
    SpinePacienteDto? Detalhe,
    IReadOnlyList<SpinePacienteCandidatoDto> Candidatos);

public record SpinePacienteCandidatoDto(
    long IdClient,
    string Nome,
    string? Whatsapp,
    string? Cidade,
    string? Uf,
    string? Origem);

/// <summary>Ficha completa do paciente + histórico da agenda dele.</summary>
public record SpinePacienteDto(
    long IdClient,
    string Nome,
    string? Origem,
    string? Status,
    DateOnly? Nascimento,
    int? Idade,
    string? Sexo,
    string? Telefone,
    string? Email,
    string? Endereco,
    string? Cidade,
    string? Uf,
    int TotalAtendimentos,
    int TotalFaltas,
    DateTime? PrimeiroAtendimento,
    DateTime? UltimoAtendimento,
    IReadOnlyList<SpinePacienteHistoricoDto> Historico);

/// <summary>Uma linha do histórico, já em horário local e ordenada do mais recente.</summary>
public record SpinePacienteHistoricoDto(
    long IdSchedule,
    DateTime QuandoLocal,
    string? Categoria,
    string? Profissional,
    int IdStatus,
    string? Situacao,
    string Grupo);
