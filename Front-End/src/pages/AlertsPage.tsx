import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { AlertTriangle, Bell } from "lucide-react";
import { Link } from "react-router-dom";
import { PageHeader } from "@/components/layout/PageHeader";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { Input } from "@/components/ui/Input";
import { Button } from "@/components/ui/Button";
import { Badge, StateBadge } from "@/components/ui/Badge";
import { EmptyState } from "@/components/ui/EmptyState";
import { analyticsService } from "@/services/analytics";

export default function AlertsPage() {
  const [unitId, setUnitId] = useState(
    localStorage.getItem("lf.alerts.unitId") ?? ""
  );

  const alerts = useQuery({
    queryKey: ["alerts", unitId],
    queryFn: () => analyticsService.unitAlerts(unitId),
    enabled: !!unitId,
    refetchInterval: 30_000,
  });

  return (
    <>
      <PageHeader
        title="Alertas"
        description="Leads fora do SLA esperado"
        actions={
          <div className="flex items-center gap-2">
            <Input
              className="w-64"
              placeholder="Unit ID"
              value={unitId}
              onChange={(e) => {
                setUnitId(e.target.value);
                localStorage.setItem("lf.alerts.unitId", e.target.value);
              }}
            />
            <Button variant="outline" size="sm" onClick={() => alerts.refetch()}>
              Atualizar
            </Button>
          </div>
        }
      />

      {!unitId ? (
        <Card className="p-8">
          <EmptyState
            icon={<Bell className="h-5 w-5 text-amber-400" />}
            title="Informe um Unit ID"
            description="Os alertas são escaneados por unidade."
          />
        </Card>
      ) : alerts.isLoading ? (
        <div className="space-y-3">
          {Array.from({ length: 4 }).map((_, i) => (
            <div key={i} className="skeleton h-24 w-full rounded-xl" />
          ))}
        </div>
      ) : alerts.data && alerts.data.length > 0 ? (
        <div className="space-y-3">
          {alerts.data.map((a) => (
            <Card
              key={a.leadId}
              className="p-4 border-amber-500/30 bg-amber-500/[0.04]"
            >
              <div className="flex items-start gap-3">
                <div className="h-10 w-10 rounded-lg bg-amber-500/15 text-amber-300 grid place-items-center shrink-0">
                  <AlertTriangle className="h-5 w-5" />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2 flex-wrap">
                    <Link
                      to={`/leads/${a.leadId}`}
                      className="font-medium text-slate-100 hover:text-brand-300"
                    >
                      {a.name ?? a.leadId}
                    </Link>
                    <StateBadge state={a.currentState} />
                  </div>
                  <ul className="mt-2 flex flex-wrap gap-2">
                    {(a.alerts ?? []).map((msg, i) => (
                      <Badge key={i} tone="red">
                        <AlertTriangle className="h-3 w-3" /> {msg}
                      </Badge>
                    ))}
                  </ul>
                </div>
                <Link to={`/leads/${a.leadId}`}>
                  <Button variant="outline" size="sm">Abrir</Button>
                </Link>
              </div>
            </Card>
          ))}
        </div>
      ) : (
        <Card className="p-8">
          <EmptyState title="Nenhum alerta ativo" description="Tudo sob controle! 🎯" />
        </Card>
      )}
    </>
  );
}
