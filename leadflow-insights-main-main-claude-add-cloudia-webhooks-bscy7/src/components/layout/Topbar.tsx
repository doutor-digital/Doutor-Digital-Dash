import { useState } from "react";
import { Building2, ChevronDown, LogOut, RefreshCw, Search } from "lucide-react";
import { useAuth } from "@/hooks/useAuth";
import { useClinic } from "@/hooks/useClinic";
import { useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import { cn } from "@/lib/utils";

export function Topbar() {
  const { user, logout } = useAuth();
  const { setContext } = useClinic();
  const qc = useQueryClient();
  const navigate = useNavigate();
  const [refreshing, setRefreshing] = useState(false);

  const clinicId = useClinic().unitId || useClinic().tenantId;

  async function handleRefresh() {
    setRefreshing(true);
    await qc.invalidateQueries();
    setTimeout(() => setRefreshing(false), 700);
  }

  const initials = (user?.name ?? "C")
    .split(" ")
    .map((n) => n[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();

  return (
    <header
      className={cn(
        "sticky top-0 z-20",
        "flex h-14 items-center gap-2 px-4 lg:px-6",
        "border-b border-white/[0.06]",
        "bg-[rgba(8,8,16,0.88)] backdrop-blur-xl",
        "shadow-[inset_0_-1px_0_rgba(255,255,255,0.04),inset_0_1px_0_rgba(255,255,255,0.04)]"
      )}
    >

      {/* ══ ZONA ESQUERDA — Busca ══════════════════════════════════ */}
      <div className="relative w-56 lg:w-72 shrink-0">
        <Search className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-slate-600" />
        <input
          placeholder="Buscar leads…"
          className={cn(
            "peer h-8 w-full rounded-lg pl-8 pr-3",
            "bg-white/[0.04] border border-white/[0.07]",
            "text-[12.5px] text-slate-300 placeholder:text-slate-600",
            "caret-brand-400 outline-none",
            "transition-[border-color,box-shadow,background-color] duration-200",
            "hover:border-white/[0.10] hover:bg-white/[0.05]",
            "focus:border-brand-500/40 focus:bg-white/[0.06]",
            "focus:shadow-[0_0_0_3px_rgba(99,102,241,0.15)]"
          )}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              const v = (e.target as HTMLInputElement).value.trim();
              if (v) navigate(`/leads?search=${encodeURIComponent(v)}`);
            }
          }}
        />
        {/* Label flutuante de atalho */}
        <kbd className="pointer-events-none absolute right-2.5 top-1/2 -translate-y-1/2 rounded border border-white/[0.08] bg-white/[0.04] px-1 py-px text-[10px] text-slate-600 peer-focus:opacity-0 transition-opacity duration-150">
          ↵
        </kbd>
      </div>

      {/* ══ SPACER ════════════════════════════════════════════════ */}
      <div className="flex-1" />

      {/* ══ ZONA DIREITA — Controles ══════════════════════════════ */}
      <div className="flex items-center gap-1.5">

        {/* Unidade ativa + trocar */}
        <button
          onClick={() => navigate("/select-unit")}
          className={cn(
            "hidden md:flex items-center gap-2 h-8 px-3 rounded-lg",
            "border border-white/[0.07] bg-white/[0.03]",
            "transition-[border-color,background-color] duration-200",
            "hover:border-white/[0.12] hover:bg-white/[0.06]",
            "group"
          )}
        >
          <Building2 className="h-3.5 w-3.5 text-slate-500 group-hover:text-slate-300 transition-colors duration-200" />
          <span className="text-[11px] text-slate-500 group-hover:text-slate-400 transition-colors duration-200">
            Unidade
          </span>
          <span
            className={cn(
              "text-[11px] font-mono font-semibold max-w-[6rem] truncate",
              clinicId ? "text-slate-200" : "text-slate-600"
            )}
          >
            {clinicId || "—"}
          </span>
          <ChevronDown className="h-3 w-3 text-slate-600 group-hover:text-slate-400 transition-colors duration-200" />
        </button>

        {/* Divisor */}
        <div className="mx-1 h-5 w-px bg-white/[0.07]" />

        {/* Refresh */}
        <button
          onClick={handleRefresh}
          title="Recarregar dados"
          className={cn(
            "flex h-8 w-8 items-center justify-center rounded-lg",
            "text-slate-500 transition-[color,background-color,transform] duration-200",
            "hover:bg-white/[0.06] hover:text-slate-200",
            "active:scale-90"
          )}
        >
          <RefreshCw
            className={cn(
              "h-3.5 w-3.5",
              refreshing
                ? "animate-spin text-brand-400"
                : "transition-transform duration-300"
            )}
          />
        </button>

        {/* Divisor */}
        <div className="mx-1 h-5 w-px bg-white/[0.07]" />

        {/* Usuário + Avatar + Logout */}
        <div className="flex items-center gap-2">

          {/* Info do usuário */}
          <div className="hidden sm:flex flex-col items-end leading-none gap-0.5">
            <span className="text-[12px] font-medium text-slate-200 leading-none">
              {user?.name ?? "Convidado"}
            </span>
            <span className="text-[10px] text-slate-500 leading-none">
              {user?.email ?? "local"}
            </span>
          </div>

          {/* Avatar */}
          <div
            className={cn(
              "relative h-8 w-8 shrink-0 select-none",
              "rounded-full cursor-default",
              "bg-gradient-to-br from-brand-400 to-violet-600",
              "grid place-items-center",
              "text-[11px] font-black tracking-wide text-white",
              "ring-[1.5px] ring-white/10 ring-offset-[1.5px] ring-offset-[rgba(8,8,16,0.88)]",
              "transition-[ring-color] duration-200 hover:ring-white/25",
              "shadow-[0_0_0_0] hover:shadow-[0_0_12px_rgba(139,92,246,0.35)]"
            )}
          >
            {initials}
            {/* Status online */}
            <span
              aria-label="online"
              className="absolute -bottom-0.5 -right-0.5 h-2.5 w-2.5 rounded-full bg-emerald-400 ring-[1.5px] ring-[rgba(8,8,16,0.88)]"
            />
          </div>

          {/* Logout */}
          <button
            onClick={logout}
            title="Sair da conta"
            className={cn(
              "flex h-8 w-8 items-center justify-center rounded-lg",
              "text-slate-600 transition-[color,background-color,transform] duration-200",
              "hover:bg-red-500/10 hover:text-red-400",
              "active:scale-90"
            )}
          >
            <LogOut className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>
    </header>
  );
}