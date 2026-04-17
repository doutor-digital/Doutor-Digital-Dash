// src/components/filters/DashboardFilters.tsx

import { useEffect, useRef, useState } from "react";
import {
  BarChart2, Calendar, ChevronDown,
  GitCompare, RotateCcw, Search, SlidersHorizontal,
} from "lucide-react";
import { cn } from "@/lib/utils";

// ─── Tipos ────────────────────────────────────────────────────────────────────

export type Granularity = "day" | "week" | "month" | "quarter";
export type CompareMode = "none" | "previous_period" | "previous_year";

export interface DashboardFiltersState {
  preset:      string;
  startDate:   string;
  endDate:     string;
  granularity: Granularity;
  compare:     CompareMode;
}

interface Props {
  value:    DashboardFiltersState;
  onChange: (f: DashboardFiltersState) => void;
  onSearch: () => void;   // ← novo
}

// ─── Dados ────────────────────────────────────────────────────────────────────

const PRESETS = [
  { label: "Hoje",         value: "today",        resolve: () => { const d = today(); return { start: d, end: d }; } },
  { label: "Ontem",        value: "yesterday",    resolve: () => { const d = offsetDay(-1); return { start: d, end: d }; } },
  { label: "7 dias",       value: "7d",           resolve: () => ({ start: offsetDay(-6),   end: today() }) },
  { label: "14 dias",      value: "14d",          resolve: () => ({ start: offsetDay(-13),  end: today() }) },
  { label: "30 dias",      value: "30d",          resolve: () => ({ start: offsetDay(-29),  end: today() }) },
  { label: "90 dias",      value: "90d",          resolve: () => ({ start: offsetDay(-89),  end: today() }) },
  { label: "Este mês",     value: "this_month",   resolve: () => ({ start: startOfMonth(0),  end: today() }) },
  { label: "Mês anterior", value: "last_month",   resolve: () => ({ start: startOfMonth(-1), end: endOfMonth(-1) }) },
  { label: "Trimestre",    value: "this_quarter", resolve: () => ({ start: startOfQuarter(), end: today() }) },
  { label: "Este ano",     value: "this_year",    resolve: () => ({ start: `${new Date().getFullYear()}-01-01`, end: today() }) },
  { label: "Ano anterior", value: "last_year",    resolve: () => { const y = new Date().getFullYear() - 1; return { start: `${y}-01-01`, end: `${y}-12-31` }; } },
];

const GRANULARITIES: { label: string; value: Granularity }[] = [
  { label: "Dia",       value: "day"     },
  { label: "Semana",    value: "week"    },
  { label: "Mês",       value: "month"   },
  { label: "Trimestre", value: "quarter" },
];

const COMPARE_MODES: { label: string; value: CompareMode }[] = [
  { label: "Nenhum",            value: "none"            },
  { label: "Período anterior",  value: "previous_period" },
  { label: "Mesmo período/ano", value: "previous_year"   },
];

// ─── Helpers ──────────────────────────────────────────────────────────────────

function today()                 { return new Date().toISOString().slice(0, 10); }
function offsetDay(n: number)    { const d = new Date(); d.setDate(d.getDate() + n); return d.toISOString().slice(0, 10); }
function startOfMonth(o: number) { const d = new Date(); d.setMonth(d.getMonth() + o, 1); return d.toISOString().slice(0, 10); }
function endOfMonth(o: number)   { const d = new Date(); d.setMonth(d.getMonth() + o + 1, 0); return d.toISOString().slice(0, 10); }
function startOfQuarter()        { const m = new Date().getMonth(); const d = new Date(new Date().getFullYear(), Math.floor(m / 3) * 3, 1); return d.toISOString().slice(0, 10); }

export function defaultFilters(): DashboardFiltersState {
  return { preset: "30d", startDate: offsetDay(-29), endDate: today(), granularity: "day", compare: "none" };
}

// ─── Subcomponentes ───────────────────────────────────────────────────────────

function FilterBtn({
  active, onClick, children,
  tone = "brand",
}: {
  active: boolean;
  onClick: () => void;
  children: React.ReactNode;
  tone?: "brand" | "violet" | "emerald";
}) {
  const activeStyles = {
    brand:   "bg-brand-500/15 text-brand-300 ring-1 ring-brand-500/30",
    violet:  "bg-violet-500/15 text-violet-300 ring-1 ring-violet-500/30",
    emerald: "bg-emerald-500/12 text-emerald-300 ring-1 ring-emerald-500/25",
  };
  return (
    <button
      onClick={onClick}
      className={cn(
        "rounded-xl px-3 py-1.5 text-[12px] font-semibold transition-all duration-150",
        active ? activeStyles[tone] : "text-slate-400 hover:bg-white/[0.06] hover:text-slate-200"
      )}
    >
      {children}
    </button>
  );
}

// ─── Dropdown de Período ──────────────────────────────────────────────────────

function PeriodDropdown({
  value,
  onChange,
}: {
  value: DashboardFiltersState;
  onChange: (f: DashboardFiltersState) => void;
}) {
  const [open, setOpen] = useState(false);
  const [showCustom, setShowCustom] = useState(value.preset === "custom");
  const ref = useRef<HTMLDivElement>(null);

  // Fecha ao clicar fora
  useEffect(() => {
    function handle(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    }
    document.addEventListener("mousedown", handle);
    return () => document.removeEventListener("mousedown", handle);
  }, []);

  function applyPreset(preset: string) {
    const p = PRESETS.find((x) => x.value === preset)!;
    const { start, end } = p.resolve();
    const isCustom = preset === "custom";
    setShowCustom(isCustom);
    onChange({ ...value, preset, startDate: start, endDate: end });
    if (!isCustom) setOpen(false);
  }

  const activePreset = PRESETS.find((p) => p.value === value.preset);
  const rangeLabel = value.startDate === value.endDate
    ? value.startDate
    : `${value.startDate} → ${value.endDate}`;

  return (
    <div ref={ref} className="relative">

      {/* ── Trigger ── */}
      <button
        onClick={() => setOpen((v) => !v)}
        className={cn(
          "flex items-center gap-2 rounded-xl px-3.5 py-2 text-[12px] font-semibold",
          "border transition-all duration-150",
          open
            ? "border-brand-500/40 bg-brand-500/10 text-brand-300 shadow-[0_0_0_3px_rgba(99,102,241,0.12)]"
            : "border-white/[0.08] bg-white/[0.03] text-slate-300 hover:border-white/[0.14] hover:bg-white/[0.06]"
        )}
      >
        <Calendar className="h-3.5 w-3.5 text-brand-400" />
        <span>{activePreset?.label ?? "Período"}</span>
        <span className="mx-1 h-3 w-px bg-white/10" />
        <span className="font-mono text-[10px] text-slate-500">{rangeLabel}</span>
        <ChevronDown className={cn(
          "h-3.5 w-3.5 text-slate-500 transition-transform duration-200",
          open && "rotate-180"
        )} />
      </button>

      {/* ── Dropdown popup ── */}
      {open && (
        <div
          className={cn(
            "absolute left-0 top-[calc(100%+8px)] z-50 w-[440px]",
            "rounded-2xl border border-white/[0.1]",
            "bg-[rgba(14,14,24,0.97)] backdrop-blur-xl",
            "shadow-[0_24px_64px_rgba(0,0,0,0.6),inset_0_1px_0_rgba(255,255,255,0.06)]",
            // Animação de entrada
            "animate-in fade-in-0 slide-in-from-top-2 duration-150"
          )}
        >
          {/* Cabeçalho do popup */}
          <div className="flex items-center justify-between border-b border-white/[0.06] px-4 py-3">
            <span className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-500">
              Selecionar período
            </span>
            <span className="rounded-lg bg-white/[0.05] px-2 py-0.5 font-mono text-[10px] text-slate-400">
              {rangeLabel}
            </span>
          </div>

          {/* Grid de presets */}
          <div className="grid grid-cols-3 gap-1 p-3">
            {PRESETS.map((p) => (
              <button
                key={p.value}
                onClick={() => applyPreset(p.value)}
                className={cn(
                  "rounded-xl px-3 py-2.5 text-left text-[12px] font-semibold",
                  "transition-all duration-100",
                  value.preset === p.value
                    ? "bg-brand-500/15 text-brand-300 ring-1 ring-brand-500/30"
                    : "text-slate-400 hover:bg-white/[0.06] hover:text-slate-200"
                )}
              >
                {p.label}
              </button>
            ))}

            {/* Personalizado */}
            <button
              onClick={() => applyPreset("custom")}
              className={cn(
                "col-span-3 flex items-center gap-2 rounded-xl px-3 py-2.5 text-[12px] font-semibold",
                "transition-all duration-100",
                value.preset === "custom"
                  ? "bg-brand-500/15 text-brand-300 ring-1 ring-brand-500/30"
                  : "text-slate-400 hover:bg-white/[0.06] hover:text-slate-200"
              )}
            >
              <Calendar className="h-3.5 w-3.5" />
              Intervalo personalizado
            </button>
          </div>

          {/* Inputs custom — só aparece quando selecionado */}
          {showCustom && (
            <div className="border-t border-white/[0.06] px-4 py-3">
              <div className="flex flex-wrap items-center gap-3">
                <div className="flex flex-col gap-1">
                  <label className="text-[10px] font-semibold uppercase tracking-widest text-slate-600">
                    Início
                  </label>
                  <input
                    type="date"
                    value={value.startDate}
                    max={value.endDate}
                    onChange={(e) => onChange({ ...value, startDate: e.target.value, preset: "custom" })}
                    className={cn(
                      "h-9 rounded-xl border border-white/[0.1] bg-white/[0.05]",
                      "px-3 text-[12px] font-medium text-slate-200",
                      "outline-none transition-all duration-150",
                      "focus:border-brand-500/50 focus:shadow-[0_0_0_3px_rgba(99,102,241,0.15)]",
                      "[color-scheme:dark]"
                    )}
                  />
                </div>

                <span className="mt-5 text-slate-600">→</span>

                <div className="flex flex-col gap-1">
                  <label className="text-[10px] font-semibold uppercase tracking-widest text-slate-600">
                    Fim
                  </label>
                  <input
                    type="date"
                    value={value.endDate}
                    min={value.startDate}
                    max={today()}
                    onChange={(e) => onChange({ ...value, endDate: e.target.value, preset: "custom" })}
                    className={cn(
                      "h-9 rounded-xl border border-white/[0.1] bg-white/[0.05]",
                      "px-3 text-[12px] font-medium text-slate-200",
                      "outline-none transition-all duration-150",
                      "focus:border-brand-500/50 focus:shadow-[0_0_0_3px_rgba(99,102,241,0.15)]",
                      "[color-scheme:dark]"
                    )}
                  />
                </div>

                {/* Contador de dias */}
                {(() => {
                  const diff = Math.round(
                    (new Date(value.endDate).getTime() - new Date(value.startDate).getTime()) / 86_400_000
                  );
                  return diff >= 0 ? (
                    <span className="mt-5 rounded-lg bg-brand-500/10 px-2.5 py-1.5 text-[11px] font-bold text-brand-400">
                      {diff + 1}d
                    </span>
                  ) : null;
                })()}
              </div>
            </div>
          )}

          {/* Rodapé com botão confirmar */}
          <div className="flex justify-end border-t border-white/[0.06] px-4 py-3">
            <button
              onClick={() => setOpen(false)}
              className="rounded-xl bg-brand-500/15 px-4 py-2 text-[12px] font-bold text-brand-300 ring-1 ring-brand-500/30 transition hover:bg-brand-500/25"
            >
              Confirmar
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

// ─── Componente principal ─────────────────────────────────────────────────────

export function DashboardFilters({ value, onChange, onSearch }: Props) {
  const isGranularity = (v: string) => value.granularity === v;
  const isCompare     = (v: string) => value.compare     === v;

  return (
    <div
      className={cn(
        "flex flex-wrap items-center gap-2 rounded-2xl",
        "border border-white/[0.08] bg-[rgba(255,255,255,0.02)]",
        "px-4 py-3",
        "shadow-[0_1px_3px_rgba(0,0,0,0.25),inset_0_1px_0_rgba(255,255,255,0.04)]"
      )}
    >
      <SlidersHorizontal className="h-3.5 w-3.5 shrink-0 text-slate-600" />

      <PeriodDropdown value={value} onChange={onChange} />

      <div className="h-5 w-px bg-white/[0.07]" />

      {/* Granularidade */}
      <div className="flex items-center gap-1.5">
        <BarChart2 className="h-3.5 w-3.5 text-slate-600" />
        <span className="text-[10px] font-bold uppercase tracking-widest text-slate-600">Agrupar</span>
        <div className="ml-1 flex items-center gap-0.5">
          {GRANULARITIES.map((g) => (
            <FilterBtn key={g.value} active={isGranularity(g.value)}
              onClick={() => onChange({ ...value, granularity: g.value })} tone="violet">
              {g.label}
            </FilterBtn>
          ))}
        </div>
      </div>

      <div className="h-5 w-px bg-white/[0.07]" />

      {/* Comparação */}
      <div className="flex items-center gap-1.5">
        <GitCompare className="h-3.5 w-3.5 text-slate-600" />
        <span className="text-[10px] font-bold uppercase tracking-widest text-slate-600">Comparar</span>
        <div className="ml-1 flex items-center gap-0.5">
          {COMPARE_MODES.map((c) => (
            <FilterBtn key={c.value} active={isCompare(c.value)}
              onClick={() => onChange({ ...value, compare: c.value })} tone="emerald">
              {c.label}
            </FilterBtn>
          ))}
        </div>
      </div>

      {/* ── Ações: Reset + Buscar ── */}
      <div className="ml-auto flex items-center gap-2">

        {/* Reset */}
        <button
          onClick={() => onChange(defaultFilters())}
          title="Resetar filtros"
          className="flex items-center gap-1.5 rounded-xl px-3 py-1.5 text-[11px] font-semibold text-slate-600 transition-all hover:bg-white/[0.05] hover:text-slate-300"
        >
          <RotateCcw className="h-3 w-3" />
          Resetar
        </button>

        {/* Buscar */}
        <button
          onClick={onSearch}
          className={cn(
            "flex items-center gap-1.5 rounded-xl px-4 py-2 text-[12px] font-bold",
            "bg-brand-500/15 text-brand-300",
            "ring-1 ring-brand-500/30",
            "shadow-[0_0_12px_rgba(99,102,241,0.15)]",
            "transition-all duration-150",
            "hover:bg-brand-500/25 hover:shadow-[0_0_18px_rgba(99,102,241,0.25)]",
            "active:scale-[0.97]"
          )}
        >
          <Search className="h-3.5 w-3.5" />
          Buscar
        </button>
      </div>
    </div>
  );
}