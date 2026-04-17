import { useSearchParams } from "react-router-dom";
import { useCallback, useMemo } from "react";

export function useFilters<T extends Record<string, string | undefined>>(defaults: T) {
  const [sp, setSp] = useSearchParams();

  const values = useMemo(() => {
    const out: Record<string, string | undefined> = { ...defaults };
    for (const key of Object.keys(defaults)) {
      const v = sp.get(key);
      if (v !== null) out[key] = v;
    }
    return out as T;
  }, [sp, defaults]);

  const setFilter = useCallback(
    (patch: Partial<T>) => {
      const next = new URLSearchParams(sp);
      for (const [k, v] of Object.entries(patch)) {
        if (v === undefined || v === "" || v === null) next.delete(k);
        else next.set(k, String(v));
      }
      setSp(next, { replace: true });
    },
    [sp, setSp]
  );

  const reset = useCallback(() => setSp(new URLSearchParams(), { replace: true }), [setSp]);

  return { values, setFilter, reset };
}
