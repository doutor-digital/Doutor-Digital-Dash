/**
 * Traduz erros HTTP para o contrato `ProblemDetails` do RFC 7807 quando
 * o backend responde nesse formato, ou para um fallback estruturado.
 */

import { AxiosError } from "axios";
import type { ProblemDetails } from "@/api/types";

export interface FriendlyError {
  status: number | null;
  title: string;
  detail: string;
  raw: ProblemDetails | null;
}

export function parseProblemDetails(err: unknown): FriendlyError {
  if (isAxiosError(err)) {
    const resp = err.response;
    const data = resp?.data;
    if (isProblem(data)) {
      return {
        status: data.status ?? resp?.status ?? null,
        title: data.title ?? resp?.statusText ?? "Erro",
        detail: data.detail ?? err.message,
        raw: data,
      };
    }
    return {
      status: resp?.status ?? null,
      title: resp?.statusText ?? "Erro",
      detail:
        (typeof data === "object" && data !== null && "message" in data
          ? String((data as { message: unknown }).message)
          : null) ?? err.message,
      raw: null,
    };
  }
  if (err instanceof Error) {
    return { status: null, title: err.name, detail: err.message, raw: null };
  }
  return { status: null, title: "Erro", detail: String(err), raw: null };
}

function isAxiosError(e: unknown): e is AxiosError<unknown> {
  return typeof e === "object" && e !== null && (e as AxiosError).isAxiosError === true;
}

function isProblem(v: unknown): v is ProblemDetails {
  if (typeof v !== "object" || v === null) return false;
  const r = v as Record<string, unknown>;
  return (
    "title" in r ||
    "detail" in r ||
    "type" in r ||
    ("status" in r && typeof r.status === "number")
  );
}
