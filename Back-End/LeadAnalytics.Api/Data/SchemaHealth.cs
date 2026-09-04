namespace LeadAnalytics.Api.Data;

/// <summary>
/// O estado do schema no startup, guardado para poder ser perguntado depois.
///
/// POR QUE ISSO EXISTE
/// -------------------
/// A migration <c>MapaDeEtapasKommo</c> falhou no startup, o catch registrou o
/// erro no log e a aplicação subiu assim mesmo. O <c>/health</c> continuou
/// devolvendo "ok", o swarm convergiu o deploy, e a tabela que faltava só
/// apareceu como <c>42P01</c> na primeira consulta — horas depois, dentro de
/// 88 mil linhas de log.
///
/// O log já dizia a verdade; ninguém tinha como perguntar. Agora dá:
/// <c>/health</c> carrega o veredito e <c>/internal/schema</c> o detalhe.
/// </summary>
public static class SchemaHealth
{
    public static DateTime? VerificadoEm { get; private set; }
    public static IReadOnlyList<string> Aplicadas { get; private set; } = [];
    public static IReadOnlyList<string> Pendentes { get; private set; } = [];
    public static string? Erro { get; private set; }

    /// <summary>Schema íntegro = migrou sem erro e não sobrou nada pendente.</summary>
    public static bool Ok => Erro is null && Pendentes.Count == 0 && VerificadoEm is not null;

    public static void Registrar(IEnumerable<string> aplicadas, IEnumerable<string> pendentes, string? erro)
    {
        Aplicadas = aplicadas.ToList();
        Pendentes = pendentes.ToList();
        Erro = erro;
        VerificadoEm = DateTime.UtcNow;
    }
}
