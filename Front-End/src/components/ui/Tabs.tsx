import { ReactNode } from "react";
import { cn } from "@/lib/utils";

export function Tabs({
  tabs,
  value,
  onChange,
}: {
  tabs: { value: string; label: ReactNode; count?: number }[];
  value: string;
  onChange: (v: string) => void;
}) {
  return (
    <div className="flex gap-1 p-1 bg-white/5 border border-white/10 rounded-xl w-fit">
      {tabs.map((t) => (
        <button
          key={t.value}
          onClick={() => onChange(t.value)}
          className={cn(
            "px-3 py-1.5 text-xs rounded-lg transition-all flex items-center gap-1.5",
            value === t.value
              ? "bg-brand-500 text-white shadow-sm"
              : "text-slate-300 hover:bg-white/5"
          )}
        >
          {t.label}
          {typeof t.count === "number" && (
            <span
              className={cn(
                "ml-1 px-1.5 py-0.5 text-[10px] rounded-full",
                value === t.value ? "bg-white/20 text-white" : "bg-white/10 text-slate-400"
              )}
            >
              {t.count}
            </span>
          )}
        </button>
      ))}
    </div>
  );
}
