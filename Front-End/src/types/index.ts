export type EntityId = number;
export type ConversationState = "bot" | "queue" | "service" | "concluido";

export interface ProblemDetails {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
  [key: string]: unknown;
}

/** Swagger: ActiveLeadDto */
export interface ActiveLeadDto {
  id: number;
  externalId?: number;
  name?: string | null;
  phone?: string | null;
  conversationState?: string | null;
  attendantId?: number | null;
  unitId?: number | null;
  updatedAt: string;
  createdAt: string;
}

/** Swagger: LeadsCountDto */
export interface LeadsCountDto {
  bot: number;
  queue: number;
  service: number;
  concluido: number;
  total: number;
}

/** Swagger: SyncLeadDto */
export interface SyncLeadDto {
  externalId: number;
  tags?: string[] | null;
  name?: string | null;
  phone?: string | null;
  stage?: string | null;
  tenantId: number;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface Lead {
  id: number;
  externalId?: number;
  name?: string | null;
  phone?: string | null;
  email?: string | null;
  source?: string | null;
  currentStage?: string | null;
  conversationState?: ConversationState | null;
  tags?: string[] | string | null;
  clinicId?: number | null;
  unitId?: number | null;
  attendantId?: number | null;
  attendantName?: string | null;
  createdAt?: string;
  updatedAt?: string;
  firstAttendanceAt?: string | null;
  concludedAt?: string | null;
  [key: string]: unknown;
}

export interface StageCount {
  stage: string;
  count: number;
}

export interface SourceCount {
  source: string;
  count: number;
}

export interface OrigemAgrupada {
  origem: string;
  quantidade: number;
  porcentagem?: number;
}

export interface StateCount extends LeadsCountDto {}

export interface TimeSeriesPoint {
  periodo: string;
  total: number;
}

export interface LeadMetrics {
  leadId: number;
  name?: string | null;
  currentState?: ConversationState;
  timeInBot?: number;
  timeInQueue?: number;
  timeInService?: number;
  totalTime?: number;
  timeToFirstAttendance?: number | null;
  timeToResolution?: number | null;
  alerts?: string[];
  transitions?: Array<{
    from: ConversationState;
    to: ConversationState;
    at: string;
  }>;
  interactions?: Array<{
    id: string;
    type: string;
    content?: string;
    at: string;
  }>;
  [key: string]: unknown;
}

export interface UnitSummary {
  totalLeads: number;
  totals: Partial<Record<ConversationState, number>>;
  averages: Partial<Record<ConversationState, number>> & {
    firstAttendance?: number;
    resolution?: number;
  };
  alertsCount?: number;
  topAttendants?: Array<{
    attendantName: string;
    conversions: number;
    total: number;
  }>;
  [key: string]: unknown;
}

export interface Attendant {
  id: number;
  name: string;
  email?: string | null;
  totalAssignments?: number;
  conversions?: number;
}

export interface AttendantRanking {
  attendantId: number;
  name: string;
  total: number;
  conversions?: number;
}

export interface Unit {
  id: number | string;
  clinicId: number | string;
  logo_url?: string | null;
  name?: string | null;
  leadsCount?: number;
}

export interface LiveMetrics {
  atendentes?: Array<{
    name: string;
    status?: string;
    emAtendimento?: number;
    naFila?: number;
    tempoMedio?: number;
  }>;
  fila?: Array<{
    name: string;
    phone?: string;
    waitingSince?: string;
    waitingMinutes?: number;
  }>;
  totalEmAtendimento?: number;
  totalNaFila?: number;
  tempoMedio?: number;
  [key: string]: unknown;
}

export interface ApiCountPayload {
  count?: number;
  total?: number;
  quantidade?: number;
}
