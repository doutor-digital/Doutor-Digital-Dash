import { api } from "@/lib/api";
import { cleanParams, toInt } from "@/lib/http";
import type { LiveMetrics } from "@/types";

export interface DashboardMetricsParams {
  clinicId?: number | string;
  attendantType?: string;
}

export const metricsService = {
  async dashboard(params: DashboardMetricsParams): Promise<LiveMetrics> {
    const { data } = await api.get<LiveMetrics>("/metrics/dashboard", {
      params: cleanParams({
        clinicId: toInt(params.clinicId),
        attendantType: params.attendantType,
      }),
    });

    return data ?? {};
  },

  async resumo(clinicId?: number | string): Promise<LiveMetrics> {
    const { data } = await api.get<LiveMetrics>("/metrics/resumo", {
      params: cleanParams({ clinicId: toInt(clinicId) }),
    });

    return data ?? {};
  },

  async fila(clinicId?: number | string): Promise<LiveMetrics> {
    const { data } = await api.get<LiveMetrics>("/metrics/fila", {
      params: cleanParams({ clinicId: toInt(clinicId) }),
    });

    return data ?? {};
  },

  async completo(clinicId?: number | string): Promise<LiveMetrics> {
    const { data } = await api.get<LiveMetrics>("/metrics/completo", {
      params: cleanParams({ clinicId: toInt(clinicId) }),
    });

    return data ?? {};
  },
};
