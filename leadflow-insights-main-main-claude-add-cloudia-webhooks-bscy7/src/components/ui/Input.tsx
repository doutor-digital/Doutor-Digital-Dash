import { InputHTMLAttributes, forwardRef } from "react";
import { cn } from "@/lib/utils";

interface Props extends InputHTMLAttributes<HTMLInputElement> {
  icon?: React.ReactNode;
}

export const Input = forwardRef<HTMLInputElement, Props>(
  ({ className, icon, ...rest }, ref) => (
    <div className="relative">
      {icon && (
        <span className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-500 pointer-events-none">
          {icon}
        </span>
      )}
      <input
        ref={ref}
        className={cn(
          "w-full bg-white/5 border border-white/10 rounded-lg px-3 py-2 text-sm text-slate-100",
          "placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-brand-500/60 focus:border-brand-500/60 transition",
          icon && "pl-9",
          className
        )}
        {...rest}
      />
    </div>
  )
);
Input.displayName = "Input";
