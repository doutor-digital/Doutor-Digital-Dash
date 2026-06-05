using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Endpoints da I.A. integrada ao painel (GPT-4o-mini).
///
/// - <c>GET  /api/ai/settings</c>           → diz se a chave está configurada (sem expor o valor).
/// - <c>PUT  /api/ai/settings</c>           → grava a chave (cifrada via DataProtection).
/// - <c>POST /api/ai/settings/test</c>      → ping na OpenAI pra validar a chave.
/// - <c>DELETE /api/ai/settings</c>         → remove a chave.
/// - <c>POST /api/ai/analyze</c>            → análise profunda da unidade no período.
/// - <c>POST /api/ai/transcribe</c>         → multipart áudio → texto (Whisper).
/// </summary>
[ApiController]
[Authorize]
[Route("api/ai")]
public class AiController(
    AiKeyStorage keys,
    OpenAiClient openAi,
    AiAnalyticsService analytics,
    AiToolRegistry tools,
    TenantUnitGuard tenantGuard,
    ICurrentUser currentUser,
    ILogger<AiController> logger) : ControllerBase
{
    // ─── SETTINGS ───────────────────────────────────────────────────────────

    public record SettingsDto(bool HasKey);
    public record SetKeyRequest(string ApiKey);
    public record PingResponse(bool Ok, string? Error);

    /// <summary>
    /// Resolve o tenantId pra operações da I.A. Aceita um <paramref name="explicitTenantId"/>
    /// (query string) quando o caller é super_admin sem tenant_id no JWT — esse é o caso
    /// típico de quem administra o painel. Senão, exige o tenant do JWT.
    /// </summary>
    private int? ResolveAiTenantId(int? explicitTenantId)
    {
        if (currentUser.TenantId is int t) return t;
        if (currentUser.IsSuperAdmin && explicitTenantId is int et && et > 0) return et;
        return null;
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings([FromQuery] int? tenantId, CancellationToken ct)
    {
        if (ResolveAiTenantId(tenantId) is not int t) return Forbid();
        var has = await keys.HasKeyAsync(t, ct);
        return Ok(new SettingsDto(has));
    }

    [HttpPut("settings")]
    public async Task<IActionResult> SetKey([FromBody] SetKeyRequest body, [FromQuery] int? tenantId, CancellationToken ct)
    {
        if (ResolveAiTenantId(tenantId) is not int t) return Forbid();
        if (string.IsNullOrWhiteSpace(body.ApiKey) || body.ApiKey.Length < 20)
            return BadRequest(new { error = "API key inválida (mínimo 20 chars)." });

        await keys.SetAsync(t, body.ApiKey.Trim(), ct);
        logger.LogInformation("[ai] tenant={Tenant} key gravada", t);
        return Ok(new SettingsDto(true));
    }

    [HttpDelete("settings")]
    public async Task<IActionResult> DeleteKey([FromQuery] int? tenantId, CancellationToken ct)
    {
        if (ResolveAiTenantId(tenantId) is not int t) return Forbid();
        await keys.DeleteAsync(t, ct);
        return Ok(new SettingsDto(false));
    }

    [HttpPost("settings/test")]
    public async Task<IActionResult> Ping([FromQuery] int? tenantId, CancellationToken ct)
    {
        if (ResolveAiTenantId(tenantId) is not int t) return Forbid();
        var key = await keys.GetAsync(t, ct);
        if (string.IsNullOrWhiteSpace(key))
            return Ok(new PingResponse(false, "Chave não configurada."));
        try
        {
            var ok = await openAi.PingAsync(key, ct);
            return Ok(new PingResponse(ok, ok ? null : "OpenAI rejeitou a chave."));
        }
        catch (Exception ex)
        {
            return Ok(new PingResponse(false, ex.Message));
        }
    }

    // ─── ANALYZE ────────────────────────────────────────────────────────────

    public record AnalyzeRequest(int UnitId, DateTime? DateFrom, DateTime? DateTo);
    public record AnalyzeResponse(string Markdown, int Tokens, double DurationSec);

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] AnalyzeRequest body, CancellationToken ct)
    {
        var (err, tenantId) = await tenantGuard.ResolveTenantAsync(body.UnitId, ct);
        if (err is not null) return err;
        if (tenantId is null) return Forbid();

        var to = (body.DateTo ?? DateTime.UtcNow).Date.AddDays(1).AddTicks(-1);
        var from = (body.DateFrom ?? to.AddDays(-30)).Date;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var md = await analytics.AnalyzeUnitAsync(tenantId.Value, body.UnitId, from, to, ct);
            sw.Stop();
            return Ok(new AnalyzeResponse(md, md.Length / 4, sw.Elapsed.TotalSeconds));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "[ai] erro na OpenAI");
            return StatusCode(502, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            // Catch-all pra superficiar a real do erro pro front em vez de 500 cego.
            logger.LogError(ex, "[ai-analyze] erro inesperado tenant={Tenant} unit={Unit}", tenantId.Value, body.UnitId);
            return StatusCode(500, new
            {
                error = $"{ex.GetType().Name}: {ex.Message}",
                inner = ex.InnerException?.Message,
                where = ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim(),
            });
        }
    }

    // ─── CHAT ───────────────────────────────────────────────────────────────

    public record ChatMessageDto(string Role, string Content);
    public record ChatRequest(
        List<ChatMessageDto> Messages,
        int? UnitId,
        DateTime? DateFrom,
        DateTime? DateTo,
        string? CurrentPath);
    public record ChatResponse(string Content);

    private const string ChatSystemPromptBase =
        "Você é a assistente operacional do painel Doutor Digital, uma rede de clínicas. " +
        "Ajuda principalmente as SDRs e gestores das unidades a entenderem os números e " +
        "navegarem o sistema. Responde em pt-BR, direto, sem rodeios. Quando o usuário " +
        "perguntar sobre dados, use APENAS os números abaixo (ou diga 'não tenho esse dado'); " +
        "nunca invente. Quando perguntarem 'como faço X no painel', explica o caminho " +
        "(ex.: 'Sidebar → Leads → Recuperação'). Se a pergunta não tiver nada a ver com " +
        "o painel ou clínica, redirecione gentilmente.";

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest body, [FromQuery(Name = "tenantId")] int? explicitTenantId, CancellationToken ct)
    {
        if (ResolveAiTenantId(explicitTenantId) is not int tenantId) return Forbid();
        var key = await keys.GetAsync(tenantId, ct);
        if (string.IsNullOrWhiteSpace(key))
            return BadRequest(new { error = "Chave OpenAI não configurada." });

        if (body.Messages is null || body.Messages.Count == 0)
            return BadRequest(new { error = "Mensagens vazias." });

        // Contexto enxuto: se tem unidade + período, anexa um resumo curto.
        var contextFacts = await BuildChatContextAsync(tenantId, body.UnitId, body.DateFrom, body.DateTo, body.CurrentPath, ct);
        var systemPrompt = ChatSystemPromptBase + "\n\n" + contextFacts;

        var toolsCalled = new List<string>();
        var toolDefs = tools.All().Select(t => (t.Name, t.Description, t.Schema)).ToList();

        try
        {
            var content = await openAi.ChatWithToolsAsync(
                key,
                systemPrompt,
                body.Messages.Select(m => (m.Role, m.Content)),
                toolDefs,
                async (name, args, c) =>
                {
                    toolsCalled.Add(name);
                    logger.LogInformation("[ai-chat] tool={Tool}", name);
                    return await tools.ExecuteAsync(name, args, tenantId, body.UnitId, c);
                },
                ct);
            return Ok(new { content, toolsCalled });
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "[ai] erro no chat");
            return StatusCode(502, new { error = ex.Message });
        }
    }

    private async Task<string> BuildChatContextAsync(
        int tenantId, int? unitId, DateTime? dateFrom, DateTime? dateTo, string? currentPath, CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## Contexto atual da sessão");
        if (!string.IsNullOrWhiteSpace(currentPath))
            sb.AppendLine($"- Página aberta: `{currentPath}`");

        if (unitId is int uid)
        {
            var to = (dateTo ?? DateTime.UtcNow).Date.AddDays(1).AddTicks(-1);
            var from = (dateFrom ?? to.AddDays(-30)).Date;
            sb.AppendLine($"- Unidade selecionada: id {uid}");
            sb.AppendLine($"- Período: {from:dd/MM/yyyy} → {to:dd/MM/yyyy}");

            try
            {
                var facts = await analytics.BuildChatFactsAsync(tenantId, uid, from, to, ct);
                if (!string.IsNullOrWhiteSpace(facts))
                {
                    sb.AppendLine();
                    sb.AppendLine(facts);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[ai-chat] falha ao montar quick facts (unit {Unit})", uid);
            }
        }
        else
        {
            sb.AppendLine("- Nenhuma unidade selecionada — peça pro usuário escolher uma se a pergunta depender disso.");
        }

        sb.AppendLine();
        sb.AppendLine("## Rotas do painel (caso o usuário pergunte como navegar)");
        sb.AppendLine("- `/` Dashboard principal");
        sb.AppendLine("- `/campos-customizados` Análise dos campos da Kommo (Sexo×Desfecho, Origem, Tratamento Indicado, Motivo Não Agendamento, Profissão…)");
        sb.AppendLine("- `/conversas` WhatsApp/atendimento (1ª resposta, sem-resposta, heatmap por hora, por agente)");
        sb.AppendLine("- `/leads` Lista completa, `/recent-leads` Recentes, `/recuperacao` Leads que precisam ser puxados");
        sb.AppendLine("- `/funnel` Funil, `/conversao` Conversão, `/sources` Origens");
        sb.AppendLine("- `/sdr/cadastro-geral` Cadastro de leads pelas SDRs");
        sb.AppendLine("- `/sdr/agenda` Agenda das consultas, `/sdr/tarefas` Tarefas pendentes");
        sb.AppendLine("- `/sdr/metas` Metas das secretárias");
        sb.AppendLine("- `/units` Unidades + sincronização com Kommo");
        sb.AppendLine("- `/ia-analytics` Análise profunda da unidade gerada por IA");

        return sb.ToString();
    }

    // ─── TRANSCRIBE ─────────────────────────────────────────────────────────

    [HttpPost("transcribe")]
    [RequestSizeLimit(25 * 1024 * 1024)] // 25MB — limite do endpoint
    public async Task<IActionResult> Transcribe([FromForm] IFormFile? audio, [FromQuery(Name = "tenantId")] int? explicitTenantId, CancellationToken ct)
    {
        if (audio is null || audio.Length == 0) return BadRequest(new { error = "Áudio vazio." });
        if (ResolveAiTenantId(explicitTenantId) is not int tenantId) return Forbid();
        var key = await keys.GetAsync(tenantId, ct);
        if (string.IsNullOrWhiteSpace(key))
            return BadRequest(new { error = "Chave da OpenAI não configurada." });

        try
        {
            await using var stream = audio.OpenReadStream();
            var text = await openAi.TranscribeAsync(key, stream, audio.FileName, ct);
            return Ok(new { text });
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "[ai] erro na transcrição");
            return StatusCode(502, new { error = ex.Message });
        }
    }
}
