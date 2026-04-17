import { useState } from "react";
import { FileDown, FileText } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/layout/PageHeader";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Button } from "@/components/ui/Button";
import { reportsService } from "@/services/reports";
import { useClinic } from "@/hooks/useClinic";

export default function ReportsPage() {
  const { clinicId } = useClinic();
  const today = new Date();

  const [monthlyParams, setMonthlyParams] = useState({
    clinicId: clinicId || "",
    mes: today.getMonth() + 1,
    ano: today.getFullYear(),
  });
  const [dailyParams, setDailyParams] = useState({
    tenantId: clinicId || "",
    date: today.toISOString().slice(0, 10),
  });
  const [monthlyLoading, setMonthlyLoading] = useState(false);
  const [dailyLoading, setDailyLoading] = useState(false);
  const [dailyData, setDailyData] = useState<any>(null);

  async function gerarMensal() {
    if (!monthlyParams.clinicId) return toast.error("Informe o Clinic ID");
    setMonthlyLoading(true);
    try {
      await reportsService.monthly(monthlyParams);
      toast.success("PDF gerado");
    } catch {
      // erro tratado pelo interceptor
    } finally {
      setMonthlyLoading(false);
    }
  }

  async function gerarDiario() {
    if (!dailyParams.tenantId) return toast.error("Informe o Tenant ID");
    setDailyLoading(true);
    try {
      const data = await reportsService.daily(dailyParams);
      setDailyData(data);
      toast.success("Relatório carregado");
    } catch {
      // tratado
    } finally {
      setDailyLoading(false);
    }
  }

  return (
    <>
      <PageHeader
        title="Relatórios"
        description="Gere PDFs mensais e visualize o relatório diário por unidade"
      />

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <Card>
          <CardHeader
            title="Relatório mensal (PDF)"
            subtitle="Taxa de conversão, origens, etapas, unidades"
          />
          <CardBody className="space-y-3">
            <div>
              <label className="label">Clinic ID</label>
              <Input
                className="mt-1"
                value={monthlyParams.clinicId}
                onChange={(e) =>
                  setMonthlyParams((p) => ({ ...p, clinicId: e.target.value }))
                }
              />
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="label">Mês</label>
                <Select
                  className="mt-1"
                  value={monthlyParams.mes}
                  onChange={(e) =>
                    setMonthlyParams((p) => ({ ...p, mes: +e.target.value }))
                  }
                >
                  {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
                    <option key={m} value={m}>
                      {new Date(2000, m - 1, 1).toLocaleString("pt-BR", {
                        month: "long",
                      })}
                    </option>
                  ))}
                </Select>
              </div>
              <div>
                <label className="label">Ano</label>
                <Input
                  type="number"
                  className="mt-1"
                  value={monthlyParams.ano}
                  onChange={(e) =>
                    setMonthlyParams((p) => ({ ...p, ano: +e.target.value }))
                  }
                />
              </div>
            </div>
            <Button onClick={gerarMensal} loading={monthlyLoading} className="w-full justify-center">
              <FileDown className="h-4 w-4" /> Baixar PDF
            </Button>
          </CardBody>
        </Card>

        <Card>
          <CardHeader
            title="Relatório diário"
            subtitle="Agendamentos, resgates, motivos"
          />
          <CardBody className="space-y-3">
            <div>
              <label className="label">Tenant ID</label>
              <Input
                className="mt-1"
                value={dailyParams.tenantId}
                onChange={(e) =>
                  setDailyParams((p) => ({ ...p, tenantId: e.target.value }))
                }
              />
            </div>
            <div>
              <label className="label">Data</label>
              <Input
                type="date"
                className="mt-1"
                value={dailyParams.date}
                onChange={(e) =>
                  setDailyParams((p) => ({ ...p, date: e.target.value }))
                }
              />
            </div>
            <Button onClick={gerarDiario} loading={dailyLoading} className="w-full justify-center">
              <FileText className="h-4 w-4" /> Visualizar relatório
            </Button>

            {dailyData && (
              <div className="mt-3 rounded-lg border border-white/10 bg-white/[0.02] p-3">
                <pre className="text-[11px] text-slate-300 overflow-auto max-h-72">
                  {JSON.stringify(dailyData, null, 2)}
                </pre>
              </div>
            )}
          </CardBody>
        </Card>
      </div>
    </>
  );
}
