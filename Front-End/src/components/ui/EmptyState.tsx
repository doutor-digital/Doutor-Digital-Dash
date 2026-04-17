import { Inbox } from "lucide-react";
import { ReactNode } from "react";

export function EmptyState({
  title = "Sem dados para exibir",
  description,
  action,
  icon,
}: {
  title?: string;
  description?: string;
  action?: ReactNode;
  icon?: ReactNode;
}) {
  return (
    <div className="flex flex-col items-center justify-center p-10 text-center">
      <div className="h-12 w-12 rounded-full bg-white/5 border border-white/10 grid place-items-center mb-3">
        {icon ?? <Inbox className="h-5 w-5 text-slate-400" />}
      </div>
      <p className="text-sm font-medium text-slate-200">{title}</p>
      {description && <p className="text-xs text-slate-400 mt-1 max-w-sm">{description}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  );
}
