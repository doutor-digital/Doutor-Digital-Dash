import { create } from "zustand";
import { persist } from "zustand/middleware";

interface ClinicStore {
  tenantId: number | null;
  unitId: number | null;

  setContext: (tenantId: number, unitId: number) => void;
}

export const useClinic = create<ClinicStore>()(
  persist(
    (set) => ({
      tenantId: null,
      unitId: null,
      setContext: (tenantId, unitId) => set({ tenantId, unitId }),
    }),
    { name: "doutor.digital.clinic" }
  )
);