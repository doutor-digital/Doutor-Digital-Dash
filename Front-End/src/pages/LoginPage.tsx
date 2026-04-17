import { FormEvent, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Gauge, Lock, Mail } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { useAuth } from "@/hooks/useAuth";
import { useClinic } from "@/hooks/useClinic";
import { toast } from "sonner";
import { cn } from "@/lib/utils";
import { authService } from "@/services/auth";

export default function LoginPage() {
  const { login } = useAuth();
  const { setContext } = useClinic();
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [loading, setLoading] = useState(false);

  async function onSubmit(e: FormEvent) {
  e.preventDefault();

  if (!email || !password) return;

  setLoading(true);

  try {
    const data = await authService.login({
      email,
      password,
    });

    // 🔐 salva sessão
    login(
      {
        name: data.userName,
        email: data.email,
        role: data.role,
      },
      data.accessToken
    );

    // 🏥 seta unidade correta
    setContext(
      data.selectedUnit.clinicId,
      data.selectedUnit.id
    );

    toast.success("Login realizado com sucesso!");

    navigate("/select-unit");

  } catch (err: any) {
    toast.error("Email ou senha inválidos");
  } finally {
    setLoading(false);
  }
}

return (
  <div className="min-h-screen grid lg:grid-cols-2 bg-white">

    {/* ══ LADO ESQUERDO — Hero com imagem ══════════════════════ */}
    <div className="hidden lg:flex flex-col justify-between p-10 relative overflow-hidden">

      {/* Imagem de fundo */}
      <img
        src="https://i.postimg.cc/xjx4m8p5/Copia-de-logo-cor-original.png"
        alt=""
        aria-hidden
        className="absolute inset-0 h-full w-full object-contain object-center"
      />

      {/* Overlay escuro para legibilidade — tons do azul da marca */}
      <div className="absolute inset-0 bg-gradient-to-br from-[#020B18]/95 via-[#052040]/80 to-[#020B18]/90" />

      {/* Brilho azul canto superior direito */}
      <div className="pointer-events-none absolute -top-32 -right-32 h-96 w-96 rounded-full bg-[#0077CC]/20 blur-3xl" />
      {/* Brilho azul claro canto inferior esquerdo */}
      <div className="pointer-events-none absolute -bottom-32 -left-32 h-80 w-80 rounded-full bg-[#00AAFF]/10 blur-3xl" />

      {/* ── Logo ── */}
      <div className="relative flex items-center gap-3">
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
        <span className="text-[15px] font-bold text-white tracking-tight">
          Doutor Digital
        </span>
      </div>

      {/* ── Headline ── */}
      <div className="relative space-y-5">
        <h2 className="text-3xl font-bold tracking-tight leading-snug text-white">
          Performance real de cada lead,
          <br />
          <span className="text-[#4DB8FF]">da origem ao fechamento.</span>
        </h2>

        <p className="text-sm text-slate-300 max-w-md leading-relaxed">
          Centralize webhooks da Cloudia e da Meta, acompanhe a evolução por
          etapa e descubra quais campanhas realmente convertem.
        </p>

        <ul className="space-y-2.5">
          {[
            "Funil de conversão consolidado",
            "Métricas ao vivo dos atendentes",
            "Alertas de SLA em tempo real",
            "Relatórios PDF automatizados",
          ].map((t) => (
            <li key={t} className="flex items-center gap-2.5 text-sm text-slate-300">
              <span className="flex h-4 w-4 shrink-0 items-center justify-center rounded-full bg-[#0077CC]/30 ring-1 ring-[#0077CC]/50">
                <span className="h-1.5 w-1.5 rounded-full bg-[#4DB8FF]" />
              </span>
              {t}
            </li>
          ))}
        </ul>
      </div>

      {/* ── Rodapé ── */}
      <div className="relative text-xs text-slate-600">
        © {new Date().getFullYear()} Doutor Digital — Todos os direitos reservados
      </div>
    </div>

    {/* ══ LADO DIREITO — Formulário ════════════════════════════ */}
    <div className="relative flex items-center justify-center p-6 bg-[#020B18]">

      {/* Glow azul sutil atrás do form */}
      <div className="pointer-events-none absolute top-1/2 left-1/2 h-80 w-80 -translate-x-1/2 -translate-y-1/2 rounded-full bg-[#0077CC]/8 blur-3xl" />

      <form onSubmit={onSubmit} className="relative w-full max-w-sm space-y-5">

        {/* Header */}
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-white">
            Entrar
          </h1>
          <p className="mt-1 text-sm text-slate-500">
            Acesse o painel com suas credenciais.
          </p>
        </div>

        {/* Campos */}
        <div className="space-y-3">
          <div>
            <label className="text-[11px] font-semibold uppercase tracking-widest text-slate-500">
              E-mail
            </label>
            <div className="mt-1.5">
              <Input
                type="email"
                icon={<Mail className="h-4 w-4" />}
                placeholder="voce@empresa.com"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                autoFocus
                required
              />
            </div>
          </div>

          <div>
            <label className="text-[11px] font-semibold uppercase tracking-widest text-slate-500">
              Senha
            </label>
            <div className="mt-1.5">
              <Input
                type="password"
                icon={<Lock className="h-4 w-4" />}
                placeholder="••••••••"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
              />
            </div>
          </div>
        </div>

        {/* CTA */}
        <Button
          type="submit"
          className="w-full justify-center bg-[#0077CC] hover:bg-[#0088EE] text-white font-semibold shadow-[0_0_20px_rgba(0,119,204,0.35)] transition-all duration-200"
          loading={loading}
        >
          Entrar no painel
        </Button>

        <p className="text-center text-[11px] text-slate-600">
          Sessão local enquanto a autenticação JWT não está no backend.
        </p>
      </form>
    </div>
  </div>
);
}
