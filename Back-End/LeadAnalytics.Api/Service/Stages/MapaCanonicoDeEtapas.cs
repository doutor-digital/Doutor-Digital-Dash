namespace LeadAnalytics.Api.Service.Stages;

/// <summary>Uma etapa da Kommo como ela existe na conta: funil, id da etapa e nome.</summary>
public readonly record struct EtapaDaConta(long Funil, long Etapa, string? Nome);

/// <summary>
/// Monta o mapa "etapa da Kommo → etapa canônica" a partir dos funis da conta. É o MESMO
/// mapa para o sync e para o webhook — antes cada um montava o seu, e o do webhook só
/// conhecia o número da etapa.
///
/// POR QUE A CHAVE É (FUNIL, ETAPA)
/// --------------------------------
/// Os ids 142 (ganho) e 143 (perdido) existem em TODOS os funis da conta, com nomes
/// diferentes. Na Açailândia o 143 é "PERDIDO" no COMERCIAL e "TRATAMENTO CANCELADO" no
/// TRATAMENTO. Indexando só pelo número, o último funil lido vencia: medido em 16/10/2026,
/// 180 de 180 leads perdidos do COMERCIAL estavam gravados como "tratamento cancelado".
///
/// A chave sem funil só entra quando o número tem UM significado na conta inteira. Se o
/// mesmo id vira etapas canônicas diferentes em dois funis, escolher uma seria chute — o
/// evento sem funil fica sem tradução (cai no mapa manual da unidade, se houver).
/// </summary>
public static class MapaCanonicoDeEtapas
{
    /// <summary>Chave qualificada pelo funil, no formato que o <see cref="KommoIngestionService"/> procura.</summary>
    public static string Chave(long funil, long etapa) => $"{funil}:{etapa}";

    public static Dictionary<string, string> Montar(IEnumerable<EtapaDaConta> etapas)
    {
        var mapa = new Dictionary<string, string>();
        var porNumero = new Dictionary<long, HashSet<string>>();

        foreach (var e in etapas)
        {
            var canonica = CanonicalStages.Resolve(e.Nome);

            // Conta TODA ocorrência do número, inclusive a que não se traduz: um terceiro
            // funil com o 143 chamado "Venda perdida" (nome padrão da Kommo, não casa com
            // PERDIDO) também torna o 143 ambíguo. Sem isso a chave simples nascia e os
            // leads desse funil caíam nela.
            if (!porNumero.TryGetValue(e.Etapa, out var sentidos))
                porNumero[e.Etapa] = sentidos = new HashSet<string>(StringComparer.Ordinal);
            sentidos.Add(canonica ?? SemTraducao);

            if (canonica is not null)
                mapa[Chave(e.Funil, e.Etapa)] = canonica;
        }

        foreach (var (numero, sentidos) in porNumero)
            if (sentidos.Count == 1 && !sentidos.Contains(SemTraducao))
                mapa[numero.ToString()] = sentidos.First();

        return mapa;
    }

    /// <summary>Marca, no <see cref="Montar"/>, a ocorrência cujo nome não vira etapa canônica.</summary>
    private const string SemTraducao = "\0sem-traducao";

    /// <summary>
    /// Números de etapa que o mapa automático sabe que são ambíguos: têm chave com funil, mas
    /// não ganharam a chave simples (dois funis discordam — ver <see cref="Montar"/>).
    /// </summary>
    public static HashSet<string> NumerosAmbiguos(IReadOnlyDictionary<string, string> automatico)
    {
        var ambiguos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chave in automatico.Keys)
        {
            var i = chave.IndexOf(':');
            if (i < 0) continue;
            var numero = chave[(i + 1)..];
            if (!automatico.ContainsKey(numero)) ambiguos.Add(numero);
        }
        return ambiguos;
    }

    /// <summary>
    /// Traduz a etapa de um evento combinando o mapa MANUAL da unidade (KommoStageMapJson,
    /// config do admin) com o AUTOMÁTICO (nomes das etapas na Kommo). Ordem:
    /// <list type="number">
    ///   <item>manual com funil (<c>"{funil}:{etapa}"</c>);</item>
    ///   <item>manual só com o número — o admin vence, MENOS quando o número é ambíguo na
    ///   conta (142/143): aí "143" sozinho não diz de qual funil é;</item>
    ///   <item>automático com funil;</item>
    ///   <item>automático só com o número (só existe quando não é ambíguo).</item>
    /// </list>
    /// Com o automático vazio (Kommo fora do ar, sem token), nenhum número é ambíguo e o mapa
    /// manual vale inteiro, como antes.
    /// </summary>
    public static string? Traduzir(
        IReadOnlyDictionary<string, string> manual,
        IReadOnlyDictionary<string, string> automatico,
        ISet<string> ambiguos,
        string? funil, string etapa)
    {
        var temFunil = !string.IsNullOrWhiteSpace(funil);
        if (temFunil && manual.TryGetValue($"{funil}:{etapa}", out var v)) return v;
        var ambigua = ambiguos.Contains(etapa);
        if (!ambigua && manual.TryGetValue(etapa, out v)) return v;
        if (temFunil && automatico.TryGetValue($"{funil}:{etapa}", out v)) return v;
        if (automatico.TryGetValue(etapa, out v)) return v;
        return null;
    }

    /// <summary>
    /// Nome da etapa pelo par (funil, etapa). Sem funil, só devolve o nome quando o número
    /// tem um nome só na conta — mesmo critério do <see cref="Montar"/>.
    /// </summary>
    public static string? Nome(IReadOnlyCollection<EtapaDaConta> etapas, long? funil, long etapa)
    {
        if (funil is long f)
        {
            foreach (var e in etapas)
                if (e.Funil == f && e.Etapa == etapa) return e.Nome;
        }

        var nomes = etapas.Where(e => e.Etapa == etapa && !string.IsNullOrWhiteSpace(e.Nome))
            .Select(e => e.Nome!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return nomes.Count == 1 ? nomes[0] : null;
    }
}
