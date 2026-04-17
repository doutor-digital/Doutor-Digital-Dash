/**
 * Relatórios — rotas mistas.
 *
 * OpenAPI:
 *  - GET /api/relatorios/mensal?clinicId=<int>&mes=<int>&ano=<int>
 *      Retorna `binary`. Download é responsabilidade do caller —
 *      o service expõe apenas o Blob + filename sugerido.
 *  - GET /daily-relatory/generate?tenantId=<int>&date=<date-time>
 *      Sem schema formal. Tratamos como JSON arbitrário.
 */

import { api } from "@/lib/api";
import { cleanParams, toIsoDateTime, toNumberOrUndef } from "@/api/params";
import type { DailyReportParams, MonthlyReportParams } from "@/api/types";

export interface MonthlyReportBlob {
  blob: Blob;
  filename: string;
}

export const relatoriosService = {
  async downloadMonthly(
    params: MonthlyReportParams
  ): Promise<MonthlyReportBlob> {
    const res = await api.get<Blob>("/api/relatorios/mensal", {
      params: cleanParams({
        clinicId: toNumberOrUndef(params.clinicId),
        mes: params.mes,
        ano: params.ano,
      }),
      responseType: "blob",
    });
    const blob = new Blob([res.data], { type: "application/pdf" });
    const filename = `relatorio-${params.ano}-${String(params.mes).padStart(
      2,
      "0"
    )}.pdf`;
    return { blob, filename };
  },

  async getDaily(params: DailyReportParams): Promise<unknown> {
    const { data } = await api.get<unknown>("/daily-relatory/generate", {
      params: cleanParams({
        tenantId: toNumberOrUndef(params.tenantId),
        date: toIsoDateTime(params.date),
      }),
    });
    return data;
  },
};

/** Helper utilitário para disparar o download no browser. */
export function downloadBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
