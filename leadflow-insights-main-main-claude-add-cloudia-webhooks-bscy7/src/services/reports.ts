import { useClinic } from "@/hooks/useClinic";
import { api } from "@/lib/api";
import { toInt } from "@/lib/http";

export interface MonthlyReportParams {
  clinicId: number | string;
  mes: number;
  ano: number;
}

export interface DailyReportParams {
  tenantId: number | string;
  date: string;
}

export const reportsService = {
  async monthly(params: MonthlyReportParams): Promise<void> {
    const unitId = useClinic((s) => s.unitId);
    console.log("CLINIC ID:", unitId);
    if (!unitId) throw new Error("clinicId inválido para /api/relatorios/mensal");

    const res = await api.get<Blob>("/api/relatorios/mensal", {
      params: { clinicId: unitId, mes: params.mes, ano: params.ano },
      responseType: "blob",
    });

    const blob = new Blob([res.data], { type: "application/pdf" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = `relatorio-${params.ano}-${String(params.mes).padStart(2, "0")}.pdf`;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  },

  async daily(params: DailyReportParams): Promise<unknown> {
    const tenantId = toInt(params.tenantId);
    if (!tenantId) throw new Error("tenantId inválido para /daily-relatory/generate");

    const { data } = await api.get<unknown>("/daily-relatory/generate", {
      params: { tenantId, date: params.date },
    });

    return data;
  },
};
