using LeadAnalytics.Api.Service;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// A regra que decide se um card é movido no CRM DO CLIENTE.
///
/// POR QUE ESTE ARQUIVO EXISTE
/// ---------------------------
/// Mover card é escrita irreversível numa conta de produção: dispara o Pipeline Digital
/// e os bots, que podem mandar mensagem para paciente real, e o carimbo de entrada na
/// etapa fica no histórico para sempre. Não existe "desfazer" — existe "mover de volta",
/// que deixa as duas passagens registradas.
///
/// E o erro fácil aqui é invisível no número: a Kommo reaproveita os ids 142 e 143 em
/// TODO funil. No comercial, 142 é GANHO — mover para tratamento é o passo certo. No
/// funil de tratamento, 142 é ALTA: mover de volta ressuscitaria um paciente que já
/// recebeu alta. O mesmo id, dois significados opostos, e só o funil separa.
///
/// Medido na Imperatriz (01/09/2026): dos 16 tratamentos de agosto, 8 cards estavam
/// fora da etapa — todos no funil COMERCIAL, dois deles marcados como PERDIDO.
/// </summary>
public class MoverParaTratamentoTests
{
    private const int FunilTratamento = 14091116;
    private const int EtapaEmTratamento = 108773168;
    private const int FunilComercial = 14091100;

    private static CandidatoAoTratamento Card(long? etapa, long? funil) =>
        new(IdTreatment: 120311, LeadId: 13021638, Paciente: "Walter Braz",
            DiaLancamento: new DateOnly(2026, 8, 1), EtapaAtual: etapa, FunilAtual: funil);

    private static DecisaoDeMovimento Decidir(long? etapa, long? funil) =>
        MoverParaTratamentoService.Decidir(Card(etapa, funil), FunilTratamento, EtapaEmTratamento);

    // ─── O que deve mover ───────────────────────────────────────────────────────

    /// GANHO no comercial: o comercial fechou e o próximo passo é o tratamento.
    [Fact]
    public void Ganho_no_comercial_vai_para_tratamento()
    {
        var d = Decidir(KommoStatusNativos.Ganho, FunilComercial);

        Assert.True(d.Mover);
    }

    /// PERDIDO no comercial com tratamento fechado na clínica é erro de CRM — o caso da
    /// Maria Zita e do Cristiano. Corrigir é justamente o motivo desta rota existir.
    [Fact]
    public void Perdido_no_comercial_e_corrigido()
    {
        var d = Decidir(KommoStatusNativos.Perdido, FunilComercial);

        Assert.True(d.Mover);
    }

    /// Card no meio do funil comercial também avança.
    [Fact]
    public void Etapa_comum_do_comercial_avanca()
    {
        var d = Decidir(108773012, FunilComercial);   // COMPARECEU

        Assert.True(d.Mover);
    }

    // ─── O que NUNCA pode mover ─────────────────────────────────────────────────

    /// ALTA (142 no funil de TRATAMENTO) é paciente que terminou. Mover de volta o faria
    /// reaparecer como ativo — e a alta é o "ganho" do negócio.
    [Fact]
    public void Alta_no_funil_de_tratamento_nao_e_desfeita()
    {
        var d = Decidir(KommoStatusNativos.Ganho, FunilTratamento);

        Assert.False(d.Mover);
        Assert.Contains("alta", d.Motivo, StringComparison.OrdinalIgnoreCase);
    }

    /// TRATAMENTO CANCELADO (143 no funil de tratamento) é desistência registrada.
    [Fact]
    public void Tratamento_cancelado_nao_e_revertido()
    {
        var d = Decidir(KommoStatusNativos.Perdido, FunilTratamento);

        Assert.False(d.Mover);
        Assert.Contains("cancelado", d.Motivo, StringComparison.OrdinalIgnoreCase);
    }

    /// O MESMO id decide diferente conforme o funil — é a asserção que resume o perigo.
    [Theory]
    [InlineData(KommoStatusNativos.Ganho)]
    [InlineData(KommoStatusNativos.Perdido)]
    public void Mesmo_id_decide_diferente_conforme_o_funil(int status)
    {
        Assert.True(Decidir(status, FunilComercial).Mover);
        Assert.False(Decidir(status, FunilTratamento).Mover);
    }

    /// Já está na etapa: não reescreve o histórico com uma entrada duplicada.
    [Fact]
    public void Card_ja_na_etapa_nao_e_movido()
    {
        var d = Decidir(EtapaEmTratamento, FunilTratamento);

        Assert.False(d.Mover);
        Assert.Contains("já está", d.Motivo);
    }

    /// Sem conseguir ler a etapa atual, não se escreve nada: mover às cegas pode
    /// atropelar uma alta que a gente não viu.
    [Theory]
    [InlineData(null, null)]
    [InlineData(108773012, null)]
    [InlineData(null, 14091100)]
    public void Sem_saber_a_etapa_atual_nao_move(int? etapa, int? funil)
    {
        var d = Decidir(etapa, funil);

        Assert.False(d.Mover);
        Assert.Contains("não consegui ler", d.Motivo);
    }

    /// Entrada no começo do funil de tratamento avança normalmente.
    [Fact]
    public void Incoming_do_funil_de_tratamento_avanca()
    {
        var d = Decidir(108773164, FunilTratamento);   // Incoming leads (TRATAMENTO)

        Assert.True(d.Mover);
    }
}
