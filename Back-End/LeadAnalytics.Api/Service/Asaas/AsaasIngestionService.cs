using System.Text.Json;
using System.Text.Json.Serialization;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Asaas;

/// <summary>
/// Recebe o evento de cobrança do Asaas e escreve o estado financeiro no cartão
/// do lead na Kommo.
///
/// O LEAD VEM DO externalReference, NÃO DE HEURÍSTICA
/// --------------------------------------------------
/// Ao criar a cobrança, o id do lead da Kommo vai no <c>externalReference</c>. O
/// webhook devolve esse id e o casamento é exato. Casar por CPF ou telefone seria
/// adivinhação: em Imperatriz o CPF está quase todo vazio e nenhum dos 8.772 leads
/// tem telefone gravado. Errar aqui significa lançar a dívida de um paciente no
/// cartão de outro, então cobrança sem <c>externalReference</c> é recusada em vez
/// de "melhor esforço".
///
/// O ESTADO SUBSTITUI, NÃO ACUMULA
/// -------------------------------
/// Cada evento traz a cobrança inteira, então o cartão recebe o retrato atual. Uma
/// cobrança que vence e depois é paga sobrescreve o próprio status — o cartão diz o
/// que vale agora, e o histórico completo mora no Asaas, que é quem tem fé pública
/// sobre dinheiro.
/// </summary>
public class AsaasIngestionService(
    AppDbContext db,
    KommoApiClient kommo,
    ProtectedTokenService protector,
    ILogger<AsaasIngestionService> logger)
{
    // ─── Os 14 campos do grupo "ASAAS — Financeiro" no cartão da ITZ ─────────
    //
    // Os ids ficam explícitos porque os NOMES colidem: "⬢ Forma de pagamento" e
    // "# Nº de parcelas" existem também fora deste grupo, na parte comercial do
    // cartão. Resolver por nome escreveria no campo errado.
    private const long CampoCliente = 2442943;        // text
    private const long CampoCobranca = 2442945;       // text
    private const long CampoAssinatura = 2442947;     // text
    private const long CampoStatus = 2442949;         // select
    private const long CampoForma = 2442951;          // select
    private const long CampoParcelas = 2442953;       // numeric
    private const long CampoVencimento = 2442955;     // date
    private const long CampoPagamento = 2442957;      // date
    private const long CampoProxVencimento = 2442959; // date
    private const long CampoLinkFatura = 2442961;     // url
    private const long CampoBoletoPix = 2442963;      // url
    private const long CampoDescricao = 2442965;      // text
    private const long CampoValor = 2442967;          // monetary
    private const long CampoLiquido = 2442969;        // monetary

    /// <summary>Status do Asaas → opção da lista no cartão.</summary>
    internal static long? EnumStatus(string? s) => s?.ToUpperInvariant() switch
    {
        "PENDING" => 1837785,                 // Pendente
        "CONFIRMED" => 1837787,               // Confirmado
        "RECEIVED" => 1837789,                // Recebido
        "RECEIVED_IN_CASH" => 1837791,        // Recebido em dinheiro
        "OVERDUE" => 1837793,                 // Vencido
        "REFUNDED" or "REFUND_REQUESTED" => 1837795,  // Estornado
        "DELETED" or "CANCELED" => 1837797,   // Cancelado
        "AWAITING_RISK_ANALYSIS" => 1837799,  // Em análise (risco)
        "CHARGEBACK_REQUESTED" or "CHARGEBACK_DISPUTE" or "AWAITING_CHARGEBACK_REVERSAL" => 1837801,
        _ => null,
    };

    internal static long? EnumForma(string? b) => b?.ToUpperInvariant() switch
    {
        "BOLETO" => 1837803,
        "PIX" => 1837805,
        "CREDIT_CARD" => 1837807,
        "DEBIT_CARD" => 1837809,
        "UNDEFINED" => 1837811,
        _ => null,
    };

    public async Task<AsaasIngestResult> IngestAsync(
        AsaasWebhookPayload payload, Unit unit, CancellationToken ct)
    {
        var c = payload.Payment
            ?? throw new ArgumentException("Evento sem objeto de cobrança (payment).");

        var referencia = c.ExternalReference?.Trim();
        if (string.IsNullOrWhiteSpace(referencia) || !long.TryParse(referencia, out var leadKommoId))
            return AsaasIngestResult.SemReferencia(c.Id);

        // Confere que o lead existe do nosso lado antes de escrever na Kommo: id
        // solto vindo do Asaas pode ser de outra conta ou de um teste.
        var existe = await db.Leads.AsNoTracking().AnyAsync(
            l => l.TenantId == unit.ClinicId && l.ExternalId == (int)leadKommoId, ct);
        if (!existe)
            return AsaasIngestResult.LeadDesconhecido(c.Id, leadKommoId);

        var token = protector.Unprotect(unit.KommoAccessToken);
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(unit.KommoSubdomain))
            return AsaasIngestResult.SemTokenKommo(c.Id);

        var campos = new List<KommoCustomFieldPatch>
        {
            new(CampoCliente, "text", c.Customer, null),
            new(CampoCobranca, "text", c.Id, null),
            new(CampoAssinatura, "text", c.Subscription, null),
            new(CampoStatus, "select", null, EnumStatus(c.Status)),
            new(CampoForma, "select", null, EnumForma(c.BillingType)),
            new(CampoVencimento, "date", c.DueDate, null),
            new(CampoPagamento, "date", c.PaymentDate ?? c.ClientPaymentDate, null),
            new(CampoLinkFatura, "url", c.InvoiceUrl, null),
            new(CampoBoletoPix, "url", c.BankSlipUrl ?? c.TransactionReceiptUrl, null),
            new(CampoDescricao, "text", c.Description, null),
            new(CampoValor, "monetary", Dinheiro(c.Value), null),
            new(CampoLiquido, "monetary", Dinheiro(c.NetValue), null),
        };

        // Parcelamento só aparece quando existe: mandar "1" numa cobrança avulsa
        // faria o cartão afirmar algo que o Asaas não disse.
        if (c.InstallmentCount is int n && n > 0)
            campos.Add(new(CampoParcelas, "numeric", n.ToString(), null));

        // "Próximo vencimento" é da assinatura, não da cobrança avulsa.
        if (!string.IsNullOrWhiteSpace(c.Subscription) && !string.IsNullOrWhiteSpace(c.DueDate))
            campos.Add(new(CampoProxVencimento, "date", c.DueDate, null));

        await kommo.PatchLeadCustomFieldsAsync(
            unit.KommoSubdomain!, token!, leadKommoId, campos, ct);

        logger.LogInformation(
            "💳 Asaas → Kommo | evento={Evento} cobranca={Cobranca} lead={Lead} status={Status} valor={Valor}",
            payload.Event, c.Id, leadKommoId, c.Status, c.Value);

        return AsaasIngestResult.Ok(c.Id, leadKommoId, campos.Count);
    }

    /// <summary>
    /// A Kommo espera o monetário como número simples. Cultura invariante de
    /// propósito: formatar com vírgula aqui gravaria 1.234,50 como 1,234.50 lá.
    /// </summary>
    internal static string? Dinheiro(decimal? v) =>
        v is null ? null : v.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct AsaasIngestResult(
    bool Aceito, string Motivo, string? CobrancaId, long? LeadId, int CamposEscritos)
{
    public static AsaasIngestResult Ok(string? c, long lead, int n) =>
        new(true, "ok", c, lead, n);

    public static AsaasIngestResult SemReferencia(string? c) =>
        new(false, "cobrança sem externalReference — não dá para saber de qual lead é", c, null, 0);

    public static AsaasIngestResult LeadDesconhecido(string? c, long lead) =>
        new(false, $"lead {lead} não existe nesta unidade", c, lead, 0);

    public static AsaasIngestResult SemTokenKommo(string? c) =>
        new(false, "unidade sem token da Kommo configurado", c, null, 0);
}

// ─── Contrato do webhook do Asaas ───────────────────────────────────────────

public class AsaasWebhookPayload
{
    /// <summary>PAYMENT_CREATED, PAYMENT_RECEIVED, PAYMENT_OVERDUE, etc.</summary>
    [JsonPropertyName("event")] public string? Event { get; set; }
    [JsonPropertyName("payment")] public AsaasPaymentDto? Payment { get; set; }
}

public class AsaasPaymentDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("customer")] public string? Customer { get; set; }
    [JsonPropertyName("subscription")] public string? Subscription { get; set; }
    [JsonPropertyName("installment")] public string? Installment { get; set; }
    [JsonPropertyName("installmentCount")] public int? InstallmentCount { get; set; }

    /// <summary>Id do lead na Kommo. É o que liga a cobrança ao cartão.</summary>
    [JsonPropertyName("externalReference")] public string? ExternalReference { get; set; }

    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("billingType")] public string? BillingType { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }

    [JsonPropertyName("value")] public decimal? Value { get; set; }
    [JsonPropertyName("netValue")] public decimal? NetValue { get; set; }

    [JsonPropertyName("dueDate")] public string? DueDate { get; set; }
    [JsonPropertyName("paymentDate")] public string? PaymentDate { get; set; }
    [JsonPropertyName("clientPaymentDate")] public string? ClientPaymentDate { get; set; }

    [JsonPropertyName("invoiceUrl")] public string? InvoiceUrl { get; set; }
    [JsonPropertyName("bankSlipUrl")] public string? BankSlipUrl { get; set; }
    [JsonPropertyName("transactionReceiptUrl")] public string? TransactionReceiptUrl { get; set; }
}
