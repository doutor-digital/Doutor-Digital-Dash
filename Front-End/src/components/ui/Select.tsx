import { SelectHTMLAttributes, forwardRef } from "react";
import { cn } from "@/lib/utils";

export const Select = forwardRef<HTMLSelectElement, SelectHTMLAttributes<HTMLSelectElement>>(
  ({ className, children, ...rest }, ref) => (
    <select
      ref={ref}
      className={cn(
        "w-full bg-white/5 border border-white/10 rounded-lg px-3 py-2 text-sm text-slate-100",
        "focus:outline-none focus:ring-2 focus:ring-brand-500/60 focus:border-brand-500/60 transition",
        "[&>option]:bg-ink-900 [&>option]:text-slate-100",
        className
      )}
      {...rest}
    >
      {children}
    </select>
  )
);
Select.displayName = "Select";
