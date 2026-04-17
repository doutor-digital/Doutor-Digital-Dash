import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { LogOut, MessageCircle, Search, Sparkles } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { unitsService } from "@/services/units";
import { useClinic } from "@/hooks/useClinic";
import { useAuth } from "@/hooks/useAuth";

interface UnitOption {
  id: string | number;
  clinicId: number;
  name: string;
  logo: string;
}

const fallbackUnits: UnitOption[] = [
  {
    id: "araguaina",
    clinicId: 8020,
    name: "Doutor Hérnia Unidade Araguaína",
    logo: "https://i0.wp.com/www.cloudia.com.br/wp-content/uploads/2024/01/Logo-800-sem-fundo.png",
  },
  { id: "maraba", clinicId: 8021, name: "DOUTOR HÉRNIA UNIDADE MARABÁ", logo: "/assets/logo-maraba.png" },
  { id: "parauapebas", clinicId: 8022, name: "DOUTOR HÉRNIA UNIDADE PARAUAPEBAS", logo: "/assets/logo-parauapebas.png" },
  { id: "imperatriz", clinicId: 8023, name: "DOUTOR HÉRNIA IMPERATRIZ", logo: "/assets/logo-10anos.png" },
  { id: "canaa", clinicId: 8024, name: "DOUTOR HÉRNIA CANAÃ", logo: "/assets/logo-10anos.png" },
  { id: "balsas", clinicId: 8025, name: "DOUTOR HÉRNIA BALSAS", logo: "/assets/logo-10anos.png" },
  { id: "trauma", clinicId: 8026, name: "INSTITUTO TRAUMA", logo: "/assets/logo-trauma.png" },
];

export default function UnitSelectPage() {
  const navigate = useNavigate();
  const { logout } = useAuth();
  const { setContext } = useClinic();
  const [search, setSearch] = useState("");

  const units = useQuery({
    queryKey: ["units", "selector"],
    queryFn: () => unitsService.list(),
    retry: false,
  });

  const options = useMemo<UnitOption[]>(() => {
  const fromApi = (units.data ?? [])
    .map((unit): UnitOption | null => {
      const clinicId =
        typeof unit.clinicId === "number" ? unit.clinicId : Number(unit.clinicId);

      if (Number.isNaN(clinicId)) return null;

      const fallbackMatch = fallbackUnits.find((f) => f.clinicId === clinicId);

      return {
        id: unit.id,
        clinicId,
        name: unit.name?.trim() || `Unidade ${clinicId}`,
        logo: unit.logo_url || fallbackMatch?.logo || "/assets/default-logo.png",
      };
    })
    .filter((item): item is UnitOption => item !== null);

  const combined = fromApi.length ? fromApi : fallbackUnits;

  return combined.filter((item) => {
    const normalized = `${item.name} ${item.clinicId}`.toLowerCase();
    return normalized.includes(search.toLowerCase());
  });
}, [search, units.data]);
  return (
    <div className="relative min-h-screen w-full bg-[#f6f7fb] py-4 sm:py-6">
      <div className="min-h-[calc(100vh-2rem)] w-full border-y border-slate-200 bg-white px-4 py-8 shadow-sm sm:px-10 sm:py-10 lg:px-16 lg:py-12 xl:border-x xl:rounded-[32px]">
        <div className="flex items-center justify-end">
          <button
            onClick={logout}
            className="flex items-center gap-2 rounded-xl px-3 py-2 text-sm font-semibold text-slate-800 transition hover:bg-slate-50"
          >
            <LogOut className="h-5 w-5" />
            Sair
          </button>
        </div>

        <div className="mt-6 text-center">
          <h1 className="text-3xl font-bold tracking-tight text-slate-800 sm:text-4xl">
            Escolha uma conta
          </h1>
        </div>

        <div className="mx-auto mt-10 max-w-[500px]">
          <div className="relative">
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Pesquisar sua conta"
              className="h-12 w-full rounded-lg border border-slate-300 bg-white py-3 pl-4 pr-12 text-sm text-slate-700 outline-none transition focus:border-blue-500 focus:ring-2 focus:ring-blue-200"
            />
            <Search className="absolute right-4 top-1/2 h-5 w-5 -translate-y-1/2 text-slate-400" />
          </div>
        </div>

        <div className="mt-16 grid grid-cols-1 gap-x-6 gap-y-12 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 2xl:grid-cols-6">
          {options.map((unit) => (
            <button
              key={unit.id}
              onClick={() => {
                setContext(unit.clinicId, Number(unit.id));
                navigate("/");
              }}
              className="group flex flex-col items-center justify-start text-center transition-transform duration-200 hover:-translate-y-1"
            >
              <div className="flex h-36 w-36 items-center justify-center overflow-hidden rounded-full bg-slate-50 p-4 transition duration-200 group-hover:bg-white group-hover:shadow-lg sm:h-40 sm:w-40">
                <img
                  src={unit.logo}
                  alt={unit.name}
                  className="h-full w-full object-contain"
                  onError={(e) => {
                    const target = e.currentTarget;
                    target.style.display = "none";
                    target.parentElement?.classList.add("border-2", "border-slate-200", "bg-slate-100");
                  }}
                />
              </div>

              <p className="mt-6 w-full px-2 text-[13px] font-extrabold uppercase leading-snug text-slate-800 sm:text-[15px]">
                {unit.name}
              </p>
            </button>
          ))}
        </div>
      </div>

      <div className="fixed bottom-6 right-6 z-50 hidden xl:block">
        <div className="w-[320px] rounded-[24px] border border-slate-200 bg-white/95 p-4 shadow-[0_20px_50px_rgba(0,0,0,0.15)] backdrop-blur-md">
          <div className="flex items-center gap-3 border-b border-slate-100 pb-3">
            <div className="flex h-11 w-11 items-center justify-center rounded-2xl bg-blue-100 text-blue-600">
              <Sparkles className="h-5 w-5" />
            </div>
            <div>
              <p className="text-sm font-bold text-slate-800">Assistente IA</p>
              <p className="text-xs text-slate-500">Interação rápida</p>
            </div>
          </div>
          <div className="mt-4 space-y-3">
            <div className="rounded-2xl bg-slate-50 p-3 text-sm leading-relaxed text-slate-600">
              Olá! Posso ajudar você a encontrar a unidade correta ou tirar dúvidas sobre o acesso.
            </div>
            <button className="flex w-full items-center justify-center gap-2 rounded-2xl bg-blue-600 px-4 py-3 text-sm font-semibold text-white hover:bg-blue-700 transition-colors">
              <MessageCircle className="h-4 w-4" />
              Iniciar conversa
            </button>
          </div>
        </div>
      </div>

      <div className="fixed bottom-6 right-6 z-50 flex items-center gap-3 xl:hidden">
        <div className="hidden rounded-full bg-blue-600 px-4 py-2 text-xs font-medium text-white shadow-lg sm:block">
          Precisa de ajuda?
        </div>
        <button className="flex h-14 w-14 items-center justify-center rounded-full bg-blue-600 text-white shadow-2xl transition hover:scale-105 active:scale-95">
          <Sparkles className="h-6 w-6" />
        </button>
      </div>
    </div>
  );
}
