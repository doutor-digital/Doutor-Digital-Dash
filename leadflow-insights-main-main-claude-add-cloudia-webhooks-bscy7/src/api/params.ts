/**
 * Helpers de serialização de query params.
 * Centraliza conversões (ISO date-time, remoção de undefined) para que
 * nenhum service precise reinventar o formato esperado pelo contrato.
 */

/** Remove chaves com undefined/null/"" — mantém 0 e false. */
export function cleanParams<T extends Record<string, unknown>>(
  obj: T
): Partial<T> {
  const out: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(obj)) {
    if (v === undefined || v === null || v === "") continue;
    out[k] = v;
  }
  return out as Partial<T>;
}

/**
 * Converte Date | string ISO | "YYYY-MM-DD" em ISO date-time completo.
 * Obrigatório para endpoints com `format: date-time` no OpenAPI.
 */
export function toIsoDateTime(
  value: Date | string | null | undefined
): string | undefined {
  if (value === null || value === undefined || value === "") return undefined;
  if (value instanceof Date) return value.toISOString();
  // Trata "YYYY-MM-DD" como meia-noite local do cliente.
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    return new Date(`${value}T00:00:00`).toISOString();
  }
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? undefined : d.toISOString();
}

/** Converte string|number|undefined em number|undefined seguro. */
export function toNumberOrUndef(
  value: string | number | null | undefined
): number | undefined {
  if (value === null || value === undefined || value === "") return undefined;
  const n = typeof value === "number" ? value : Number(value);
  return Number.isFinite(n) ? n : undefined;
}
