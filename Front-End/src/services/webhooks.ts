import { api } from "@/lib/api";
import { asArray, cleanParams, toInt } from "@/lib/http";
import type {
  ActiveLeadDto,
  ApiCountPayload,
  Lead,
  LeadsCountDto,
  OrigemAgrupada,
  StageCount,
  TimeSeriesPoint,
} from "@/types";

export interface LeadFilters {
  clinicId?: number | string;
  search?: string;
  stage?: string;
  state?: string;
  source?: string;
  startDate?: string;
  endDate?: string;
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDir?: "asc" | "desc";
}

export interface ConsultaPeriodosParams {
  ClinicId?: number | string;
  Ano?: number;
  Mes?: number;
  Semana?: number;
  Dia?: number;
}

function extractCount(data: ApiCountPayload | number | null | undefined): number {
  if (typeof data === "number") return data;
  if (!data) return 0;
  return data.count ?? data.total ?? data.quantidade ?? 0;
}

function normalizeSourceGroup(data: unknown): Array<{ source: string; count: number }> {
  return asArray<Record<string, unknown>>(data).map((item) => ({
    source: String(item.source ?? item.origem ?? item.etapa ?? item.name ?? "—"),
    count: Number(item.count ?? item.quantidade ?? item.total ?? 0),
  }));
}

function normalizeStageGroup(data: unknown): StageCount[] {
  return asArray<Record<string, unknown>>(data).map((item) => ({
    stage: String(item.stage ?? item.etapa ?? item.name ?? "—"),
    count: Number(item.count ?? item.quantidade ?? item.total ?? 0),
  }));
}

function normalizeSeries(data: unknown): TimeSeriesPoint[] {
  return asArray<Record<string, unknown>>(data).map((item) => ({
    periodo: String(item.periodo ?? item.mes ?? item.data ?? item.label ?? "—"),
    total: Number(item.total ?? item.count ?? item.quantidade ?? 0),
  }));
}

export const webhooksService = {
  async listLeads(filters: LeadFilters = {}): Promise<Lead[]> {
    const { data } = await api.get<Lead[]>("/webhooks", {
      params: cleanParams({
        ...filters,
        unitId: toInt(filters.clinicId),
      }),
    });

    return asArray<Lead>(data);
  },

  async consultas(clinicId?: number | string): Promise<number> {
    const { data } = await api.get<ApiCountPayload | number>("/webhooks/consultas", {
      params: cleanParams({ clinicId: toInt(clinicId) }),
    });
    return extractCount(data);
  },

  async semPagamento(clinicId?: number | string): Promise<number> {
    const { data } = await api.get<ApiCountPayload | number>("/webhooks/sem-pagamento", {
      params: cleanParams({ clinicId: toInt(clinicId) }),
    });
    return extractCount(data);
  },

  async comPagamento(clinicId?: number | string): Promise<number> {
    const { data } = await api.get<ApiCountPayload | number>("/webhooks/com-pagamento", {
      params: cleanParams({ clinicId: toInt(clinicId) }),
    });
    return extractCount(data);
  },

  async sourceFinal(clinicId?: number | string): Promise<Array<{ source: string; count: number }>> {
    const { data } = await api.get<unknown>("/webhooks/source-final", {
      params: cleanParams({ clinicId: toInt(clinicId) }),
    });

    return normalizeSourceGroup(data);
  },

  async origemCloudia(clinicId?: number | string): Promise<OrigemAgrupada[]> {
    const { data } = await api.get<OrigemAgrupada[]>("/webhooks/origem-cloudia", {
      params: cleanParams({ clinicId: toInt(clinicId) }),
    });

    return asArray<OrigemAgrupada>(data);
  },

  async fimDeSemana(clinicId?: number | string): Promise<Lead[]> {
    const { data } = await api.get<Lead[]>("/webhooks/fim-de-semana", {
      params: cleanParams({ clinicId: toInt(clinicId) }),
    });

    return asArray<Lead>(data);
  },

  async etapaAgrupada(clinicId?: number | string): Promise<StageCount[]> {
    const { data } = await api.get<unknown>("/webhooks/etapa-agrupada", {
      params: cleanParams({ clinicId: toInt(clinicId) }),
    });

    return normalizeStageGroup(data);
  },

  async buscarInicioFim(params: {
    clinicId?: number | string;
    dataInicio?: string;
    dataFim?: string;
  }): Promise<TimeSeriesPoint[]> {
    const { data } = await api.get<unknown>("/webhooks/buscar-inicio-fim", {
      params: cleanParams({
        clinicId: toInt(params.clinicId),
        dataInicio: params.dataInicio,
        dataFim: params.dataFim,
      }),
    });

    return normalizeSeries(data);
  },

  /**
   * Swagger exige query params com inicial maiúscula: ClinicId, Ano, Mes, Semana, Dia.
   */
  async consultaPeriodos(params: ConsultaPeriodosParams): Promise<TimeSeriesPoint[]> {
    const { data } = await api.get<unknown>("/webhooks/consulta-periodos", {
      params: cleanParams({
        ClinicId: toInt(params.ClinicId),
        Ano: params.Ano,
        Mes: params.Mes,
        Semana: params.Semana,
        Dia: params.Dia,
      }),
    });

    return normalizeSeries(data);
  },

  async activeLeads(params: { limit?: number; unitId?: number | string } = {}): Promise<ActiveLeadDto[]> {
    const { data } = await api.get<ActiveLeadDto[]>("/webhooks/active", {
      params: cleanParams({
        limit: params.limit,
        unitId: toInt(params.unitId),
      }),
    });

    return asArray<ActiveLeadDto>(data);
  },

  async countByState(unitId?: number | string, tenantId?: number | string): Promise<LeadsCountDto> {
    const { data } = await api.get<LeadsCountDto>("/webhooks/count-by-state", {
      params: cleanParams({ unitId: toInt(unitId), tenantId: toInt(tenantId) }),
    });

    return data ?? { bot: 0, queue: 0, service: 0, concluido: 0, total: 0 };
  },

  async syncHealth(): Promise<unknown> {
    const { data } = await api.get<unknown>("/webhooks/sync/health");
    return data;
  },

  async getTotalLeads(tenantId: number): Promise<number> {
  const { data } = await api.get<{ total: number }>("/webhooks/total-leads", {
    params: { clinicId: tenantId },
  });

  return data.total;
  }
};
