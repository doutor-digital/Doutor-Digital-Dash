namespace LeadAnalytics.Api.Service;

/// <summary>
/// Classificador heurístico de motivos de não-conversão a partir do texto livre
/// em <c>Lead.Observations</c>. Não é NLP — é lookup de palavras-chave em PT-BR
/// adaptado ao vocabulário do setor odontológico (consulta, fechamento de tratamento).
/// Ordem das categorias importa: a primeira que casar vence (mais específica primeiro).
/// </summary>
public static class RejectionReasons
{
    public sealed record Category(string Key, string Label, string[] Keywords);

    public static readonly Category[] Categories = new Category[]
    {
        new("preco", "Preço / valor",
            new[] { "caro", "preço", "preco", "valor", "custo", "dinheiro",
                    "não tem condição", "nao tem condicao", "sem condicao", "fora do orçamento",
                    "fora do orcamento", "desconto" }),
        new("vai_pensar", "Vai pensar",
            new[] { "pensar", "pensando", "decidir", "depois", "vai retornar",
                    "voltar a falar", "vai analisar", "analisando" }),
        new("convenio", "Quer convênio",
            new[] { "convenio", "convênio", "plano de saude", "plano de saúde",
                    "particular não", "particular nao" }),
        new("familia", "Consultar família",
            new[] { "marido", "esposa", "esposo", "família", "familia",
                    "consultar", "namorado", "namorada", "pais", "mãe", "mae",
                    "pai" }),
        new("tempo_distancia", "Tempo / distância",
            new[] { "longe", "distância", "distancia", "viagem", "tempo",
                    "horario", "horário", "trabalho", "ocupado", "ocupada" }),
        new("medo", "Medo / dúvida clínica",
            new[] { "medo", "dor", "ansioso", "ansiosa", "receio", "duvida",
                    "dúvida", "nervoso", "nervosa" }),
        new("concorrente", "Foi pra concorrente",
            new[] { "outro lugar", "outra clinica", "outra clínica",
                    "concorrente", "outro dentista", "já fechou em",
                    "ja fechou em" }),
        new("nao_atendeu", "Não atendeu / sumiu",
            new[] { "nao atende", "não atende", "sumiu", "não respondeu",
                    "nao respondeu", "fora do ar", "celular off",
                    "fora de área", "fora de area" }),
        new("desistiu", "Desistiu",
            new[] { "desistiu", "desistencia", "desistência", "cancelou",
                    "cancelamento", "não quer", "nao quer" }),
    };

    /// <summary>
    /// Retorna o primeiro <see cref="Category.Key"/> cujo `Keywords` aparece em <paramref name="observations"/>.
    /// Comparação é case-insensitive e não aplica diacrítica antes do match (já cobrimos
    /// variantes "preço" e "preco" na tabela). Retorna <c>null</c> se nada bater (categoria
    /// "Sem motivo registrado" é sintetizada em quem consome).
    /// </summary>
    public static string? Classify(string? observations)
    {
        if (string.IsNullOrWhiteSpace(observations)) return null;
        var text = observations.ToLowerInvariant();

        foreach (var cat in Categories)
        {
            foreach (var kw in cat.Keywords)
            {
                if (text.Contains(kw)) return cat.Key;
            }
        }
        return null;
    }

    public static Category? Get(string key) =>
        Categories.FirstOrDefault(c => c.Key == key);
}
