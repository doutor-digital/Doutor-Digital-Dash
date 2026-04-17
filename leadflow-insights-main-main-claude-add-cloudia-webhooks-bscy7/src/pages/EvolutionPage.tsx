import { useQuery } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { PageHeader } from "@/components/layout/PageHeader";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { Input } from "@/components/ui/Input";
import { Button } from "@/components/ui/Button";
import { EvolutionLine } from "@/components/charts/EvolutionLine";
import { EmptyState } from "@/components/ui/EmptyState";
import { webhooksService } from "@/services/webhooks";
import { useClinic } from "@/hooks/useClinic";
import { formatNumber } from "@/lib/utils";

export default function EvolutionPage() {
  const { clinicId } = useClinic();
  const defaultRange = useMemo(() => {
    const end = new Date();
    const start = new Date();
    start.setMonth(start.getMonth() - 11);
    start.setDate(1);
    return {
      start: start.toISOString().slice(0, 10),
      end: end.toISOString().slice(0, 10),
    };
  }, []);

  const [range, setRange] = useState(defaultRange);

  const serie = useQuery({
    queryKey: ["evolution", clinicId, range],
    queryFn: () =>
      webhooksService.buscarInicioFim({
        clinicId: clinicId || undefined,
        dataInicio: range.start,
        dataFim: range.end,
      }),
  });

  const total = (serie.data ?? []).reduce((acc, d) => acc + d.total, 0);
  const media =
    serie.data && serie.data.length > 0 ? total / serie.data.length : 0;

  return (
    <>
      <PageHeader
        title="Evolução temporal"
        description="Volume de leads ao longo do tempo"
        actions={
          <div className="flex items-center gap-2">
            <Input
              type="date"
              value={range.start}
              onChange={(e) => setRange((r) => ({ ...r, start: e.target.value }))}
            />
            <span className="text-slate-400 text-sm">→</span>
            <Input
              type="date"
              value={range.end}
              onChange={(e) => setRange((r) => ({ ...r, end: e.target.value }))}
            />
            <Button variant="outline" size="sm" onClick={() => setRange(defaultRange)}>
              Resetar
            </Button>
          </div>
        }
      />

      <div className="grid grid-cols-2 md:grid-cols-3 gap-3 mb-4">
        <Mini label="Período" value={`${range.start} → ${range.end}`} />
        <Mini label="Total" value={formatNumber(total)} />
        <Mini label="Média/mês" value={formatNumber(Math.round(media))} />
      </div>

      <Card>
        <CardHeader title="Captação mensal" subtitle="Soma por mês dentro do intervalo" />
        <CardBody>
          {serie.isLoading ? (
            <div className="skeleton h-72 w-full rounded" />
          ) : (serie.data?.length ?? 0) > 0 ? (
            <EvolutionLine data={serie.data!} />
          ) : (
            <EmptyState title="Sem dados no período escolhido" />
          )}
        </CardBody>
      </Card>
    </>
  );
}

function Mini({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-white/10 bg-white/[0.03] p-3">
      <div className="label">{label}</div>
      <div className="mt-1 text-lg font-semibold text-slate-50">{value}</div>
    </div>
  );
}
