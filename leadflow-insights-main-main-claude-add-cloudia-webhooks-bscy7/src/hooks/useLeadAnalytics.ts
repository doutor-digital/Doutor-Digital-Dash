import { useMemo } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { analyticsService } from "@/services/analytics";
import { assignmentsService } from "@/services/assignments";
import { reportsService } from "@/services/reports";
import { webhooksService } from "@/services/webhooks";
import { toInt } from "@/lib/http";

export function useUnitSummary(unitId?: number | string, startDate?: string, endDate?: string) {
  const parsedUnitId = toInt(unitId);
  return useQuery({
    queryKey: ["analytics", "unit-summary", parsedUnitId, startDate, endDate],
    queryFn: () => analyticsService.unitSummary(parsedUnitId!, { startDate, endDate }),
    enabled: Boolean(parsedUnitId),
  });
}

export function useUnitLeadsMetrics(
  unitId?: number | string,
  filters: { startDate?: string; endDate?: string; state?: string } = {}
) {
  const parsedUnitId = toInt(unitId);
  return useQuery({
    queryKey: ["analytics", "unit-leads-metrics", parsedUnitId, filters],
    queryFn: () => analyticsService.unitLeadsMetrics(parsedUnitId!, filters),
    enabled: Boolean(parsedUnitId),
  });
}

export function useTodayDashboard(unitId?: number | string) {
  const parsedUnitId = toInt(unitId);
  return useQuery({
    queryKey: ["analytics", "today-dashboard", parsedUnitId],
    queryFn: () => analyticsService.unitDashboardToday(parsedUnitId!),
    enabled: Boolean(parsedUnitId),
  });
}

export function useActiveLeads(unitId?: number | string, limit = 100) {
  const parsedUnitId = toInt(unitId);
  return useQuery({
    queryKey: ["webhooks", "active", parsedUnitId, limit],
    queryFn: () => webhooksService.activeLeads({ unitId: parsedUnitId, limit }),
    enabled: Boolean(parsedUnitId),
  });
}

export function useCountByState(unitId?: number | string) {
  const parsedUnitId = toInt(unitId);
  return useQuery({
    queryKey: ["webhooks", "count-by-state", parsedUnitId],
    queryFn: () => webhooksService.countByState(parsedUnitId),
    enabled: Boolean(parsedUnitId),
  });
}

export function useAssignmentsRanking(clinicId?: number | string) {
  const parsedClinicId = toInt(clinicId);
  return useQuery({
    queryKey: ["assignments", "ranking", parsedClinicId],
    queryFn: () => assignmentsService.ranking(parsedClinicId),
    enabled: Boolean(parsedClinicId),
  });
}

export function useMonthlyReportDownload() {
  return useMutation({
    mutationFn: (params: { clinicId: number | string; mes: number; ano: number }) =>
      reportsService.monthly(params),
  });
}

export function useOperationalKpis(unitId?: number | string) {
  const activeQuery = useActiveLeads(unitId, 100);
  const stateQuery = useCountByState(unitId);

  const kpis = useMemo(() => {
    const leads = activeQuery.data ?? [];
    const states = stateQuery.data;
    const total = states?.total ?? 0;
    const semResponsavel = leads.filter((lead) => !lead.attendantId).length;

    const queueAgesMinutes = leads
      .filter((lead) => lead.conversationState === "queue")
      .map((lead) => {
        const base = new Date(lead.updatedAt ?? lead.createdAt).getTime();
        return Math.max(0, (Date.now() - base) / 60_000);
      });

    const criticos = queueAgesMinutes.filter((m) => m > 30).length;
    const atencao = queueAgesMinutes.filter((m) => m > 10 && m <= 30).length;
    const recente = queueAgesMinutes.filter((m) => m <= 10).length;

    /**
     * KPI: taxa de backlog
     * Fórmula: queue / total
     * Endpoints: /webhooks/count-by-state
     * Limitação: total depende da atualização do agregador do backend.
     */
    const backlogRate = total > 0 ? (states?.queue ?? 0) / total : 0;

    /**
     * KPI: taxa de leads sem responsável
     * Fórmula: leads ativos sem attendantId / leads ativos
     * Endpoints: /webhooks/active
     * Limitação: considera apenas ativos retornados pelo endpoint, não a base completa.
     */
    const unassignedRate = leads.length > 0 ? semResponsavel / leads.length : 0;

    /**
     * KPI: score operacional da unidade (0-100)
     * Fórmula: 100 - (backlogRate*45 + unassignedRate*35 + críticoQueueRate*20) * 100
     * Endpoints: /webhooks/count-by-state + /webhooks/active
     * Limitação: score heurístico para priorização operacional, não métrica financeira.
     */
    const criticalQueueRate = queueAgesMinutes.length > 0 ? criticos / queueAgesMinutes.length : 0;
    const operationalScore = Math.max(
      0,
      Math.round(100 - (backlogRate * 45 + unassignedRate * 35 + criticalQueueRate * 20) * 100)
    );

    return {
      backlogRate,
      unassignedRate,
      operationalScore,
      queueAgingBands: { recente, atencao, criticos },
    };
  }, [activeQuery.data, stateQuery.data]);

  return { ...kpis, activeQuery, stateQuery };
}
