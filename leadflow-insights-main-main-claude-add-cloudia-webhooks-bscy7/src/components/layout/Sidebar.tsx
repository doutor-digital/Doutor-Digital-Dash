import { NavLink } from "react-router-dom";
import {
  BarChart3, Bell, Building2, Cog, FileBarChart,
  Filter, LayoutDashboard, LineChart,
  ListChecks, Radio, Users2, Workflow,
} from "lucide-react";
import { cn } from "@/lib/utils";

const navGroups = [
  {
    label: "Visão geral",
    items: [
      { to: "/",          label: "Dashboard",  icon: LayoutDashboard, end: true },
      { to: "/live",      label: "Ao vivo",    icon: Radio },
      { to: "/analytics", label: "Analytics",  icon: BarChart3 },
    ],
  },
  {
    label: "Gestão",
    items: [
      { to: "/leads",      label: "Leads",      icon: ListChecks },
      { to: "/funnel",     label: "Funil",      icon: Workflow },
      { to: "/sources",    label: "Origens",    icon: Filter },
      { to: "/evolution",  label: "Evolução",   icon: LineChart },
      { to: "/attendants", label: "Atendentes", icon: Users2 },
      { to: "/units",      label: "Unidades",   icon: Building2 },
    ],
  },
  {
    label: "Relatórios",
    items: [
      { to: "/reports", label: "Relatórios", icon: FileBarChart },
      { to: "/alerts",  label: "Alertas",    icon: Bell },
    ],
  },
  {
    label: "Sistema",
    items: [
      { to: "/settings", label: "Configurações", icon: Cog },
    ],
  },
];

export function Sidebar() {
  return (
    <aside
      className={cn(
        "hidden lg:flex lg:flex-col",
        "w-72 shrink-0",
        "border-r border-white/[0.06]",
        "bg-[rgba(8,8,16,0.75)] backdrop-blur-xl",
        "shadow-[inset_-1px_0_0_rgba(255,255,255,0.04)]"
      )}
    >

      {/* ── Logo ──────────────────────────────────────────────── */}
      <div className="flex items-center gap-3 px-5 py-4 border-b border-white/[0.06]">
        <div
          className={cn(
            "h-10 w-10 shrink-0 overflow-hidden rounded-xl",
          )}
        >
          <img
            src="https://i.postimg.cc/xjx4m8p5/Copia-de-logo-cor-original.png"
            alt="Doutor Digital"
            className="h-full w-full object-cover object-center"
          />
        </div>

        <div className="leading-tight min-w-0">
          <div className="text-[14px] font-bold text-slate-50 tracking-tight truncate">
            Doutor Digital
          </div>
          <div className="text-[10px] uppercase tracking-[0.12em] text-slate-500 mt-0.5">
            Insights · v1.0
          </div>
        </div>
      </div>

      {/* ── Navegação ─────────────────────────────────────────── */}
      <nav className="flex-1 overflow-y-auto px-3 py-4 space-y-5">
        {navGroups.map((group) => (
          <div key={group.label}>
            <p className="mb-1.5 px-3 text-[10px] font-semibold uppercase tracking-[0.12em] text-slate-600">
              {group.label}
            </p>

            <div className="space-y-0.5">
              {group.items.map(({ to, label, icon: Icon, end }) => (
                <NavLink
                  key={to}
                  to={to}
                  end={end}
                  className={({ isActive }) =>
                    cn(
                      "group flex items-center gap-3 rounded-xl px-3 py-2 text-[13px] font-medium",
                      "transition-[background-color,color,box-shadow] duration-150",
                      isActive
                        ? [
                            "bg-brand-500/12 text-brand-200",
                            "ring-1 ring-inset ring-brand-500/25",
                            "shadow-[inset_0_1px_0_rgba(255,255,255,0.05)]",
                          ]
                        : "text-slate-400 hover:bg-white/[0.05] hover:text-slate-200"
                    )
                  }
                >
                  {({ isActive }) => (
                    <>
                      <span
                        className={cn(
                          "flex h-7 w-7 shrink-0 items-center justify-center rounded-lg",
                          "transition-colors duration-150",
                          isActive
                            ? "bg-brand-500/20 text-brand-300"
                            : "bg-white/[0.04] text-slate-500 group-hover:bg-white/[0.07] group-hover:text-slate-300"
                        )}
                      >
                        <Icon className="h-3.5 w-3.5" />
                      </span>
                      <span>{label}</span>
                    </>
                  )}
                </NavLink>
              ))}
            </div>
          </div>
        ))}
      </nav>

      {/* ── Rodapé ────────────────────────────────────────────── */}
      <div className="p-3 border-t border-white/[0.06]">
        <div
          className={cn(
            "rounded-xl border border-white/[0.07] p-3.5",
            "bg-gradient-to-br from-brand-500/8 via-transparent to-violet-500/8"
          )}
        >
          <p className="text-[11px] font-semibold text-slate-300">💡 Dica pro</p>
          <p className="mt-1 text-[11px] leading-5 text-slate-500">
            Use filtros por intervalo para cruzar conversão e origem em tempo real.
          </p>
        </div>
      </div>
    </aside>
  );
}