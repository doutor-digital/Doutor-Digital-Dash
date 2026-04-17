import { ReactNode } from "react";
import { cn } from "@/lib/utils";

type Tone = "neutral" | "blue" | "green" | "yellow" | "red" | "violet" | "slate";

export function Badge({
  children,
  tone = "neutral",
  className,
}: {
  children: ReactNode;
  tone?: Tone;
  className?: string;
}) {
  const tones: Record<Tone, string> = {
    neutral: "bg-white/10 text-slate-200 border-white/10",
    blue: "bg-brand-500/15 text-brand-200 border-brand-500/30",
    green: "bg-emerald-500/15 text-emerald-300 border-emerald-500/30",
    yellow: "bg-amber-500/15 text-amber-300 border-amber-500/30",
    red: "bg-red-500/15 text-red-300 border-red-500/30",
    violet: "bg-violet-500/15 text-violet-300 border-violet-500/30",
    slate: "bg-slate-500/15 text-slate-300 border-slate-500/30",
  };
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-semibold border",
        tones[tone],
        className
      )}
    >
      {children}
    </span>
  );
}

export function StateBadge({ state }: { state?: string | null }) {
  if (!state) return <Badge tone="slate">—</Badge>;
  const map: Record<string, { tone: Tone; label: string }> = {
    bot: { tone: "violet", label: "Bot" },
    queue: { tone: "yellow", label: "Fila" },
    service: { tone: "blue", label: "Atendimento" },
    concluido: { tone: "green", label: "Concluído" },
  };
  const c = map[state] ?? { tone: "neutral" as Tone, label: state };
  return <Badge tone={c.tone}>{c.label}</Badge>;
}

export function StageBadge({ stage }: { stage?: string | null }) {
  if (!stage) return <Badge tone="slate">—</Badge>;
  const s = stage.toLowerCase();
  let tone: Tone = "neutral";
  if (s.includes("fechou") || s.includes("tratamento")) tone = "green";
  else if (s.includes("pagamento")) tone = "blue";
  else if (s.includes("agendado")) tone = "yellow";
  else if (s.includes("perdido") || s.includes("cancelado")) tone = "red";
  return <Badge tone={tone}>{stage.replace(/_/g, " ")}</Badge>;
}
