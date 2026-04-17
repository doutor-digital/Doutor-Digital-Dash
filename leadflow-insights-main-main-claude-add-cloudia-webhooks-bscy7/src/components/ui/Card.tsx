import { HTMLAttributes, ReactNode } from "react";
import { cn } from "@/lib/utils";

// ─── Card ─────────────────────────────────────────────────────────────────────

export function Card({
  className,
  children,
  ...props
}: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn(
        "group relative flex flex-col overflow-hidden rounded-2xl",

        // Fundo com textura translúcida
        "bg-gradient-to-b from-white/[0.05] to-white/[0.02]",

        // Borda externa + inner glow no topo via box-shadow
        "border border-white/[0.08]",
        "shadow-[0_2px_8px_rgba(0,0,0,0.35),inset_0_1px_0_rgba(255,255,255,0.07)]",

        // Hover — apenas compositor, zero reflow
        "transition-[border-color,box-shadow,background] duration-300",
        "hover:border-white/[0.14]",
        "hover:shadow-[0_8px_32px_rgba(0,0,0,0.4),inset_0_1px_0_rgba(255,255,255,0.10)]",
        "hover:from-white/[0.07] hover:to-white/[0.03]",

        className
      )}
      {...props}
    >
      {/* Reflexo de luz no topo — detalhe premium */}
      <div
        aria-hidden
        className="pointer-events-none absolute inset-x-0 top-0 h-px bg-gradient-to-r from-transparent via-white/20 to-transparent"
      />

      {children}
    </div>
  );
}

// ─── CardHeader ───────────────────────────────────────────────────────────────

export function CardHeader({
  title,
  subtitle,
  action,
  className,
}: {
  title: ReactNode;
  subtitle?: ReactNode;
  action?: ReactNode;
  className?: string;
}) {
  return (
    <div
      className={cn(
        "flex items-start justify-between gap-4 px-5 pt-5 pb-4",
        className
      )}
    >
      <div className="min-w-0 flex-1">
        {/* Título com gradiente claro → branco puro */}
        <h3
          className={cn(
            "truncate text-[13px] font-semibold leading-snug tracking-tight",
            "bg-gradient-to-r from-slate-100 to-white bg-clip-text text-transparent"
          )}
        >
          {title}
        </h3>

        {subtitle && (
          <p className="mt-1 truncate text-[11px] leading-5 text-slate-500 transition-colors duration-300 group-hover:text-slate-400">
            {subtitle}
          </p>
        )}
      </div>

      {action && (
        <div className="shrink-0 translate-y-px opacity-70 transition-opacity duration-300 group-hover:opacity-100">
          {action}
        </div>
      )}
    </div>
  );
}

// ─── CardBody ─────────────────────────────────────────────────────────────────

export function CardBody({
  className,
  children,
  ...props
}: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn(
        "relative flex-1 px-5 py-4",

        // Divisor superior com fade nas laterais
        "border-t border-white/[0.06]",

        // Gradiente interno top → transparente (profundidade de conteúdo)
        "bg-[linear-gradient(to_bottom,rgba(255,255,255,0.025)_0%,transparent_60%)]",

        className
      )}
      {...props}
    >
      {/* ── Cantos iluminados — detalhe de material ── */}
      <span
        aria-hidden
        className="pointer-events-none absolute left-0 top-0 h-12 w-12
          bg-[radial-gradient(ellipse_at_top_left,rgba(255,255,255,0.06),transparent_70%)]"
      />
      <span
        aria-hidden
        className="pointer-events-none absolute right-0 bottom-0 h-16 w-16
          bg-[radial-gradient(ellipse_at_bottom_right,rgba(255,255,255,0.03),transparent_70%)]"
      />

      {/* ── Conteúdo real ── */}
      <div className="relative">{children}</div>
    </div>
  );
}