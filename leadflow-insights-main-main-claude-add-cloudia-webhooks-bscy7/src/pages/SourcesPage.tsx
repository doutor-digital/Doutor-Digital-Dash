import { useQuery } from "@tanstack/react-query";
import { useMemo } from "react";
import { PageHeader } from "@/components/layout/PageHeader";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { Table, TBody, THead, Td, Th, Tr } from "@/components/ui/Table";
import { Badge } from "@/components/ui/Badge";
import { EmptyState } from "@/components/ui/EmptyState";
import { SourceDonut } from "@/components/charts/SourceDonut";
import { useClinic } from "@/hooks/useClinic";
import { webhooksService } from "@/services/webhooks";
import { formatNumber, formatPercent } from "@/lib/utils";

export default function SourcesPage() {
  const { unitId } = useClinic();

  const origem = useQuery({
    queryKey: ["sources-origem", unitId],
    queryFn: () => webhooksService.origemCloudia(unitId || undefined),
  });
  const source = useQuery({
    queryKey: ["sources-final", unitId],
    queryFn: () => webhooksService.sourceFinal(unitId || undefined),
  });
  const leads = useQuery({
    queryKey: ["sources-leads", unitId],
    queryFn: () => webhooksService.listLeads({ clinicId: unitId || undefined }),
  });

  const converted = useMemo(() => {
    const list = leads.data ?? [];
    const map: Record<string, { total: number; convertidos: number }> = {};
    for (const l of list) {
      const key = (l.source ?? "Sem origem").toString();
      if (!map[key]) map[key] = { total: 0, convertidos: 0 };
      map[key].total += 1;
      const stage = (l.currentStage ?? "").toLowerCase();
      if (stage.includes("fechou") || stage.includes("tratamento")) {
        map[key].convertidos += 1;
      }
    }
    return Object.entries(map)
      .map(([source, v]) => ({
        source,
        total: v.total,
        convertidos: v.convertidos,
        taxa: v.total > 0 ? (v.convertidos / v.total) * 100 : 0,
      }))
      .sort((a, b) => b.total - a.total);
  }, [leads.data]);

  const donutData = useMemo(() => {
    const base = origem.data ?? [];
    return base.slice(0, 10).map((o) => ({
      name: o.origem ?? "—",
      value: o.quantidade ?? 0,
    }));
  }, [origem.data]);

  return (
    <>
      <PageHeader
        title="Origens"
        description="Quais canais trazem — e convertem — mais leads"
      />

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 mb-4">
        <Card>
          <CardHeader title="Distribuição por origem" subtitle="Cloudia /origem-cloudia" />
          <CardBody>
            {origem.isLoading ? (
              <div className="skeleton h-72 w-full rounded" />
            ) : donutData.length ? (
              <SourceDonut data={donutData} />
            ) : (
              <EmptyState title="Sem dados" />
            )}
          </CardBody>
        </Card>

        <Card>
          <CardHeader title="Source final" subtitle="Meta/Google/Direto" />
          <CardBody>
            {source.isLoading ? (
              <div className="skeleton h-72 w-full rounded" />
            ) : source.data && source.data.length > 0 ? (
              <SourceDonut
                data={source.data.map((s) => ({ name: s.source, value: s.count }))}
              />
            ) : (
              <EmptyState title="Sem dados" />
            )}
          </CardBody>
        </Card>
      </div>

      <Card>
        <CardHeader
          title="Performance por origem"
          subtitle="Total de leads × leads fechados"
        />
        <CardBody className="p-0">
          {leads.isLoading ? (
            <div className="p-4 space-y-2">
              {Array.from({ length: 5 }).map((_, i) => (
                <div key={i} className="skeleton h-8 w-full rounded" />
              ))}
            </div>
          ) : converted.length ? (
            <Table>
              <THead>
                <Tr>
                  <Th>Origem</Th>
                  <Th>Total</Th>
                  <Th>Convertidos</Th>
                  <Th>Taxa</Th>
                  <Th>Performance</Th>
                </Tr>
              </THead>
              <TBody>
                {converted.map((row) => (
                  <Tr key={row.source}>
                    <Td className="font-medium">{row.source}</Td>
                    <Td>{formatNumber(row.total)}</Td>
                    <Td>{formatNumber(row.convertidos)}</Td>
                    <Td>
                      <Badge
                        tone={
                          row.taxa >= 30 ? "green" : row.taxa >= 15 ? "yellow" : "red"
                        }
                      >
                        {formatPercent(row.taxa)}
                      </Badge>
                    </Td>
                    <Td>
                      <div className="h-2 w-48 rounded-full bg-white/5 overflow-hidden">
                        <div
                          className="h-full bg-gradient-to-r from-brand-500 to-emerald-500"
                          style={{ width: `${Math.min(100, row.taxa)}%` }}
                        />
                      </div>
                    </Td>
                  </Tr>
                ))}
              </TBody>
            </Table>
          ) : (
            <EmptyState title="Sem leads para analisar" />
          )}
        </CardBody>
      </Card>
    </>
  );
}
