namespace LeadAnalytics.Api.Models;

/// <summary>
/// O mapa de etapas de cada conta Kommo: id → nome atual.
///
/// POR QUE ESTA TABELA EXISTE
/// --------------------------
/// O histórico (<c>lead_stage_histories</c>) guardava o NOME da etapa no momento
/// da mudança. Nome muda: renomeamos etapas em todas as unidades, e o resultado
/// foram **202 rótulos distintos para ~12 etapas reais** — `QUALIFICACAO`,
/// `04_AGENDADO_SEM_PAGAMENTO`, `10_EM_TRATAMENTO` e `COMPARECEU` convivendo — e
/// **22% dos registros com o ID CRU no lugar do nome** (`143`, `108772996`),
/// porque quando o webhook não sabia o nome ele gravava o número.
///
/// Com o nome persistido, qualquer contagem por etapa erra: ou o rótulo mudou,
/// ou é um número que ninguém reconhece. O `StageId` sempre esteve lá e é
/// estável — 100% dos registros o têm. Então a regra passa a ser:
/// **guardar o id, resolver o nome na leitura por esta tabela.**
/// </summary>
public class KommoStage
{
    public int Id { get; set; }

    public int UnitId { get; set; }

    /// <summary>Funil (pipeline) na Kommo. 142/143 se repetem entre funis — sem ele não dá pra distinguir "PERDIDO" de "TRATAMENTO CANCELADO".</summary>
    public long PipelineId { get; set; }

    public string PipelineName { get; set; } = string.Empty;

    /// <summary>Id da etapa. É a chave estável: renomear não muda.</summary>
    public long StatusId { get; set; }

    /// <summary>Nome ATUAL da etapa, como está na Kommo hoje.</summary>
    public string StatusName { get; set; } = string.Empty;

    /// <summary>Ordem na tela — serve pra montar o funil na ordem certa.</summary>
    public int Sort { get; set; }

    public DateTime UpdatedAt { get; set; }
}
