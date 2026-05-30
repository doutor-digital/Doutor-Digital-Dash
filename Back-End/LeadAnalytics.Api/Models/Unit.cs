namespace LeadAnalytics.Api.Models;

/// <summary>
/// Uma unidade (clínica/filial). Cada unidade é um tenant isolado: seus leads,
/// pagamentos, consultas etc. são filtrados por <see cref="ClinicId"/> (TenantId).
///
/// Cada unidade tem um <see cref="Slug"/> único que compõe a URL pública do webhook
/// da Kommo (ex.: <c>/webhooks/kommo/{slug}</c>). Assim a Kommo de cada unidade
/// aponta para o seu próprio endereço e os dados entram já separados por tenant.
/// </summary>
public class Unit
{
    public int Id { get; set; }

    /// <summary>Identificador do tenant. Todo lead/pagamento referencia este valor via TenantId.</summary>
    public int ClinicId { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>Slug único e estável usado na URL do webhook da Kommo. Ex.: "araguaina".</summary>
    public string? Slug { get; set; }

    // ─── Cadastro ────────────────────────────────────────────
    public string? Email { get; set; }
    public string? Cnpj { get; set; }
    public string? Phone { get; set; }
    public string? AddressLine { get; set; }
    public string? AddressNumber { get; set; }
    public string? Neighborhood { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }

    /// <summary>URL da foto/logo da unidade. Default na imagem da Kommo.</summary>
    public string? PhotoUrl { get; set; }

    public string? ResponsibleName { get; set; }

    /// <summary>Quando false, o webhook da unidade é recusado e ela some das listagens ativas.</summary>
    public bool IsActive { get; set; } = true;

    // ─── Integração Kommo ────────────────────────────────────
    /// <summary>account_id que a Kommo envia nos webhooks (ajuda a validar a origem).</summary>
    public string? KommoAccountId { get; set; }

    /// <summary>Subdomínio da conta Kommo (ex.: "minhaclinica" de minhaclinica.kommo.com).</summary>
    public string? KommoSubdomain { get; set; }

    /// <summary>
    /// Mapa JSON do status_id da Kommo para a etapa canônica do funil.
    /// Ex.: <c>{"67548619":"AGENDADO_COM_PAGAMENTO","67548607":"ENTRADA_LEAD"}</c>.
    /// Preenchido depois que o pipeline da Kommo da unidade existir. Vazio = sem
    /// automação de consulta/tratamento (lead entra/atualiza normalmente).
    /// </summary>
    public string? KommoStageMapJson { get; set; }

    // ─── Auditoria ───────────────────────────────────────────
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public List<Lead> Leads { get; set; } = [];
}
