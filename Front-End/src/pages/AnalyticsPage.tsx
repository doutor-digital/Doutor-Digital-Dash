import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { PageHeader } from "@/components/layout/PageHeader";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Button } from "@/components/ui/Button";
import { KpiCard } from "@/components/kpi/KpiCard";
import { Table, TBody, THead, Td, Th, Tr } from "@/components/ui/Table";
import { StateBadge } from "@/components/ui/Badge";
import { EmptyState } from "@/components/ui/EmptyState";
import { analyticsService } from "@/services/analytics";
import { formatDuration, formatNumber } from "@/lib/utils";

export default function AnalyticsPage() {
  const [unitId, setUnitId] = useState(
    localStorage.getItem("lf.analytics.unitId") ?? ""
  );
  const [startDate, setStartDate] = useState(() => {
    const d = new Date();
    d.setDate(d.getDate() - 30);
    return d.toISOString().slice(0, 10);
  });
  const [endDate, setEndDate] = useState(() =>
    new Date().toISOString().slice(0, 10)
  );
  const [state, setState] = useState("");

  const summary = useQuery({
    queryKey: ["unit-summary", unitId, startDate, endDate],
    queryFn: () => analyticsService.unitSummary(unitId, { startDate, endDate }),
    enabled: !!unitId,
  });

  const leads = useQuery({
    queryKey: ["unit-leads-metrics", unitId, startDate, endDate, state],
    queryFn: () =>
      analyticsService.unitLeadsMetrics(unitId, {
        startDate,
        endDate,
        state: state || undefined,
      }),
    enabled: !!unitId,
  });

  return (
    <>
      <PageHeader
        title="Analytics"
        description="Métricas profundas por unidade (dados locais do banco)"
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <Input
              className="w-48"
              placeholder="Unit ID"
              value={unitId}
              onChange={(e) => {
                setUnitId(e.target.value);
                localStorage.setItem("lf.analytics.unitId", e.target.value);
              }}
            />
            <Input
              type="date"
              value={startDate}
              onChange={(e) => setStartDate(e.target.value)}
            />
            <span className="text-slate-400 text-sm">→</span>
            <Input
              type="date"
              value={endDate}
              onChange={(e) => setEndDate(e.target.value)}
            />
            <Select value={state} onChange={(e) => setState(e.target.value)}>
              <option value="">Todos estados</option>
              <option value="bot">Bot</option>
              <option value="queue">Fila</option>
              <option value="service">Atendimento</option>
              <option value="concluido">Concluído</option>
            </Select>
            <Button variant="outline" size="sm" onClick={() => summary.refetch()}>
              Aplicar
            </Button>
          </div>
        }
      />

      {!unitId ? (
        <Card className="p-8">
          <EmptyState
            title="Informe um Unit ID"
            description="As métricas analíticas são consultadas por unidade. Use o campo acima para carregar."
          />
        </Card>
      ) : (
        <>
          <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-4">
            <KpiCard
              label="Total no período"
              value={summary.data?.totalLeads ?? 0}
              tone="blue"
              loading={summary.isLoading}
            />
            <KpiCard
              label="Em atendimento"
              value={summary.data?.totals?.service ?? 0}
              tone="violet"
              loading={summary.isLoading}
            />
            <KpiCard
              label="Concluídos"
              value={summary.data?.totals?.concluido ?? 0}
              tone="green"
              loading={summary.isLoading}
            />
            <KpiCard
              label="Com alertas"
              value={summary.data?.alertsCount ?? 0}
              tone="red"
              loading={summary.isLoading}
            />
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-3 gap-4 mb-4">
            <Card>
              <CardHeader title="Tempo médio" subtitle="Por estado" />
              <CardBody className="space-y-3">
                {["bot", "queue", "service"].map((s) => (
                  <Row
                    key={s}
                    label={labelState(s)}
                    value={formatDuration(summary.data?.averages?.[s as never])}
                  />
                ))}
                <Row
                  label="Até 1º atendimento"
                  value={formatDuration(summary.data?.averages?.firstAttendance)}
                />
                <Row
                  label="Até resolução"
                  value={formatDuration(summary.data?.averages?.resolution)}
                />
              </CardBody>
            </Card>

            <Card className="lg:col-span-2">
              <CardHeader title="Top atendentes" subtitle="Performance por conversão" />
              <CardBody className="p-0">
                {summary.data?.topAttendants?.length ? (
                  <Table>
                    <THead>
                      <Tr>
                        <Th>#</Th>
                        <Th>Atendente</Th>
                        <Th>Atribuições</Th>
                        <Th>Conversões</Th>
                        <Th>Taxa</Th>
                      </Tr>
                    </THead>
                    <TBody>
                      {summary.data.topAttendants.map((a, i) => {
                        const rate = a.total > 0 ? (a.conversions / a.total) * 100 : 0;
                        return (
                          <Tr key={i}>
                            <Td>{i + 1}</Td>
                            <Td className="font-medium">{a.attendantName}</Td>
                            <Td>{formatNumber(a.total)}</Td>
                            <Td>{formatNumber(a.conversions)}</Td>
                            <Td>{rate.toFixed(1)}%</Td>
                          </Tr>
                        );
                      })}
                    </TBody>
                  </Table>
                ) : (
                  <EmptyState title="Sem dados de atendentes" />
                )}
              </CardBody>
            </Card>
          </div>

          <Card>
            <CardHeader
              title="Métricas por lead"
              subtitle="Tempo em cada estágio, com estado atual"
            />
            <CardBody className="p-0">
              {leads.isLoading ? (
                <div className="p-4 space-y-2">
                  {Array.from({ length: 5 }).map((_, i) => (
                    <div key={i} className="skeleton h-8 w-full rounded" />
                  ))}
                </div>
              ) : leads.data && leads.data.length > 0 ? (
                <Table>
                  <THead>
                    <Tr>
                      <Th>Lead</Th>
                      <Th>Estado</Th>
                      <Th>Bot</Th>
                      <Th>Fila</Th>
                      <Th>Atend.</Th>
                      <Th>Total</Th>
                      <Th>Alertas</Th>
                    </Tr>
                  </THead>
                  <TBody>
                    {leads.data.map((l) => (
                      <Tr key={l.leadId}>
                        <Td className="font-medium">{l.name ?? l.leadId}</Td>
                        <Td>
                          <StateBadge state={l.currentState} />
                        </Td>
                        <Td>{formatDuration(l.timeInBot)}</Td>
                        <Td>{formatDuration(l.timeInQueue)}</Td>
                        <Td>{formatDuration(l.timeInService)}</Td>
                        <Td>{formatDuration(l.totalTime)}</Td>
                        <Td>
                          {l.alerts && l.alerts.length > 0 ? (
                            <span className="chip bg-red-500/15 text-red-300">
                              {l.alerts.length}
                            </span>
                          ) : (
                            "—"
                          )}
                        </Td>
                      </Tr>
                    ))}
                  </TBody>
                </Table>
              ) : (
                <EmptyState title="Sem leads no período" />
              )}
            </CardBody>
          </Card>
        </>
      )}
    </>
  );
}

function labelState(s: string) {
  return { bot: "Bot", queue: "Fila", service: "Atendimento" }[s] ?? s;
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between border-b border-white/5 pb-2 last:border-0 last:pb-0">
      <span className="text-xs text-slate-400">{label}</span>
      <span className="text-sm font-semibold text-slate-100">{value}</span>
    </div>
  );
}
