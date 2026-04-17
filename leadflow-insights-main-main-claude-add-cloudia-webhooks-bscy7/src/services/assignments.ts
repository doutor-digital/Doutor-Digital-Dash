import { api } from "@/lib/api";
import { asArray, cleanParams, toInt } from "@/lib/http";
import type { Attendant, AttendantRanking, SyncLeadDto } from "@/types";

export interface AssignmentLeadHistoryItem {
  timestamp?: string;
  fromState?: string;
  toState?: string;
  attendantName?: string;
  [key: string]: unknown;
}

export const assignmentsService = {
  async listAttendants(): Promise<Attendant[]> {
    const { data } = await api.get<Attendant[]>("/assignments/attendants");
    return asArray<Attendant>(data);
  },

  async leadHistory(
    externalLeadId: number | string,
    clinicId?: number | string
  ): Promise<AssignmentLeadHistoryItem[]> {
    const leadId = toInt(externalLeadId);
    if (!leadId) throw new Error("externalLeadId inválido para /assignments/lead/{externalLeadId}");

    const { data } = await api.get<AssignmentLeadHistoryItem[]>(`/assignments/lead/${leadId}`, {
      params: cleanParams({ clinicId: toInt(clinicId) }),
    });

    return asArray<AssignmentLeadHistoryItem>(data);
  },

  async ranking(clinicId?: number | string): Promise<AttendantRanking[]> {
    const { data } = await api.get<AttendantRanking[]>("/assignments/ranking", {
      params: cleanParams({ clinicId: toInt(clinicId) }),
    });

    return asArray<AttendantRanking>(data);
  },

  async syncLead(payload: SyncLeadDto): Promise<unknown> {
    const { data } = await api.post<unknown>("/assignments/sync", payload);
    return data;
  },
};
