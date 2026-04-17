/**
 * Adapters puros — normalizam responses crus em DTOs tipados consumíveis
 * pelo front. Mantêm o restante da aplicação livre de `any` e de
 * verificações defensivas espalhadas.
 */

import type {
  ActiveLeadDto,
  AttendantDto,
  AttendantRankingDto,
  ConversationState,
  GroupCountDto,
  LeadAssignmentHistoryDto,
  LeadsCountDto,
  LeadMetricsDto,
  LiveAttendantDto,
  LiveMetricsDto,
  LiveQueueItemDto,
  OrigemAgrupadaDto,
  PeriodPointDto,
  UnitDto,
  UnitDashboardTodayDto,
  UnitSummaryDto,
  WebhookLead,
} from "@/api/types";

type JsonRecord = Record<string, unknown>;

function isRecord(v: unknown): v is JsonRecord {
  return typeof v === "object" && v !== null && !Array.isArray(v);
}

function str(v: unknown): string | null {
  return typeof v === "string" ? v : v == null ? null : String(v);
}

function num(v: unknown): number | null {
  if (typeof v === "number" && Number.isFinite(v)) return v;
  if (typeof v === "string" && v.trim() !== "" && !Number.isNaN(Number(v)))
    return Number(v);
  return null;
}

function numOrZero(v: unknown): number {
  return num(v) ?? 0;
}

function boolOr(v: unknown, fallback: boolean): boolean {
  return typeof v === "boolean" ? v : fallback;
}

function stringArr(v: unknown): string[] {
  if (Array.isArray(v)) return v.filter((i): i is string => typeof i === "string");
  return [];
}

function asConversationState(v: unknown): ConversationState | null {
  return v === "bot" || v === "queue" || v === "service" || v === "concluido"
    ? v
    : null;
}

export function normalizeActiveLead(raw: unknown): ActiveLeadDto {
  const r = isRecord(raw) ? raw : {};
  return {
    id: numOrZero(r.id),
    externalId: numOrZero(r.externalId),
    name: str(r.name),
    phone: str(r.phone),
    conversationState: str(r.conversationState),
    attendantId: num(r.attendantId),
    unitId: num(r.unitId),
    updatedAt: str(r.updatedAt) ?? "",
    createdAt: str(r.createdAt) ?? "",
  };
}

export function normalizeActiveLeadList(raw: unknown): ActiveLeadDto[] {
  return Array.isArray(raw) ? raw.map(normalizeActiveLead) : [];
}

export function normalizeLeadsCount(raw: unknown): LeadsCountDto {
  const r = isRecord(raw) ? raw : {};
  return {
    bot: numOrZero(r.bot),
    queue: numOrZero(r.queue),
    service: numOrZero(r.service),
    concluido: numOrZero(r.concluido),
    total: numOrZero(r.total),
  };
}

export function normalizeWebhookLead(raw: unknown): WebhookLead {
  const r = isRecord(raw) ? raw : {};
  return {
    id: numOrZero(r.id),
    externalId: num(r.externalId),
    name: str(r.name),
    phone: str(r.phone),
    email: str(r.email),
    source: str(r.source),
    currentStage: str(r.currentStage),
    conversationState: asConversationState(r.conversationState),
    tags: parseTags(r.tags),
    clinicId: num(r.clinicId),
    unitId: num(r.unitId),
    attendantId: num(r.attendantId),
    attendantName: str(r.attendantName),
    createdAt: str(r.createdAt),
    updatedAt: str(r.updatedAt),
    firstAttendanceAt: str(r.firstAttendanceAt),
    concludedAt: str(r.concludedAt),
  };
}

export function normalizeWebhookLeadList(raw: unknown): WebhookLead[] {
  return Array.isArray(raw) ? raw.map(normalizeWebhookLead) : [];
}

function parseTags(raw: unknown): string[] | null {
  if (raw == null) return null;
  if (Array.isArray(raw))
    return raw.filter((t): t is string => typeof t === "string");
  if (typeof raw === "string") {
    const trimmed = raw.trim();
    if (trimmed.startsWith("[")) {
      try {
        const arr = JSON.parse(trimmed);
        return Array.isArray(arr)
          ? arr.filter((t): t is string => typeof t === "string")
          : null;
      } catch {
        return trimmed.split(",").map((s) => s.trim()).filter(Boolean);
      }
    }
    return trimmed ? trimmed.split(",").map((s) => s.trim()) : null;
  }
  return null;
}

/** Normaliza `/webhooks/source-final` e `/webhooks/etapa-agrupada`. */
export function normalizeGroupCount(
  raw: unknown,
  preferredKey: "source" | "stage" | "origem" | "etapa"
): GroupCountDto[] {
  if (!Array.isArray(raw)) return [];
  return raw.map((item) => {
    const r = isRecord(item) ? item : {};
    const key =
      str(r[preferredKey]) ??
      str(r.origem) ??
      str(r.etapa) ??
      str(r.source) ??
      str(r.stage) ??
      str(r.name) ??
      "—";
    return {
      key,
      count: numOrZero(r.count ?? r.quantidade ?? r.total),
      stage: key,
      source: key,
    };
  });
}

export function normalizeOrigemAgrupada(raw: unknown): OrigemAgrupadaDto[] {
  if (!Array.isArray(raw)) return [];
  return raw.map((item) => {
    const r = isRecord(item) ? item : {};
    return {
      origem: str(r.origem) ?? str(r.source) ?? "—",
      quantidade: numOrZero(r.quantidade ?? r.count ?? r.total),
      porcentagem: num(r.porcentagem),
    };
  });
}

export function normalizePeriodSeries(raw: unknown): PeriodPointDto[] {
  if (!Array.isArray(raw)) return [];
  return raw
    .map((item) => {
      const r = isRecord(item) ? item : {};
      return {
        periodo:
          str(r.periodo) ??
          str(r.mes) ??
          str(r.data) ??
          str(r.label) ??
          str(r.dia) ??
          "",
        total: numOrZero(r.total ?? r.count ?? r.quantidade),
      };
    })
    .filter((p) => p.periodo !== "");
}

export function extractCountFromResponse(raw: unknown): number {
  if (typeof raw === "number") return raw;
  if (isRecord(raw)) {
    return numOrZero(raw.count ?? raw.total ?? raw.quantidade);
  }
  return 0;
}

export function normalizeAttendant(raw: unknown): AttendantDto {
  const r = isRecord(raw) ? raw : {};
  return {
    id: numOrZero(r.id),
    name: str(r.name) ?? "—",
    email: str(r.email),
    totalAssignments: num(r.totalAssignments ?? r.total),
    conversions: num(r.conversions),
  };
}

export function normalizeAttendantList(raw: unknown): AttendantDto[] {
  return Array.isArray(raw) ? raw.map(normalizeAttendant) : [];
}

export function normalizeRanking(raw: unknown): AttendantRankingDto[] {
  if (!Array.isArray(raw)) return [];
  return raw.map((item) => {
    const r = isRecord(item) ? item : {};
    return {
      attendantId: numOrZero(r.attendantId ?? r.id),
      name: str(r.name) ?? "—",
      total: numOrZero(r.total ?? r.totalAssignments),
      conversions: num(r.conversions),
    };
  });
}

export function normalizeAssignmentHistory(
  raw: unknown
): LeadAssignmentHistoryDto[] {
  if (!Array.isArray(raw)) return [];
  return raw.map((item) => {
    const r = isRecord(item) ? item : {};
    return {
      attendantId: numOrZero(r.attendantId),
      attendantName: str(r.attendantName) ?? "—",
      assignedAt: str(r.assignedAt ?? r.createdAt) ?? "",
      unassignedAt: str(r.unassignedAt),
    };
  });
}

export function normalizeUnit(raw: unknown): UnitDto {
  const r = isRecord(raw) ? raw : {};
  return {
    id: numOrZero(r.id),
    clinicId: numOrZero(r.clinicId),
    name: str(r.name),
    leadsCount: num(r.leadsCount ?? r.total),
  };
}

export function normalizeUnitList(raw: unknown): UnitDto[] {
  return Array.isArray(raw) ? raw.map(normalizeUnit) : [];
}

export function normalizeUnitSummary(raw: unknown): UnitSummaryDto {
  const r = isRecord(raw) ? raw : {};
  const totalsRaw = isRecord(r.totals) ? r.totals : {};
  const avgRaw = isRecord(r.averages) ? r.averages : {};

  const pickState = (src: JsonRecord, k: ConversationState) => num(src[k]) ?? undefined;

  return {
    totalLeads: numOrZero(r.totalLeads ?? r.total),
    totals: {
      bot: pickState(totalsRaw, "bot"),
      queue: pickState(totalsRaw, "queue"),
      service: pickState(totalsRaw, "service"),
      concluido: pickState(totalsRaw, "concluido"),
    },
    averages: {
      bot: pickState(avgRaw, "bot"),
      queue: pickState(avgRaw, "queue"),
      service: pickState(avgRaw, "service"),
      firstAttendance: num(avgRaw.firstAttendance) ?? undefined,
      resolution: num(avgRaw.resolution) ?? undefined,
    },
    alertsCount: numOrZero(r.alertsCount),
    topAttendants: Array.isArray(r.topAttendants)
      ? r.topAttendants.map((a) => {
          const ar = isRecord(a) ? a : {};
          return {
            attendantId: numOrZero(ar.attendantId),
            attendantName: str(ar.attendantName) ?? "—",
            total: numOrZero(ar.total),
            conversions: numOrZero(ar.conversions),
          };
        })
      : [],
  };
}

export function normalizeLeadMetrics(raw: unknown): LeadMetricsDto {
  const r = isRecord(raw) ? raw : {};
  return {
    leadId: numOrZero(r.leadId ?? r.id),
    name: str(r.name),
    currentState: asConversationState(r.currentState),
    timeInBot: num(r.timeInBot),
    timeInQueue: num(r.timeInQueue),
    timeInService: num(r.timeInService),
    totalTime: num(r.totalTime),
    timeToFirstAttendance: num(r.timeToFirstAttendance),
    timeToResolution: num(r.timeToResolution),
    alerts: stringArr(r.alerts),
    transitions: Array.isArray(r.transitions)
      ? r.transitions.flatMap((t) => {
          const tr = isRecord(t) ? t : {};
          const from = asConversationState(tr.from);
          const to = asConversationState(tr.to);
          if (!from || !to) return [];
          return [{ from, to, at: str(tr.at) ?? "" }];
        })
      : [],
    interactions: Array.isArray(r.interactions)
      ? r.interactions.map((i) => {
          const ir = isRecord(i) ? i : {};
          const id = ir.id;
          return {
            id:
              typeof id === "number"
                ? id
                : typeof id === "string"
                ? id
                : "",
            type: str(ir.type) ?? "—",
            content: str(ir.content),
            at: str(ir.at) ?? "",
          };
        })
      : [],
  };
}

export function normalizeLeadMetricsList(raw: unknown): LeadMetricsDto[] {
  return Array.isArray(raw) ? raw.map(normalizeLeadMetrics) : [];
}

export function normalizeUnitDashboardToday(
  raw: unknown
): UnitDashboardTodayDto {
  const r = isRecord(raw) ? raw : {};
  return {
    summary: normalizeUnitSummary(r.summary),
    alerts: normalizeLeadMetricsList(r.alerts),
    topAttendants: Array.isArray(r.topAttendants)
      ? r.topAttendants.map((a) => {
          const ar = isRecord(a) ? a : {};
          return {
            attendantId: numOrZero(ar.attendantId),
            attendantName: str(ar.attendantName ?? ar.name) ?? "—",
            total: numOrZero(ar.total),
            conversions: numOrZero(ar.conversions),
          };
        })
      : [],
  };
}

/** Normaliza payload mutável de /metrics/* em shape estável. */
export function normalizeLiveMetrics(raw: unknown): LiveMetricsDto {
  const r = isRecord(raw) ? raw : {};
  const atendentesRaw = Array.isArray(r.atendentes) ? r.atendentes : [];
  const filaRaw = Array.isArray(r.fila) ? r.fila : [];

  const atendentes: LiveAttendantDto[] = atendentesRaw.map((a) => {
    const ar = isRecord(a) ? a : {};
    return {
      name: str(ar.name) ?? "—",
      status: str(ar.status),
      emAtendimento: numOrZero(ar.emAtendimento),
      naFila: numOrZero(ar.naFila),
      tempoMedio: num(ar.tempoMedio),
    };
  });

  const fila: LiveQueueItemDto[] = filaRaw.map((q) => {
    const qr = isRecord(q) ? q : {};
    return {
      name: str(qr.name) ?? "—",
      phone: str(qr.phone),
      waitingSince: str(qr.waitingSince),
      waitingMinutes: num(qr.waitingMinutes),
    };
  });

  return {
    atendentes,
    fila,
    totalEmAtendimento:
      numOrZero(r.totalEmAtendimento) ||
      atendentes.reduce((acc, a) => acc + a.emAtendimento, 0),
    totalNaFila: numOrZero(r.totalNaFila) || fila.length,
    tempoMedio: num(r.tempoMedio),
    raw,
  };
}

/** Status da chave Cloudia. */
export function normalizeCloudiaStatus(
  raw: unknown
): { configured: boolean; expiresAt: string | null } {
  const r = isRecord(raw) ? raw : {};
  return {
    configured: boolOr(r.configured, false),
    expiresAt: str(r.expiresAt),
  };
}
