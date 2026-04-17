import { useQuery } from "@tanstack/react-query";
import { Link, useParams } from "react-router-dom";
import {
  AlertTriangle,
  ArrowLeft,
  Clock,
  MessageSquare,
  Phone,
  Tag,
  Timer,
  User2,
} from "lucide-react";
import { PageHeader } from "@/components/layout/PageHeader";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { Badge, StageBadge, StateBadge } from "@/components/ui/Badge";
import { EmptyState } from "@/components/ui/EmptyState";
import { analyticsService } from "@/services/analytics";
import { assignmentsService, type AssignmentLeadHistoryItem } from "@/services/assignments";
import { webhooksService } from "@/services/webhooks";
import { formatDate, formatDuration } from "@/lib/utils";
import { useClinic } from "@/hooks/useClinic";

export default function LeadDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { tenantId, unitId } = useClinic();

  const metrics = useQuery({
    queryKey: ["lead-metrics", id],
    queryFn: () => analyticsService.leadMetrics(id!),
    enabled: !!id,
  });

  const history = useQuery({
    queryKey: ["lead-history", id, unitId],
    queryFn: () => assignmentsService.leadHistory(id!, unitId || undefined),
    enabled: !!id,
  });

  // Busca o lead na lista (fallback, já que não há endpoint /leads/{id} no backend).
  const lead = useQuery({
    queryKey: ["lead-find", id],
    queryFn: async () => {
      const all = await webhooksService.listLeads();
      const numericId = Number(id);
      return all.find((l) => l.id === numericId || l.externalId === numericId);
    },
    enabled: !!id,
  });

  const m = metrics.data;
  const l = lead.data;

  return (
    <>
      <PageHeader
        title={l?.name ?? "Lead"}
        description={`ID: ${id}`}
        actions={
          <Link to="/leads">
            <Button variant="outline" size="sm">
              <ArrowLeft className="h-4 w-4" /> Voltar
            </Button>
          </Link>
        }
      />

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <Card className="lg:col-span-1">
          <CardHeader title="Perfil" subtitle="Dados de contato" />
          <CardBody className="space-y-3">
            <Row icon={<User2 className="h-4 w-4" />} label="Nome" value={l?.name} />
            <Row icon={<Phone className="h-4 w-4" />} label="Telefone" value={l?.phone} />
            <Row icon={<Tag className="h-4 w-4" />} label="Origem" value={l?.source} />
            <div className="flex items-center justify-between">
              <span className="text-xs text-slate-400">Estado</span>
              <StateBadge state={l?.conversationState ?? m?.currentState} />
            </div>
            <div className="flex items-center justify-between">
              <span className="text-xs text-slate-400">Etapa</span>
              <StageBadge stage={l?.currentStage} />
            </div>
            <div className="flex items-center justify-between">
              <span className="text-xs text-slate-400">Atendente</span>
              <span className="text-sm text-slate-200">{l?.attendantName ?? "—"}</span>
            </div>
            <Row
              icon={<Clock className="h-4 w-4" />}
              label="Criado em"
              value={formatDate(l?.createdAt)}
            />
          </CardBody>
        </Card>

        <Card className="lg:col-span-2">
          <CardHeader title="Métricas de tempo" subtitle="Tempo gasto em cada estágio" />
          <CardBody>
            <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
              <TimeBlock
                label="No bot"
                value={m?.timeInBot}
                tone="violet"
                icon={<Timer className="h-4 w-4" />}
              />
              <TimeBlock
                label="Na fila"
                value={m?.timeInQueue}
                tone="amber"
                icon={<Timer className="h-4 w-4" />}
              />
              <TimeBlock
                label="Em atendimento"
                value={m?.timeInService}
                tone="blue"
                icon={<Timer className="h-4 w-4" />}
              />
              <TimeBlock
                label="Total"
                value={m?.totalTime}
                tone="emerald"
                icon={<Timer className="h-4 w-4" />}
              />
              <TimeBlock
                label="1º atendimento"
                value={m?.timeToFirstAttendance ?? undefined}
                tone="blue"
                icon={<Clock className="h-4 w-4" />}
              />
              <TimeBlock
                label="Resolução"
                value={m?.timeToResolution ?? undefined}
                tone="emerald"
                icon={<Clock className="h-4 w-4" />}
              />
            </div>

            {m?.alerts && m.alerts.length > 0 && (
              <div className="mt-4 rounded-lg border border-amber-500/30 bg-amber-500/10 p-3">
                <div className="flex items-center gap-2 text-amber-300 font-medium text-sm">
                  <AlertTriangle className="h-4 w-4" /> Alertas ativos
                </div>
                <ul className="mt-2 space-y-1 text-xs text-amber-200">
                  {m.alerts.map((a, i) => (
                    <li key={i}>• {a}</li>
                  ))}
                </ul>
              </div>
            )}
          </CardBody>
        </Card>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 mt-4">
        <Card>
          <CardHeader title="Linha do tempo" subtitle="Transições entre estados" />
          <CardBody>
            {metrics.isLoading ? (
              <div className="skeleton h-40 w-full rounded" />
            ) : m?.transitions && m.transitions.length > 0 ? (
              <ol className="relative border-l border-white/10 ml-2 space-y-4">
                {m.transitions.map((t, i) => (
                  <li key={i} className="ml-4">
                    <div className="absolute -left-1.5 h-3 w-3 rounded-full bg-brand-500 ring-4 ring-brand-500/20" />
                    <div className="text-xs text-slate-400">{formatDate(t.at)}</div>
                    <div className="text-sm text-slate-100">
                      <StateBadge state={t.from} /> <span className="opacity-60">→</span>{" "}
                      <StateBadge state={t.to} />
                    </div>
                  </li>
                ))}
              </ol>
            ) : (
              <EmptyState title="Sem transições registradas" />
            )}
          </CardBody>
        </Card>

        <Card>
          <CardHeader title="Histórico de atendentes" subtitle="Atribuições realizadas" />
          <CardBody>
            {history.isLoading ? (
              <div className="skeleton h-40 w-full rounded" />
            ) : history.data && history.data.length > 0 ? (
              <ul className="space-y-3">
                {history.data.map((h: AssignmentLeadHistoryItem, i: number) => (
                  <li key={i} className="flex items-start gap-3">
                    <div className="h-8 w-8 rounded-full bg-gradient-to-br from-brand-400 to-violet-600 grid place-items-center text-xs font-semibold shrink-0">
                      {(h.attendantName ?? "?").charAt(0).toUpperCase()}
                    </div>
                    <div className="min-w-0">
                      <p className="text-sm text-slate-100">
                        {h.attendantName ?? "Atendente"}
                      </p>
                      <p className="text-xs text-slate-400">
                        {formatDate((h.assignedAt as string | undefined) ?? (h.createdAt as string | undefined))}
                      </p>
                    </div>
                  </li>
                ))}
              </ul>
            ) : (
              <EmptyState title="Sem histórico de atendentes" />
            )}
          </CardBody>
        </Card>

        <Card className="lg:col-span-2">
          <CardHeader title="Interações" subtitle="Mensagens e eventos" />
          <CardBody>
            {m?.interactions && m.interactions.length > 0 ? (
              <ul className="space-y-2">
                {m.interactions.map((it) => (
                  <li
                    key={it.id}
                    className="flex items-start gap-3 p-3 rounded-lg bg-white/[0.02] border border-white/5"
                  >
                    <MessageSquare className="h-4 w-4 text-slate-400 mt-0.5" />
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2 text-xs text-slate-400">
                        <Badge tone="slate">{it.type}</Badge>
                        <span>{formatDate(it.at)}</span>
                      </div>
                      {it.content && (
                        <p className="text-sm text-slate-200 mt-1 break-words">
                          {it.content}
                        </p>
                      )}
                    </div>
                  </li>
                ))}
              </ul>
            ) : (
              <EmptyState title="Nenhuma interação registrada" />
            )}
          </CardBody>
        </Card>
      </div>
    </>
  );
}

function Row({
  icon,
  label,
  value,
}: {
  icon: React.ReactNode;
  label: string;
  value?: string | null;
}) {
  return (
    <div className="flex items-center justify-between">
      <span className="text-xs text-slate-400 flex items-center gap-1.5">
        <span className="text-slate-500">{icon}</span>
        {label}
      </span>
      <span className="text-sm text-slate-200 truncate max-w-[60%]">{value ?? "—"}</span>
    </div>
  );
}

function TimeBlock({
  label,
  value,
  tone,
  icon,
}: {
  label: string;
  value?: number;
  tone: "violet" | "amber" | "blue" | "emerald";
  icon: React.ReactNode;
}) {
  const tones = {
    violet: "from-violet-500/15 text-violet-300",
    amber: "from-amber-500/15 text-amber-300",
    blue: "from-brand-500/15 text-brand-300",
    emerald: "from-emerald-500/15 text-emerald-300",
  };
  return (
    <div
      className={`rounded-lg border border-white/10 bg-gradient-to-br ${tones[tone].split(" ")[0]} to-transparent p-3`}
    >
      <div className="flex items-center justify-between">
        <span className="text-xs text-slate-300">{label}</span>
        <span className={tones[tone].split(" ")[1]}>{icon}</span>
      </div>
      <div className="text-xl font-semibold mt-1">{formatDuration(value)}</div>
    </div>
  );
}
