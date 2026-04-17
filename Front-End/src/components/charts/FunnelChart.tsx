import { cn, formatNumber, formatPercent } from "@/lib/utils";

export interface FunnelStage {
  label: string;
  count: number;
  tone?: "blue" | "violet" | "amber" | "emerald" | "teal";
}

const toneBar: Record<NonNullable<FunnelStage["tone"]>, string> = {
  blue: "from-brand-500 to-brand-700",
  violet: "from-violet-500 to-violet-700",
  amber: "from-amber-500 to-orange-600",
  emerald: "from-emerald-500 to-emerald-700",
  teal: "from-teal-500 to-teal-700",
};

export function FunnelChart({ stages }: { stages: FunnelStage[] }) {
  const max = Math.max(1, ...stages.map((s) => s.count));
  const first = stages[0]?.count || 1;
  return (
    <div className="space-y-2">
      {stages.map((s, i) => {
        const widthPct = Math.max(6, (s.count / max) * 100);
        const convRate = i === 0 ? 100 : (s.count / first) * 100;
        const dropFromPrev =
          i > 0 ? ((stages[i - 1].count - s.count) / Math.max(1, stages[i - 1].count)) * 100 : 0;

        return (
          <div key={s.label} className="rounded-lg border border-white/10 bg-white/[0.02] p-3">
            <div className="flex items-center justify-between mb-2">
              <span className="text-sm text-slate-200 font-medium">{s.label}</span>
              <div className="flex items-center gap-3 text-xs">
                <span className="text-slate-400">Conv.</span>
                <span className="font-semibold text-slate-100">
                  {formatPercent(convRate)}
                </span>
                {i > 0 && (
                  <span
                    className={cn(
                      "chip",
                      dropFromPrev > 0 ? "bg-red-500/15 text-red-300" : "bg-emerald-500/15 text-emerald-300"
                    )}
                  >
                    {dropFromPrev > 0 ? "-" : "+"}
                    {Math.abs(dropFromPrev).toFixed(1)}%
                  </span>
                )}
              </div>
            </div>
            <div className="relative h-9 rounded-md bg-white/5 overflow-hidden">
              <div
                className={cn(
                  "absolute inset-y-0 left-0 bg-gradient-to-r rounded-md flex items-center px-3 transition-all",
                  toneBar[s.tone ?? "blue"]
                )}
                style={{ width: `${widthPct}%` }}
              >
                <span className="text-xs font-semibold text-white">
                  {formatNumber(s.count)}
                </span>
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}
