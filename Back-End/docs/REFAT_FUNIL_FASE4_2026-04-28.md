# Refatoração do Funil — Fase 4 (auto-derivação + Resgates + Mudanças de etapa)

Continuação das fases anteriores (já no commit `38c8c73`). Esta fase ataca três pontos pedidos pela operação:

1. **Mudanças manuais de etapa devem refletir automaticamente.** A guard que bloqueava `09/08/10` sem comparecimento prévio sai. O sistema agora auto-deriva `AttendanceStatus` da etapa destino.
2. **RESGATES com destaque na home.** Card hero com número grande, gradient e anel pulsante quando há fila.
3. **Página de mudanças de etapa.** Feed cross-lead com gráficos. Cada linha clica para uma página de jornada do lead com gráficos e timeline.

---

## Backend

### `LeadService.UpdateLeadAsync` — guard removido, auto-derivação ampliada

**Antes:**
```csharp
var blockTransition = LeadStages.RequiresPriorAttendance(novoStage)
                   && lead.AttendanceStatus != LeadStages.AttendedCompareceu;
if (blockTransition) {
    _logger.LogWarning("bloqueado");
    // não transiciona — webhook continua com outros campos
}
else if (lead.CurrentStage != novoStage || ...) {
    // ... transição
    if (novoStage == Faltou) AttendanceStatus = "faltou";
    else if (IsScheduled(novoStage)) AttendanceStatus = null; // reset
}
```

**Depois:**
```csharp
if (lead.CurrentStage != novoStage || lead.CurrentStageId != novoStageId) {
    // ... StageHistory + Interaction + atribuição

    if (novoStage == LeadStages.Faltou) {
        lead.AttendanceStatus = LeadStages.AttendedFaltou;
        lead.AttendanceStatusAt = DateTime.UtcNow;
    }
    else if (LeadStages.RequiresPriorAttendance(novoStage)) {
        // 08/09/10 implicam comparecimento. Confiamos na fonte.
        lead.AttendanceStatus = LeadStages.AttendedCompareceu;
        lead.AttendanceStatusAt = DateTime.UtcNow;
    }
    else if (LeadStages.IsScheduled(novoStage) && lead.AttendanceStatus is not null) {
        lead.AttendanceStatus = null;
        lead.AttendanceStatusAt = null;
    }
}
```

**Linha a linha:**

- Variável `blockTransition` e o `if (blockTransition) LogWarning(...)` foram **removidos**. Não há mais cenário de transição rejeitada.
- Novo `else if (LeadStages.RequiresPriorAttendance(novoStage))` — quando a Cloudia/operadora move para `08`, `09` ou `10`, estampa `AttendanceStatus="compareceu"` automaticamente. Justificativa: na operação real, quem move o lead pra essas etapas está afirmando que o paciente compareceu. Pedir confirmação extra adicionava atrito sem retorno.
- O `Faltou` continua mapeado como antes (`AttendanceStatus="faltou"` quando stage = `07_FALTOU`).
- O reset em `IsScheduled` continua: re-agendar zera o status (lead que faltou e voltou pra `04` começa do zero).

**Trade-off da Fase 4:** transição manual errada vai estampar comparecimento indevido. Como sempre tem `StageHistory` registrando a transição, é auditável e reversível. Em troca, a operação flui sem dashboards pedindo confirmação.

### NOVO `DTOs/Response/StageChangeDto.cs`

```csharp
public class StageChangeDto {
    public int Id, LeadId;
    public string LeadName;
    public string? LeadPhone, UnitName, Source, FromStage;
    public int? UnitId;
    public string ToStage;
    public DateTime ChangedAt;
}

public class StageChangesSummaryDto {
    public int Total;
    public List<StageChangeDailyPointDto> Daily;        // { Date, Count }
    public List<StageChangeDestinationDto> ByDestination; // { Stage, Count }
    public List<StageChangeDto> Items;
}
```

`FromStage` é nullable: a primeira entrada de histórico de um lead não tem etapa anterior.

### `LeadService.GetStageChangesAsync`

```csharp
var q = _db.LeadStageHistories.AsNoTracking()
    .Include(h => h.Lead).ThenInclude(l => l.Unit)
    .Where(h => h.Lead.TenantId == clinicId);
if (unitId.HasValue) q = q.Where(h => h.Lead.UnitId == unitId.Value);
if (fromUtc.HasValue) q = q.Where(h => h.ChangedAt >= fromUtc.Value);
if (toUtc.HasValue)   q = q.Where(h => h.ChangedAt < toUtc.Value);

var total         = await q.CountAsync(ct);
var daily         = await q.GroupBy(h => h.ChangedAt.Date).Select(...).OrderBy(...).ToListAsync(ct);
var byDestination = await q.GroupBy(h => h.StageLabel).Select(...).OrderByDescending(...).ToListAsync(ct);

var raw = await q.OrderByDescending(h => h.ChangedAt).Take(limit).Select(...).ToListAsync(ct);

// Para FromStage: query separada com TODAS as entradas dos leads que apareceram.
var leadIds = raw.Select(x => x.LeadId).Distinct().ToList();
var historiesByLead = await _db.LeadStageHistories.AsNoTracking()
    .Where(h => leadIds.Contains(h.LeadId))
    .OrderBy(h => h.LeadId).ThenBy(h => h.ChangedAt)
    .Select(...).ToListAsync(ct);

var prevByEntry = historiesByLead.GroupBy(h => h.LeadId)
    .ToDictionary(g => g.Key, g => g.OrderBy(x => x.ChangedAt).ToList());

var items = raw.Select(r => {
    string? from = null;
    if (prevByEntry.TryGetValue(r.LeadId, out var list)) {
        var idx = list.FindIndex(x => x.Id == r.Id);
        if (idx > 0) from = list[idx - 1].StageLabel;
    }
    return new StageChangeDto { ... FromStage = from, ToStage = r.ToStage, ... };
}).ToList();
```

**Linha a linha:**

- `Include(h => h.Lead).ThenInclude(l => l.Unit)` — projeção evita N+1.
- `q.GroupBy(h => h.ChangedAt.Date)` — agrupamento por dia (Postgres traduz `Date` para `date_trunc`).
- A query separada de `historiesByLead` resolve `FromStage` em memória. Alternativa elegante seria `LAG()` window function, mas exigiria SQL bruto. O custo aqui é proporcional a `leadIds.Count × média_de_stage_history_por_lead` — geralmente baixo (~5-10 entradas por lead).
- `list.FindIndex(x => x.Id == r.Id)` localiza a entrada atual no histórico ordenado; `idx - 1` é a anterior. Se for a primeira entrada (`idx == 0`), `from` fica null.
- `ToUtcStart`/`ToUtcEndExclusive` (helpers privados adicionados) padronizam janelas UTC com fim exclusivo (compatível com índices em `ChangedAt`).

### `LeadController.GetStageChanges`

```csharp
[HttpGet("stage-changes")]
public async Task<IActionResult> GetStageChanges(
    int clinicId, DateTime? dateFrom, DateTime? dateTo,
    int? unitId, int limit = 100, CancellationToken ct = default)
{
    if (_tenantGuard.EnsureTenantMatches(clinicId) is { } denied) return denied;
    if (unitId.HasValue && await _tenantGuard.EnsureUnitBelongsToTenantAsync(unitId.Value, ct) is { } guard)
        return guard;

    return Ok(await _leadService.GetStageChangesAsync(clinicId, dateFrom, dateTo, unitId, limit, ct));
}
```

Padrão idêntico aos outros endpoints da clínica. Limit clamp (1..500) acontece no service.

---

## Frontend

### `types/index.ts` — quatro tipos novos

```ts
export interface StageChange {
  id: number; leadId: number;
  leadName: string; leadPhone?: string | null;
  unitId?: number | null; unitName?: string | null;
  source?: string | null;
  fromStage?: string | null; toStage: string;
  changedAt: string;
}
export interface StageChangeDailyPoint   { date: string; count: number; }
export interface StageChangeDestination  { stage: string; count: number; }
export interface StageChangesSummary {
  total: number;
  daily: StageChangeDailyPoint[];
  byDestination: StageChangeDestination[];
  items: StageChange[];
}
```

### `services/webhooks.ts` — método novo

```ts
async stageChanges({ clinicId, unitId, dateFrom, dateTo, limit }) {
  const { data } = await api.get<StageChangesSummary>("/webhooks/stage-changes", {
    params: cleanParams({...}),
  });
  return data ?? { total: 0, daily: [], byDestination: [], items: [] };
}
```

Defensivo: retorna struct vazia se backend devolver null (UI não crasha).

### NOVO `pages/MudancasEtapasPage.tsx` (rota `/mudancas-etapas`)

Estrutura:

1. **Filtro de range** (chips: 7/14/30/60 dias). Default `30`.
2. **Dois gráficos lado-a-lado** (Recharts):
   - `AreaChart` "Volume por dia" — verde com gradient.
   - `BarChart` horizontal "Top etapas de destino" — top 8.
3. **Lista de transições** — cada item é `<Link to={/leads/{id}/journey}>` mostrando `from →` `to` com `StageBadge`, data e source.

**Decisões:**

- `useMemo` para `dailySeries`/`destinationSeries` — evita reformatação a cada render.
- Tooltip do Recharts customizado (`background: #0d0d12`) para casar com tema.
- `placeholderData: (prev) => prev` mantém dados visíveis ao trocar range.
- Linha clicável vai pra Jornada — atendendo o pedido "ao clicar abrir uma página diferente trazendo gráficos, linha do tempo".

### NOVO `pages/JourneyPage.tsx` (rota `/leads/:id/journey`)

Reusa `getLeadById` + `getLeadTimeline` (sem novo endpoint). Estrutura:

1. **Header** com título dinâmico (`Jornada · {nome}`) e ações.
2. **Card de resumo**: nome, telefone, `StageBadge`, chip do `attendanceStatus`.
3. **4 InsightCells**: etapas percorridas, mudanças de etapa, reatribuições, etapa mais demorada (cell `wide` ocupa 2 colunas no grid).
4. **Gráfico "Tempo em cada etapa"** — `BarChart` horizontal com `Cell`s coloridos (verde se `isCurrent`, azul caso contrário).
5. **Distribuição do atendimento** (Bot/Fila/Atendimento) — barras horizontais com porcentagem. Banner emerald "Convertido em X" se houver `totalMinutesUntilConversion`.
6. **Linha do tempo das etapas** — lista vertical (`StageStep`) com badge, marker "atual", duração e datas.
7. **Eventos da conversa** — interactions com ícone por tipo:
   - `STAGE_CHANGED` → Route azul
   - `ATTENDANCE_*` → CheckCircle emerald
   - `PAYMENT` → Zap amber
   - outros → History slate

**Por que reusar timeline em vez de criar endpoint:** os dados já estão lá. A Jornada é uma *visualização alternativa* — mais visual, mais focada em gráficos. Quem prefere planilha vai pro `/leads/:id`; quem quer visão estratégica vai pro `/leads/:id/journey`.

### `pages/DashboardPage.tsx` — RESGATES em destaque

Substituí o CTA modesto por um **hero card**:

```tsx
<Link to="/recuperacao" className={cn(
  "group relative block overflow-hidden rounded-2xl border border-amber-500/30 px-6 py-5",
  "bg-[radial-gradient(...amber...transparent),linear-gradient(135deg,rgba(244,63,94,0.10),rgba(251,191,36,0.06))]",
  "hover:shadow-[0_8px_30px_-12px_rgba(251,191,36,0.45)]",
  naoFechouCount > 0 && "ring-1 ring-amber-500/30 ring-offset-2 ring-offset-[#0a0a0d]",
)}>
  {naoFechouCount > 0 && (
    <span className="absolute right-5 top-5">
      <span className="absolute animate-ping rounded-full bg-amber-400 opacity-75" />
      <span className="relative inline-flex h-2 w-2 rounded-full bg-amber-400" />
    </span>
  )}
  <p className="text-[10.5px] tracking-[0.2em] text-amber-300">RESGATES</p>
  <h2 className="text-[44px] font-bold tabular-nums">{naoFechouCount}</h2>
  <p>{naoFechouCount > 0 ? "X oportunidades..." : "Nenhum lead em fila... 🎯"}</p>
  <span>Abrir fila de resgates →</span>
</Link>
```

**Linha a linha:**

- `bg-[radial-gradient(ellipse_at_top_left,...),linear-gradient(135deg,...)]` — gradient duplo. O radial concentra brilho no canto superior esquerdo (atrai o olhar para o número grande); o linear adiciona base âmbar/rosa de fundo.
- `hover:shadow-[0_8px_30px_-12px_rgba(251,191,36,0.45)]` — sombra "glow" amarelo-âmbar no hover.
- `ring-1 ring-amber-500/30 ring-offset-2` ativa **só quando `count > 0`** (`naoFechouCount > 0 && "ring..."`). Sem leads, o card existe limpo; com leads, ele "salta".
- O **anel pulsante** usa o trick clássico do Tailwind: dois `<span>` aninhados — um com `animate-ping` (animação de fade+scale infinita), outro estático sobre. Aparece só quando `count > 0`.
- `text-[44px] font-bold tabular-nums` — número grande, alinhamento monoespaçado (evita pulos quando o valor muda no refresh).
- Mensagem condicional: zero → "Bom trabalho 🎯"; positivo → "X oportunidades de recuperação comercial. Compareceram, mas ainda não fecharam tratamento."

Logo abaixo, um **atalho** discreto para `/mudancas-etapas` — sem gradient/anel pra não brigar com o hero principal.

### `pages/LeadDetailPage.tsx` — botão "Ver jornada"

Adicionado na topbar ao lado do "Marcar comparecimento":

```tsx
<Link to={`/leads/${l.id}/journey`} className="...emerald hover ring...">
  <Route className="h-3.5 w-3.5" /> Ver jornada
</Link>
```

### `App.tsx` + `Sidebar.tsx`

```tsx
// App.tsx
const MudancasEtapasPage = lazy(() => import("@/pages/MudancasEtapasPage"));
const JourneyPage      = lazy(() => import("@/pages/JourneyPage"));
<Route path="/leads/:id/journey" element={<JourneyPage />} />
<Route path="/mudancas-etapas"   element={<MudancasEtapasPage />} />

// Sidebar.tsx — grupo "Leads"
{ to: "/mudancas-etapas", label: "Mudanças de etapa", icon: RouteIcon }
```

`RouteIcon` é alias do `Route` do `lucide-react` — evita colisão com `Route` do `react-router-dom`.

---

## Recap dos fluxos

| Origem | Stage destino | AttendanceStatus auto-aplicado |
|---|---|---|
| Cloudia webhook | `04` ou `05` | `null` (reset, re-agendamento) |
| Cloudia webhook | `07_FALTOU` | `"faltou"` |
| Cloudia webhook | `08`, `09`, `10` | `"compareceu"` |
| `MarkAttendance attended=true outcome=fechou` | `09` | `"compareceu"` |
| `MarkAttendance attended=true outcome=nao_fechou` | `08` | `"compareceu"` |
| `MarkAttendance attended=false` | `07` | `"faltou"` |

Não há mais cenário de transição bloqueada pelo backend.

---

## Como testar

1. **Auto-derivação**: webhook movendo lead de `05` para `09`. Aceito + `attendanceStatus="compareceu"` populado em `GET /webhooks/{id}`.
2. **Hero RESGATES**: home com `naoFechouCount > 0`. Card grande com anel pulsante; texto e número em destaque. Hover mostra glow âmbar. Clique → `/recuperacao`.
3. **Mudanças de etapa**: menu Leads → "Mudanças de etapa". Vê gráficos de volume e top destinos. Clica numa linha → jornada do lead.
4. **Jornada do lead**: `/leads/:id/journey` ou botão "Ver jornada" no detalhe. Header com nome+badges, 4 insights, gráfico de tempo por etapa, distribuição de atendimento, timeline e eventos.
