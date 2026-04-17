import { useQuery } from "@tanstack/react-query";
import { Radio, RefreshCw, Timer, Users } from "lucide-react";
import { PageHeader } from "@/components/layout/PageHeader";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { KpiCard } from "@/components/kpi/KpiCard";
import { Table, TBody, THead, Td, Th, Tr } from "@/components/ui/Table";
import { EmptyState } from "@/components/ui/EmptyState";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { metricsService } from "@/services/metrics";
import { useClinic } from "@/hooks/useClinic";
import { formatDuration, formatNumber } from "@/lib/utils";

export default function LiveMetricsPage() {
  const { clinicId } = useClinic();

  const resumo = useQuery({
    queryKey: ["live-resumo", clinicId],
    queryFn: () => metricsService.resumo(clinicId || undefined),
    refetchInterval: 15_000,
  });
  const fila = useQuery({
    queryKey: ["live-fila", clinicId],
    queryFn: () => metricsService.fila(clinicId || undefined),
    refetchInterval: 15_000,
  });
  const completo = useQuery({
    queryKey: ["live-completo", clinicId],
    queryFn: () => metricsService.completo(clinicId || undefined),
    refetchInterval: 30_000,
  });

  return (
    <>
      <PageHeader
        title="Métricas ao vivo"
        description="Dados em tempo real da Cloudia"
        actions={
          <div className="flex items-center gap-2">
            <Badge tone="green">
              <Radio className="h-3 w-3 animate-pulse" /> Sincronizado
            </Badge>
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                resumo.refetch();
                fila.refetch();
                completo.refetch();
              }}
            >
              <RefreshCw className="h-4 w-4" /> Atualizar
            </Button>
          </div>
        }
      />

      <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-4">
        <KpiCard
          label="Em atendimento"
          value={resumo.data?.totalEmAtendimento ?? 0}
          icon={<Users className="h-5 w-5" />}
          tone="blue"
          loading={resumo.isLoading}
        />
        <KpiCard
          label="Na fila"
          value={resumo.data?.totalNaFila ?? 0}
          icon={<Timer className="h-5 w-5" />}
          tone="amber"
          loading={resumo.isLoading}
        />
        <KpiCard
          label="Tempo médio"
          value={
            resumo.data?.tempoMedio != null
              ? formatDuration(resumo.data.tempoMedio)
              : "—"
          }
          tone="violet"
          loading={resumo.isLoading}
        />
        <KpiCard
          label="Atendentes online"
          value={resumo.data?.atendentes?.length ?? 0}
          tone="green"
          loading={resumo.isLoading}
        />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <Card>
          <CardHeader
            title="Fila de espera"
            subtitle="Ordenados pelo maior tempo aguardando"
          />
          <CardBody className="p-0">
            {fila.isLoading ? (
              <div className="p-4 space-y-2">
                {Array.from({ length: 4 }).map((_, i) => (
                  <div key={i} className="skeleton h-8 w-full rounded" />
                ))}
              </div>
            ) : fila.data?.fila?.length ? (
              <Table>
                <THead>
                  <Tr>
                    <Th>Lead</Th>
                    <Th>Telefone</Th>
                    <Th>Aguardando há</Th>
                  </Tr>
                </THead>
                <TBody>
                  {fila.data.fila.map((f, i) => (
                    <Tr key={i}>
                      <Td className="font-medium">{f.name}</Td>
                      <Td className="text-slate-300">{f.phone ?? "—"}</Td>
                      <Td>
                        <Badge
                          tone={
                            (f.waitingMinutes ?? 0) > 15
                              ? "red"
                              : (f.waitingMinutes ?? 0) > 5
                              ? "yellow"
                              : "slate"
                          }
                        >
                          {formatDuration(f.waitingMinutes)}
                        </Badge>
                      </Td>
                    </Tr>
                  ))}
                </TBody>
              </Table>
            ) : (
              <EmptyState title="Fila vazia" />
            )}
          </CardBody>
        </Card>

        <Card>
          <CardHeader title="Atendentes" subtitle="Status atual" />
          <CardBody className="p-0">
            {resumo.isLoading ? (
              <div className="p-4 space-y-2">
                {Array.from({ length: 4 }).map((_, i) => (
                  <div key={i} className="skeleton h-8 w-full rounded" />
                ))}
              </div>
            ) : resumo.data?.atendentes?.length ? (
              <Table>
                <THead>
                  <Tr>
                    <Th>Nome</Th>
                    <Th>Status</Th>
                    <Th>Em atend.</Th>
                    <Th>Na fila</Th>
                    <Th>Tempo médio</Th>
                  </Tr>
                </THead>
                <TBody>
                  {resumo.data.atendentes.map((a, i) => (
                    <Tr key={i}>
                      <Td className="font-medium">{a.name}</Td>
                      <Td>
                        <Badge tone={a.status === "online" ? "green" : "slate"}>
                          {a.status ?? "—"}
                        </Badge>
                      </Td>
                      <Td>{formatNumber(a.emAtendimento ?? 0)}</Td>
                      <Td>{formatNumber(a.naFila ?? 0)}</Td>
                      <Td>{formatDuration(a.tempoMedio)}</Td>
                    </Tr>
                  ))}
                </TBody>
              </Table>
            ) : (
              <EmptyState title="Sem atendentes ativos" />
            )}
          </CardBody>
        </Card>
      </div>

      {completo.data && (
        <Card className="mt-4">
          <CardHeader
            title="Cruzamento ao vivo × banco"
            subtitle="Conversões fechadas cruzadas com dados em tempo real"
          />
          <CardBody>
            <pre className="text-xs text-slate-300 bg-white/[0.02] rounded-lg p-3 overflow-auto max-h-80">
              {JSON.stringify(completo.data, null, 2)}
            </pre>
          </CardBody>
        </Card>
      )}
    </>
  );
}
