import { ReactNode } from "react";

type PageHeaderProps = {
  title: string;
  description?: string;
  actions?: ReactNode;
  badge?: string;
  backgroundImage?: string;   // ← URL da imagem
};

export function PageHeader({
  title,
  description,
  actions,
  badge = "Clínica",
  backgroundImage,
}: PageHeaderProps) {
   return (
    <header className="relative mb-8 overflow-hidden rounded-3xl bg-gradient-to-br fro'm-sky-100 via-sky-100 to-teal-100 p-[1px] shadow-xl shadow-sky-500/20">
      {/* Card interno */}
      <div className="relative overflow-hidden rounded-[23px] bg-white px-7 py-6 md:px-10 md:py-7">

        {/* ── Imagem de fundo (opcional) ── */}
        {backgroundImage && (
          <>
            <img
              src={backgroundImage}
              alt=""
              aria-hidden
              className="absolute inset-0 h-full w-full object-cover object-center"
            />
            {/* Overlay que garante legibilidade sobre qualquer imagem */}
            <div className="absolute inset-0 bg-gradient-to-r from-white/95 via-white/80 to-white/30" />
          </>
        )}

        {/* Círculos decorativos — só aparecem sem imagem */}
        {!backgroundImage && (
          <>
            <div className="pointer-events-none absolute -right-12 -top-12 h-52 w-52 rounded-full bg-gradient-to-br from-sky-100 to-teal-100 opacity-60" />
            <div className="pointer-events-none absolute -bottom-8 right-24 h-32 w-32 rounded-full bg-teal-100/50" />
          </>
        )}

        <div className="relative flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
          <div className="flex items-start gap-4">
            <div className="min-w-0">
              {/* Badge */}
              <span className="mb-1.5 inline-flex items-center gap-1 rounded-full bg-sky-50 px-2.5 py-0.5 text-[10px] font-bold uppercase tracking-widest text-sky-500 ring-1 ring-sky-200">
                {badge}
              </span>

              <h1 className="text-2xl font-extrabold tracking-tight text-slate-800 md:text-3xl">
                {title}
              </h1>

              {description && (
                <p className="mt-1 max-w-xl text-sm leading-6 text-slate-400">
                  {description}
                </p>
              )}
            </div>
          </div>

          {actions && (
            <div className="flex flex-wrap items-center gap-2 md:shrink-0 md:justify-end">
              {actions}
            </div>
          )}
        </div>
      </div>
    </header>
  );
}