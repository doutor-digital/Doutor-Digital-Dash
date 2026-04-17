import { useQuery } from "@tanstack/react-query";
import { PageHeader } from "@/components/layout/PageHeader";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { FunnelChart } from "@/components/charts/FunnelChart";
import { StageBarChart } from "@/components/charts/StageBarChart";
import { KpiCard } from "@/components/kpi/KpiCard";
import { useClinic } from "@/hooks/useClinic";
import { webhooksService } from "@/services/webhooks";
import { formatPercent } from "@/lib/utils";

export default function FunnelPage() {
  const { clinicId } = useClinic();

  const states = useQuery({
    queryKey: ["funnel-states", clinicId],
    queryFn: () => webhooksService.countByState(clinicId || undefined),
  });
  const sem = useQuery({
    queryKey: ["funnel-sem", clinicId],
    queryFn: () => webhooksService.semPagamento(clinicId || undefined),
  });
  const com = useQuery({
    queryKey: ["funnel-com", clinicId],
    queryFn: () => webhooksService.comPagamento(clinicId || undefined),
  });
  const consultas = useQuery({
    queryKey: ["funnel-consultas", clinicId],
    queryFn: () => webhooksService.consultas(clinicId || undefined),
  });
  const etapa = useQuery({
    queryKey: ["funnel-etapa", clinicId],
    queryFn: () => webhooksService.etapaAgrupada(clinicId || undefined),
  });

  const total = states.data?.total ?? 0;
  const taxaAgendamento =
    total > 0 ? (((sem.data ?? 0) + (com.data ?? 0)) / total) * 100 : 0;
  const taxaPagamento =
    total > 0 ? ((com.data ?? 0) / total) * 100 : 0;
  const taxaFechamento = total > 0 ? ((consultas.data ?? 0) / total) * 100 : 0;

  return (
    <>
      <PageHeader
        title="Funil de conversão"
        description="Acompanhe cada degrau da jornada do lead"
      />

      <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-4">
        <KpiCard label="Total" value={total} tone="blue" loading={states.isLoading} />
        <KpiCard
          label="Taxa de agendamento"
          value={formatPercent(taxaAgendamento)}
          tone="amber"
        />
        <KpiCard
          label="Pagamento na hora"
          value={formatPercent(taxaPagamento)}
          tone="violet"
        />
        <KpiCard
          label="Fechamento"
          value={formatPercent(taxaFechamento)}
          tone="green"
        />
      </div>

      <Card className="mb-4">
        <CardHeader
          title="Funil consolidado"
          subtitle="Drop-off percentual entre etapas"
        />
        <CardBody>
          <FunnelChart
            stages={[
              { label: "Leads recebidos", count: total, tone: "blue" },
              { label: "Agendados sem pagamento", count: sem.data ?? 0, tone: "amber" },
              { label: "Agendados com pagamento", count: com.data ?? 0, tone: "violet" },
              { label: "Fechou / em tratamento", count: consultas.data ?? 0, tone: "emerald" },
            ]}
          />
        </CardBody>
      </Card>

      <Card>
        <CardHeader
          title="Distribuição por etapa"
          subtitle="Situação atual agrupada pelo CurrentStage"
        />
        <CardBody>
          {etapa.isLoading ? (
            <div className="skeleton h-72 w-full rounded" />
          ) : (
            <StageBarChart data={etapa.data ?? []} />
          )}
        </CardBody>
      </Card>
    </>
  );
}
