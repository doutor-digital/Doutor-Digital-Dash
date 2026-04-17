import { api } from "@/lib/api";
import { asArray, cleanParams, toInt } from "@/lib/http";
import type { ApiCountPayload, Unit } from "@/types";

export const unitsService = {
  async list(): Promise<Unit[]> {
    const { data } = await api.get<Unit[]>("/units");
    return asArray<Unit>(data);
  },

  async getOrCreate(clinicId: number | string): Promise<Unit> {
    const id = toInt(clinicId);
    if (!id) throw new Error("clinicId inválido para /units/{clinicId}");

    const { data } = await api.get<Unit>(`/units/${id}`);
    return data;
  },

  /**
   * Swagger define body como string simples para PUT /units/{clinicId}.
   */
  async updateName(clinicId: number | string, name: string): Promise<Unit> {
    const id = toInt(clinicId);
    if (!id) throw new Error("clinicId inválido para /units/{clinicId}");

    const { data } = await api.put<Unit>(`/units/${id}`, name, {
      headers: { "Content-Type": "application/json" },
    });

    return data;
  },

  async quantityLeads(clinicId?: number | string): Promise<number> {
    const { data } = await api.get<ApiCountPayload | number>("/units/quantity-leads", {
      params: cleanParams({ clinicId: toInt(clinicId) }),
    });

    if (typeof data === "number") return data;
    return data?.total ?? data?.count ?? 0;
  },
};
