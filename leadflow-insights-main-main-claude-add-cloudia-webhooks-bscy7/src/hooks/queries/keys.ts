/**
 * Query keys centralizadas — garante cache keys consistentes entre
 * hooks consumidores e mutações que precisam invalidar. Cada key é uma
 * tupla literal para que o TanStack Query infira tipos corretamente.
 */

import type {
  ActiveLeadsParams,
  BuscarInicioFimParams,
  ConsultaPeriodosParams,
  UnitLeadsMetricsParams,
  UnitSummaryParams,
} from "@/api/types";

export const qk = {
  webhooks: {
    all: () => ["webhooks"] as const,
    listAll: () => ["webhooks", "list-all"] as const,
    consultas: (clinicId?: number) =>
      ["webhooks", "consultas", clinicId ?? null] as const,
    withPayment: (clinicId?: number) =>
      ["webhooks", "com-pagamento", clinicId ?? null] as const,
    withoutPayment: (clinicId?: number) =>
      ["webhooks", "sem-pagamento", clinicId ?? null] as const,
    sourceFinal: (clinicId?: number) =>
      ["webhooks", "source-final", clinicId ?? null] as const,
    origemCloudia: (clinicId?: number) =>
      ["webhooks", "origem-cloudia", clinicId ?? null] as const,
    weekend: (clinicId?: number) =>
      ["webhooks", "fim-de-semana", clinicId ?? null] as const,
    stageGrouped: (clinicId?: number) =>
      ["webhooks", "etapa-agrupada", clinicId ?? null] as const,
    range: (p: BuscarInicioFimParams) =>
      [
        "webhooks",
        "buscar-inicio-fim",
        p.clinicId ?? null,
        p.dataInicio,
        p.dataFim,
      ] as const,
    period: (p: ConsultaPeriodosParams) =>
      [
        "webhooks",
        "consulta-periodos",
        p.ClinicId ?? null,
        p.Ano ?? null,
        p.Mes ?? null,
        p.Semana ?? null,
        p.Dia ?? null,
      ] as const,
    active: (p: ActiveLeadsParams) =>
      ["webhooks", "active", p.limit ?? null, p.unitId ?? null] as const,
    countByState: (unitId?: number) =>
      ["webhooks", "count-by-state", unitId ?? null] as const,
    syncHealth: () => ["webhooks", "sync", "health"] as const,
  },
  metrics: {
    dashboard: (clinicId?: number, attendantType?: string) =>
      ["metrics", "dashboard", clinicId ?? null, attendantType ?? "HUMAN"] as const,
    resumo: (clinicId?: number) =>
      ["metrics", "resumo", clinicId ?? null] as const,
    fila: (clinicId?: number) => ["metrics", "fila", clinicId ?? null] as const,
    completo: (clinicId?: number) =>
      ["metrics", "completo", clinicId ?? null] as const,
  },
  analytics: {
    leadMetrics: (id: number) => ["analytics", "lead", id, "metrics"] as const,
    unitLeadsMetrics: (unitId: number, p: UnitLeadsMetricsParams) =>
      [
        "analytics",
        "unit",
        unitId,
        "leads-metrics",
        p.startDate ?? null,
        p.endDate ?? null,
        p.state ?? null,
      ] as const,
    unitSummary: (unitId: number, p: UnitSummaryParams) =>
      [
        "analytics",
        "unit",
        unitId,
        "summary",
        p.startDate ?? null,
        p.endDate ?? null,
      ] as const,
    unitAlerts: (unitId: number) =>
      ["analytics", "unit", unitId, "alerts"] as const,
    unitDashboardToday: (unitId: number) =>
      ["analytics", "unit", unitId, "dashboard", "today"] as const,
  },
  assignments: {
    attendants: () => ["assignments", "attendants"] as const,
    ranking: (clinicId?: number) =>
      ["assignments", "ranking", clinicId ?? null] as const,
    leadHistory: (externalLeadId: number, clinicId?: number) =>
      ["assignments", "lead", externalLeadId, clinicId ?? null] as const,
  },
  units: {
    all: () => ["units"] as const,
    byClinic: (clinicId: number) => ["units", clinicId] as const,
    quantityLeads: (clinicId?: number) =>
      ["units", "quantity-leads", clinicId ?? null] as const,
  },
  configuration: {
    cloudiaStatus: () => ["configuration", "cloudia-status"] as const,
  },
  relatorios: {
    daily: (tenantId: number, date: string) =>
      ["relatorios", "daily", tenantId, date] as const,
  },
} as const;
