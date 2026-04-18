using LeadAnalytics.Api.Models;

namespace LeadAnalytics.Api.Service.Filtering;

public enum FilterFieldType { Text, Number, Boolean, Enum, Multiselect, Date }

/// <summary>
/// Definição de um campo filtrável. Só os campos desta whitelist podem aparecer
/// no payload do cliente — os demais resultam em 400.
/// Campos sem coluna de backing no schema atual têm <see cref="Implemented"/>=false
/// e o builder responde 501 Not Implemented em vez de inventar lógica.
/// </summary>
public sealed class FilterFieldDef
{
    public required string Id { get; init; }
    public required FilterFieldType Type { get; init; }
    public bool Implemented { get; init; } = true;
    public Func<IQueryable<Contact>, string, ValueReader, IQueryable<Contact>>? Apply { get; init; }
}

public static class ContactFilterRegistry
{
    public static readonly IReadOnlyDictionary<string, FilterFieldDef> Fields =
        BuildFields().ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<FilterFieldDef> BuildFields()
    {
        // ─── Identificação ─────────────────────────────────────────
        yield return new FilterFieldDef
        {
            Id = "name", Type = FilterFieldType.Text,
            Apply = (q, op, v) =>
            {
                if (op == "is_empty") return q.Where(c => c.Name == null || c.Name == "");
                if (op == "is_not_empty") return q.Where(c => c.Name != null && c.Name != "");
                var s = v.ReadString().ToLower();
                return op switch
                {
                    "contains" => q.Where(c => c.Name.ToLower().Contains(s)),
                    "not_contains" => q.Where(c => !c.Name.ToLower().Contains(s)),
                    "equals" => q.Where(c => c.Name.ToLower() == s),
                    "starts_with" => q.Where(c => c.Name.ToLower().StartsWith(s)),
                    _ => throw new FilterValidationException($"operador '{op}' não suportado em text")
                };
            }
        };

        yield return new FilterFieldDef
        {
            Id = "phone", Type = FilterFieldType.Text,
            Apply = (q, op, v) =>
            {
                if (op == "is_empty") return q.Where(c => c.PhoneNormalized == null || c.PhoneNormalized == "");
                if (op == "is_not_empty") return q.Where(c => c.PhoneNormalized != null && c.PhoneNormalized != "");
                var s = v.ReadString();
                return op switch
                {
                    "contains" => q.Where(c => c.PhoneNormalized.Contains(s)),
                    "not_contains" => q.Where(c => !c.PhoneNormalized.Contains(s)),
                    "equals" => q.Where(c => c.PhoneNormalized == s),
                    "starts_with" => q.Where(c => c.PhoneNormalized.StartsWith(s)),
                    _ => throw new FilterValidationException($"operador '{op}' não suportado em text")
                };
            }
        };

        yield return new FilterFieldDef
        {
            Id = "tem_telefone", Type = FilterFieldType.Boolean,
            Apply = (q, op, v) => op switch
            {
                "is_true" => q.Where(c => c.PhoneNormalized != null && c.PhoneNormalized != ""),
                "is_false" => q.Where(c => c.PhoneNormalized == null || c.PhoneNormalized == ""),
                _ => throw new FilterValidationException($"operador '{op}' não suportado em boolean")
            }
        };

        yield return NotImplemented("email", FilterFieldType.Text);

        // ─── Consulta ──────────────────────────────────────────────
        yield return new FilterFieldDef
        {
            Id = "ja_marcou_consulta", Type = FilterFieldType.Boolean,
            Apply = (q, op, v) => op switch
            {
                "is_true" => q.Where(c => c.ConsultationAt != null),
                "is_false" => q.Where(c => c.ConsultationAt == null),
                _ => throw new FilterValidationException($"operador '{op}' não suportado em boolean")
            }
        };

        yield return new FilterFieldDef
        {
            Id = "agendamento_manual", Type = FilterFieldType.Boolean,
            Apply = (q, op, v) => op switch
            {
                "is_true" => q.Where(c => c.Origem == "manual"),
                "is_false" => q.Where(c => c.Origem != "manual"),
                _ => throw new FilterValidationException($"operador '{op}' não suportado em boolean")
            }
        };

        yield return new FilterFieldDef
        {
            Id = "comparecimento", Type = FilterFieldType.Multiselect,
            Apply = (q, op, v) =>
            {
                var list = v.ReadStringList();
                EnsureAttendance(list);
                return op switch
                {
                    "in" => q.Where(c => c.AttendanceStatus != null && list.Contains(c.AttendanceStatus)),
                    "not_in" => q.Where(c => c.AttendanceStatus == null || !list.Contains(c.AttendanceStatus)),
                    _ => throw new FilterValidationException($"operador '{op}' não suportado em multiselect")
                };
            }
        };

        yield return NotImplemented("ja_cancelou", FilterFieldType.Boolean);
        yield return NotImplemented("tipo_consulta", FilterFieldType.Multiselect);
        yield return NotImplemented("local_ultima_consulta", FilterFieldType.Multiselect);
        yield return NotImplemented("profissional", FilterFieldType.Multiselect);
        yield return NotImplemented("status_ultima_consulta", FilterFieldType.Multiselect);

        // ─── Anúncios (sem coluna no schema atual) ──────────────────
        yield return NotImplemented("anuncio_facebook", FilterFieldType.Text);
        yield return NotImplemented("anuncio_instagram", FilterFieldType.Text);
        yield return NotImplemented("anuncio_whatsapp", FilterFieldType.Text);
        yield return NotImplemented("anuncio_utm", FilterFieldType.Multiselect);
        yield return NotImplemented("anuncio_mais_recente", FilterFieldType.Text);
        yield return NotImplemented("campanha", FilterFieldType.Multiselect);
        yield return NotImplemented("conjunto_anuncios", FilterFieldType.Multiselect);
        yield return NotImplemented("origem_anuncio", FilterFieldType.Enum);
        yield return NotImplemented("ja_interagiu_anuncio", FilterFieldType.Boolean);

        // ─── Conversa (sem coluna no schema atual) ──────────────────
        yield return NotImplemented("canal", FilterFieldType.Multiselect);
        yield return NotImplemented("usuarios_atribuidos", FilterFieldType.Multiselect);
        yield return NotImplemented("ja_atendido_chat", FilterFieldType.Boolean);
        yield return NotImplemented("primeiro_contato", FilterFieldType.Boolean);
        yield return NotImplemented("esta_em_sequencia", FilterFieldType.Boolean);
        yield return NotImplemented("deixou_comentario", FilterFieldType.Boolean);
        yield return NotImplemented("estado_conversa", FilterFieldType.Multiselect);
        yield return NotImplemented("motivo_conclusao", FilterFieldType.Multiselect);

        // ─── Organização ───────────────────────────────────────────
        // Tags: persistidas como JSON string "[\"a\",\"b\"]" (não array nativo).
        // Matching por substring com aspas evita colisão com prefixos ("vip" x "vip_gold").
        yield return new FilterFieldDef
        {
            Id = "tags", Type = FilterFieldType.Multiselect,
            Apply = (q, op, v) =>
            {
                var list = v.ReadStringList();
                if (list.Count == 0) return q;
                var quoted = list.Select(t => "\"" + t.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"").ToList();
                return op switch
                {
                    "in" => q.Where(c => c.TagsJson != null && quoted.Any(t => c.TagsJson.Contains(t))),
                    "not_in" => q.Where(c => c.TagsJson == null || !quoted.Any(t => c.TagsJson.Contains(t))),
                    _ => throw new FilterValidationException($"operador '{op}' não suportado em multiselect")
                };
            }
        };

        yield return new FilterFieldDef
        {
            Id = "etapas", Type = FilterFieldType.Multiselect,
            Apply = (q, op, v) =>
            {
                var list = v.ReadStringList();
                if (list.Count == 0) return q;
                return op switch
                {
                    "in" => q.Where(c => c.Etapa != null && list.Contains(c.Etapa)),
                    "not_in" => q.Where(c => c.Etapa == null || !list.Contains(c.Etapa)),
                    _ => throw new FilterValidationException($"operador '{op}' não suportado em multiselect")
                };
            }
        };

        yield return new FilterFieldDef
        {
            Id = "conexoes", Type = FilterFieldType.Multiselect,
            Apply = (q, op, v) =>
            {
                var list = v.ReadStringList();
                if (list.Count == 0) return q;
                return op switch
                {
                    "in" => q.Where(c => c.Conexao != null && list.Contains(c.Conexao)),
                    "not_in" => q.Where(c => c.Conexao == null || !list.Contains(c.Conexao)),
                    _ => throw new FilterValidationException($"operador '{op}' não suportado em multiselect")
                };
            }
        };

        yield return NotImplemented("departamentos", FilterFieldType.Multiselect);

        // ─── Dados do contato ──────────────────────────────────────
        yield return new FilterFieldDef
        {
            Id = "bloqueado", Type = FilterFieldType.Boolean,
            Apply = (q, op, v) => op switch
            {
                "is_true" => q.Where(c => c.Blocked),
                "is_false" => q.Where(c => !c.Blocked),
                _ => throw new FilterValidationException($"operador '{op}' não suportado em boolean")
            }
        };

        yield return NotImplemented("genero", FilterFieldType.Enum);
        yield return NotImplemented("cidade_preferencia", FilterFieldType.Multiselect);
        yield return NotImplemented("plano_saude", FilterFieldType.Multiselect);
        yield return NotImplemented("possui_plano_saude", FilterFieldType.Boolean);
        yield return NotImplemented("valor", FilterFieldType.Number);
        yield return NotImplemented("fb_messenger_whatsapp", FilterFieldType.Multiselect);

        // ─── Datas ─────────────────────────────────────────────────
        yield return DateField("data_nascimento", DateColumn.Birthday);
        yield return DateField("data_aniversario", DateColumn.Birthday);
        yield return DateField("data_consulta", DateColumn.ConsultationAt);
        yield return DateField("data_registro_consulta", DateColumn.ConsultationRegisteredAt);
        yield return DateField("data_ultima_mensagem", DateColumn.LastMessageAt);

        yield return NotImplemented("data_ultimo_comentario", FilterFieldType.Date);
        yield return NotImplemented("data_ultima_consulta_cancelada", FilterFieldType.Date);
        yield return NotImplemented("data_ultimo_cancelamento", FilterFieldType.Date);
        yield return NotImplemented("data_primeiro_contato", FilterFieldType.Date);
        yield return NotImplemented("data_criacao_anuncio", FilterFieldType.Date);
        yield return NotImplemented("data_interacao_anuncio", FilterFieldType.Date);
        yield return NotImplemented("data_interacao_campanha", FilterFieldType.Date);
        yield return NotImplemented("entrou_etapa_em", FilterFieldType.Date);
    }

    // ──────────────────────────────────────────────────────────────
    //  Helpers de montagem
    // ──────────────────────────────────────────────────────────────

    private static FilterFieldDef NotImplemented(string id, FilterFieldType type) =>
        new() { Id = id, Type = type, Implemented = false, Apply = null };

    private enum DateColumn { Birthday, ConsultationAt, ConsultationRegisteredAt, LastMessageAt }

    private static FilterFieldDef DateField(string id, DateColumn col) => new()
    {
        Id = id,
        Type = FilterFieldType.Date,
        Apply = (q, op, v) => ApplyDate(q, op, v, col)
    };

    private static IQueryable<Contact> ApplyDate(IQueryable<Contact> q, string op, ValueReader v, DateColumn col)
    {
        switch (op)
        {
            case "on":
            {
                var d = v.ReadDate();
                var next = d.AddDays(1);
                return col switch
                {
                    DateColumn.Birthday => q.Where(c => c.Birthday >= d && c.Birthday < next),
                    DateColumn.ConsultationAt => q.Where(c => c.ConsultationAt >= d && c.ConsultationAt < next),
                    DateColumn.ConsultationRegisteredAt => q.Where(c => c.ConsultationRegisteredAt >= d && c.ConsultationRegisteredAt < next),
                    DateColumn.LastMessageAt => q.Where(c => c.LastMessageAt >= d && c.LastMessageAt < next),
                    _ => q
                };
            }
            case "before":
            {
                var d = v.ReadDate();
                return col switch
                {
                    DateColumn.Birthday => q.Where(c => c.Birthday < d),
                    DateColumn.ConsultationAt => q.Where(c => c.ConsultationAt < d),
                    DateColumn.ConsultationRegisteredAt => q.Where(c => c.ConsultationRegisteredAt < d),
                    DateColumn.LastMessageAt => q.Where(c => c.LastMessageAt < d),
                    _ => q
                };
            }
            case "after":
            {
                var d = v.ReadDate();
                return col switch
                {
                    DateColumn.Birthday => q.Where(c => c.Birthday > d),
                    DateColumn.ConsultationAt => q.Where(c => c.ConsultationAt > d),
                    DateColumn.ConsultationRegisteredAt => q.Where(c => c.ConsultationRegisteredAt > d),
                    DateColumn.LastMessageAt => q.Where(c => c.LastMessageAt > d),
                    _ => q
                };
            }
            case "last_n_days":
            {
                var n = v.ReadPositiveInt();
                var t = DateTime.UtcNow.AddDays(-n);
                return col switch
                {
                    DateColumn.Birthday => q.Where(c => c.Birthday != null && c.Birthday >= t),
                    DateColumn.ConsultationAt => q.Where(c => c.ConsultationAt != null && c.ConsultationAt >= t),
                    DateColumn.ConsultationRegisteredAt => q.Where(c => c.ConsultationRegisteredAt != null && c.ConsultationRegisteredAt >= t),
                    DateColumn.LastMessageAt => q.Where(c => c.LastMessageAt != null && c.LastMessageAt >= t),
                    _ => q
                };
            }
            case "next_n_days":
            {
                var n = v.ReadPositiveInt();
                var now = DateTime.UtcNow;
                var end = now.AddDays(n);
                return col switch
                {
                    DateColumn.Birthday => q.Where(c => c.Birthday != null && c.Birthday >= now && c.Birthday <= end),
                    DateColumn.ConsultationAt => q.Where(c => c.ConsultationAt != null && c.ConsultationAt >= now && c.ConsultationAt <= end),
                    DateColumn.ConsultationRegisteredAt => q.Where(c => c.ConsultationRegisteredAt != null && c.ConsultationRegisteredAt >= now && c.ConsultationRegisteredAt <= end),
                    DateColumn.LastMessageAt => q.Where(c => c.LastMessageAt != null && c.LastMessageAt >= now && c.LastMessageAt <= end),
                    _ => q
                };
            }
            case "is_empty":
                return col switch
                {
                    DateColumn.Birthday => q.Where(c => c.Birthday == null),
                    DateColumn.ConsultationAt => q.Where(c => c.ConsultationAt == null),
                    DateColumn.ConsultationRegisteredAt => q.Where(c => c.ConsultationRegisteredAt == null),
                    DateColumn.LastMessageAt => q.Where(c => c.LastMessageAt == null),
                    _ => q
                };
            case "is_not_empty":
                return col switch
                {
                    DateColumn.Birthday => q.Where(c => c.Birthday != null),
                    DateColumn.ConsultationAt => q.Where(c => c.ConsultationAt != null),
                    DateColumn.ConsultationRegisteredAt => q.Where(c => c.ConsultationRegisteredAt != null),
                    DateColumn.LastMessageAt => q.Where(c => c.LastMessageAt != null),
                    _ => q
                };
            default:
                throw new FilterValidationException($"operador '{op}' não suportado em date");
        }
    }

    private static readonly string[] AllowedAttendance = new[] { "compareceu", "faltou", "aguardando" };
    private static void EnsureAttendance(List<string> values)
    {
        foreach (var v in values)
            if (!AllowedAttendance.Contains(v))
                throw new FilterValidationException(
                    $"valor '{v}' inválido para comparecimento (use compareceu, faltou, aguardando)");
    }

    public static readonly IReadOnlyDictionary<FilterFieldType, HashSet<string>> OperatorsByType =
        new Dictionary<FilterFieldType, HashSet<string>>
        {
            [FilterFieldType.Text] = new(new[] { "contains", "not_contains", "equals", "starts_with", "is_empty", "is_not_empty" }),
            [FilterFieldType.Number] = new(new[] { "eq", "neq", "gt", "gte", "lt", "lte", "between" }),
            [FilterFieldType.Boolean] = new(new[] { "is_true", "is_false" }),
            [FilterFieldType.Enum] = new(new[] { "is", "is_not" }),
            [FilterFieldType.Multiselect] = new(new[] { "in", "not_in" }),
            [FilterFieldType.Date] = new(new[] { "on", "before", "after", "last_n_days", "next_n_days", "is_empty", "is_not_empty" }),
        };
}
