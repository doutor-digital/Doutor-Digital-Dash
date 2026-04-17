/**
 * Tipos do contrato OpenAPI do LeadAnalytics.Api.
 *
 * Três grupos:
 *  1. DTOs formalmente declarados no Swagger (fonte-de-verdade).
 *  2. DTOs referenciados no contrato mas sem schema inlineado
 *     (ex.: SetApiKeyRequest) — tipados por inferência documentada.
 *  3. Tipos derivados do consumo real do front (ex.: LeadMetrics) —
 *     claramente marcados como "INFERRED", com TODO para schema.
 *
 * Regras:
 *  - IDs inteiros (`int32`) são `number`.
 *  - Datas `date-time` chegam como `string` ISO — conversão é
 *    responsabilidade dos adapters, nunca dos services.
 *  - Nullability replicada do Swagger (`nullable: true` -> `| null`).
 */

// ─── 1. DTOs do contrato ────────────────────────────────────────────

/** OpenAPI: components.schemas.ActiveLeadDto */
export interface ActiveLeadDto {
  id: number;
  externalId: number;
  name: string | null;
  phone: string | null;
  conversationState: string | null;
  attendantId: number | null;
  unitId: number | null;
  updatedAt: string;
  createdAt: string;
}

/** OpenAPI: components.schemas.LeadsCountDto */
export interface LeadsCountDto {
  bot: number;
  queue: number;
  service: number;
  concluido: number;
  total: number;
}

/** OpenAPI: components.schemas.ProblemDetails */
export interface ProblemDetails {
  type: string | null;
  title: string | null;
  status: number | null;
  detail: string | null;
  instance: string | null;
  [extension: string]: unknown;
}

/** OpenAPI: components.schemas.SyncLeadDto */
export interface SyncLeadDto {
  externalId: number;
  tags: string[] | null;
  name: string | null;
  phone: string | null;
  stage: string | null;
  tenantId: number;
  createdAt: string | null;
  updatedAt: string | null;
}

// ─── 2. Referenciados mas sem schema inlineado ─────────────────────

/**
 * OpenAPI: components.schemas.SetApiKeyRequest
 * LIMITAÇÃO: o contrato referencia este schema mas NÃO inlineia os
 * campos. Inferido a partir do comportamento do backend descrito na
 * issue original ("token de API da Cloudia no banco, com expiração
 * configurável").
 * TODO backend: publicar o schema oficial.
 */
export interface SetApiKeyRequest {
  apiKey: string;
  expiresAt?: string | null;
}

// ─── 3. DTOs inferidos de consumo (sem schema no contrato) ─────────

/**
 * INFERRED — `/api/analytics/leads/{id}/metrics`.
 * Sem schema no OpenAPI. Tipado a partir do contrato funcional descrito
 * na especificação original do projeto.
 * TODO backend: publicar `LeadMetricsDto`.
 */
export type ConversationState = "bot" | "queue" | "service" | "concluido";

export interface LeadMetricsDto {
  leadId: number;
  name: string | null;
  currentState: ConversationState | null;
  timeInBot: number | null;
  timeInQueue: number | null;
  timeInService: number | null;
  totalTime: number | null;
  timeToFirstAttendance: number | null;
  timeToResolution: number | null;
  alerts: string[];
  transitions: StateTransitionDto[];
  interactions: InteractionDto[];
}

export interface StateTransitionDto {
  from: ConversationState;
  to: ConversationState;
  at: string;
}

export interface InteractionDto {
  id: string | number;
  type: string;
  content: string | null;
  at: string;
}

/** INFERRED — `/api/analytics/units/{unitId}/summary`. */
export interface UnitSummaryDto {
  totalLeads: number;
  totals: Partial<Record<ConversationState, number>>;
  averages: Partial<Record<ConversationState, number>> & {
    firstAttendance?: number;
    resolution?: number;
  };
  alertsCount: number;
  topAttendants: AttendantPerformanceDto[];
}

export interface AttendantPerformanceDto {
  attendantId: number;
  attendantName: string;
  total: number;
  conversions: number;
}

/** INFERRED — `/api/analytics/units/{unitId}/dashboard/today`. */
export interface UnitDashboardTodayDto {
  summary: UnitSummaryDto;
  alerts: LeadMetricsDto[];
  topAttendants: AttendantPerformanceDto[];
}

/** INFERRED — `/metrics/*`. A Cloudia devolve shape variável entre
 *  deployments — tratamos o payload com adapters e expomos um
 *  contrato estável para o front. */
export interface LiveMetricsDto {
  atendentes: LiveAttendantDto[];
  fila: LiveQueueItemDto[];
  totalEmAtendimento: number;
  totalNaFila: number;
  tempoMedio: number | null;
  raw: unknown;
}

export interface LiveAttendantDto {
  name: string;
  status: string | null;
  emAtendimento: number;
  naFila: number;
  tempoMedio: number | null;
}

export interface LiveQueueItemDto {
  name: string;
  phone: string | null;
  waitingSince: string | null;
  waitingMinutes: number | null;
}

/** INFERRED — `/assignments/attendants`. */
export interface AttendantDto {
  id: number;
  name: string;
  email: string | null;
  totalAssignments: number | null;
  conversions: number | null;
}

/** INFERRED — `/assignments/ranking`. */
export interface AttendantRankingDto {
  attendantId: number;
  name: string;
  total: number;
  conversions: number | null;
}

/** INFERRED — `/assignments/lead/{externalLeadId}`. */
export interface LeadAssignmentHistoryDto {
  attendantId: number;
  attendantName: string;
  assignedAt: string;
  unassignedAt: string | null;
}

/** INFERRED — `/units`, `/units/{clinicId}`. */
export interface UnitDto {
  id: number;
  clinicId: number;
  name: string | null;
  leadsCount: number | null;
}

/** INFERRED — `/api/config/cloudia-api-key/status`. */
export interface CloudiaKeyStatusDto {
  configured: boolean;
  expiresAt: string | null;
}

/** INFERRED — `/webhooks` (retorno cru da base). Nenhum schema no
 *  contrato; o endpoint devolve a tabela `Leads` inteira. Tipado
 *  conforme colunas conhecidas do modelo. */
export interface WebhookLead {
  id: number;
  externalId: number | null;
  name: string | null;
  phone: string | null;
  email: string | null;
  source: string | null;
  currentStage: string | null;
  conversationState: ConversationState | null;
  tags: string[] | string | null;
  clinicId: number | null;
  unitId: number | null;
  attendantId: number | null;
  attendantName: string | null;
  createdAt: string | null;
  updatedAt: string | null;
  firstAttendanceAt: string | null;
  concludedAt: string | null;
}

/** INFERRED — `/webhooks/source-final`, `/webhooks/etapa-agrupada`.
 *  Mantém `key` canônico e expõe `stage`/`source` como aliases para
 *  componentes legados (StageBarChart, SourceDonut) que leem por nome
 *  semântico. */
export interface GroupCountDto {
  key: string;
  count: number;
  stage: string;
  source: string;
}

/** INFERRED — `/webhooks/origem-cloudia`. */
export interface OrigemAgrupadaDto {
  origem: string;
  quantidade: number;
  porcentagem: number | null;
}

/** INFERRED — `/webhooks/buscar-inicio-fim`, `/webhooks/consulta-periodos`. */
export interface PeriodPointDto {
  periodo: string;
  total: number;
}

// ─── 4. Query-param shapes ─────────────────────────────────────────

export interface BuscarInicioFimParams {
  clinicId?: number;
  dataInicio: string;
  dataFim: string;
}

/**
 * Note: o OpenAPI expõe estes parâmetros em PascalCase
 * (ClinicId, Ano, Mes, Semana, Dia).
 */
export interface ConsultaPeriodosParams {
  ClinicId?: number;
  Ano?: number;
  Mes?: number;
  Semana?: number;
  Dia?: number;
}

export interface ActiveLeadsParams {
  limit?: number;
  unitId?: number;
}

export interface UnitLeadsMetricsParams {
  startDate?: string;
  endDate?: string;
  state?: ConversationState | string;
}

export interface UnitSummaryParams {
  startDate?: string;
  endDate?: string;
}

export interface MonthlyReportParams {
  clinicId: number | string;
  mes: number;
  ano: number;
}

export interface DailyReportParams {
  tenantId: number | string;
  date: string;
}

export interface MetricsDashboardParams {
  clinicId?: number;
  attendantType?: string;
}
