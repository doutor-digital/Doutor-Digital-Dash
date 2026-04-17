import { ReactNode } from "react";
import { TrendingDown, TrendingUp } from "lucide-react";
import { cn, formatNumber } from "@/lib/utils";

const toneConfig = {
  neutral: {
    orb:    "bg-slate-400/10",
    icon:   "text-slate-400",
    iconBg: "bg-slate-400/8 ring-slate-400/15",
    bar:    "bg-slate-400",
    label:  "text-slate-400",
    value:  "text-white",
  },
  blue: {
    orb:    "bg-blue-500/12",
    icon:   "text-blue-300",
    iconBg: "bg-blue-500/10 ring-blue-400/20",
    bar:    "bg-blue-400",
    label:  "text-blue-400/80",
    value:  "text-blue-50",
  },
  green: {
    orb:    "bg-emerald-500/12",
    icon:   "text-emerald-300",
    iconBg: "bg-emerald-500/10 ring-emerald-400/20",
    bar:    "bg-emerald-400",
    label:  "text-emerald-400/80",
    value:  "text-emerald-50",
  },
  amber: {
    orb:    "bg-amber-500/10",
    icon:   "text-amber-300",
    iconBg: "bg-amber-500/8 ring-amber-400/15",
    bar:    "bg-amber-400",
    label:  "text-amber-400/80",
    value:  "text-amber-50",
  },
  red: {
    orb:    "bg-red-500/10",
    icon:   "text-red-300",
    iconBg: "bg-red-500/8 ring-red-400/15",
    bar:    "bg-red-400",
    label:  "text-red-400/80",
    value:  "text-red-50",
  },
  violet: {
    orb:    "bg-violet-500/12",
    icon:   "text-violet-300",
    iconBg: "bg-violet-500/10 ring-violet-400/20",
    bar:    "bg-violet-400",
    label:  "text-violet-400/80",
    value:  "text-violet-50",
  },
} as const;

export function KpiCard({
  label,
  value,
  icon,
  trend,
  tone = "neutral",
  subtitle,
  loading,
}: {
  label: string;
  value: number | string;
  icon?: ReactNode;
  trend?: number;
  tone?: keyof typeof toneConfig;
  subtitle?: string;
  loading?: boolean;
}) {
  const t = toneConfig[tone];
  const trendUp = trend !== undefined && trend >= 0;

  return (
    <div
      className={cn(
        "group relative flex flex-col justify-between overflow-hidden",
        "rounded-2xl p-5 min-h-[130px]",
        "bg-[rgba(255,255,255,0.03)]",
        "border border-white/[0.07]",
        "shadow-[0_1px_3px_rgba(0,0,0,0.3),inset_0_1px_0_rgba(255,255,255,0.05)]",
        "transition-[transform,box-shadow,border-color] duration-300 ease-out",
        "hover:-translate-y-px",
        "hover:border-white/[0.12]",
        "hover:shadow-[0_8px_24px_rgba(0,0,0,0.35),inset_0_1px_0_rgba(255,255,255,0.07)]",
      )}
    >
      {/* Orb de luz no hover */}
      <div
        aria-hidden
        className={cn(
          "pointer-events-none absolute -top-4 -right-4 h-24 w-24 rounded-full blur-2xl",
          "opacity-0 transition-opacity duration-500 group-hover:opacity-100",
          t.orb
        )}
      />

      {/* Topo: label + ícone */}
      <div className="relative flex items-start justify-between gap-2">
        <span className={cn(
          "text-[11px] font-semibold uppercase tracking-[0.13em]",
          "transition-colors duration-300",
          t.label
        )}>
          {label}
        </span>

        {icon && (
          <span className={cn(
            "flex h-8 w-8 shrink-0 items-center justify-center rounded-xl",
            "ring-1 transition-[transform,ring-color] duration-300",
            "group-hover:scale-105",
            t.icon,
            t.iconBg
          )}>
            <span className="[&>svg]:h-4 [&>svg]:w-4">{icon}</span>
          </span>
        )}
      </div>

      {/* Centro: valor + trend */}
      <div className="relative mt-3 flex items-end justify-between gap-2">
        <div className="flex items-baseline gap-2">
          {loading ? (
            <div className="skeleton h-8 w-24 rounded-lg" />
          ) : (
            <span className={cn(
              "text-[1.85rem] font-bold leading-none tracking-tight tabular-nums",
              "transition-colors duration-300",
              t.value
            )}>
              {typeof value === "number" ? formatNumber(value) : value}
            </span>
          )}

          {trend !== undefined && !loading && (
            <span className={cn(
              "mb-0.5 inline-flex items-center gap-0.5 rounded-md px-1.5 py-0.5",
              "text-[10.5px] font-bold tabular-nums",
              "transition-[opacity,transform] duration-300",
              "opacity-70 translate-y-0.5 group-hover:opacity-100 group-hover:translate-y-0",
              trendUp
                ? "bg-emerald-500/10 text-emerald-400"
                : "bg-red-500/10 text-red-400"
            )}>
              {trendUp
                ? <TrendingUp className="h-2.5 w-2.5 shrink-0" />
                : <TrendingDown className="h-2.5 w-2.5 shrink-0" />
              }
              {trendUp ? "+" : "−"}{Math.abs(trend).toFixed(1)}%
            </span>
          )}
        </div>
      </div>

      {/* Subtítulo */}
      {subtitle && (
        <div className="relative mt-3 space-y-1.5">
          <div className="h-[1px] bg-white/[0.05] transition-colors duration-300 group-hover:bg-white/[0.09]" />
          <p className={cn(
            "text-[10.5px] leading-4 text-slate-500",
            "transition-colors duration-300 group-hover:text-slate-400"
          )}>
            {subtitle}
          </p>
        </div>
      )}

      {/* ── Barra inferior — sempre visível, brilha no hover ── */}
      <div
        aria-hidden
        className={cn(
          "absolute bottom-0 left-0 right-0 h-[2px]",
          "bg-gradient-to-r from-transparent via-current to-transparent",
          "opacity-30 transition-opacity duration-300 group-hover:opacity-70",
          t.bar
        )}
      />
    </div>
  );
}