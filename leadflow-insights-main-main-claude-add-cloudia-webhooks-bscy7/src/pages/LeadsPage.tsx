import { useQuery } from "@tanstack/react-query";
import { useMemo } from "react";
import { Link } from "react-router-dom";
import { Download, Filter, Search, X } from "lucide-react";
import { PageHeader } from "@/components/layout/PageHeader";
import { Card } from "@/components/ui/Card";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Button } from "@/components/ui/Button";
import { Table, TBody, THead, Td, Th, Tr } from "@/components/ui/Table";
import { StateBadge, StageBadge, Badge } from "@/components/ui/Badge";
import { EmptyState } from "@/components/ui/EmptyState";
import { webhooksService } from "@/services/webhooks";
import { useFilters } from "@/hooks/useFilters";
import { useClinic } from "@/hooks/useClinic";
import { formatDate, formatNumber } from "@/lib/utils";

const PAGE_SIZE = 25;

export default function LeadsPage() {
  const { tenantId, unitId } = useClinic();
  const { values, setFilter, reset } = useFilters({
    search: "",
    state: "",
    stage: "",
    source: "",
    page: "1",
  });

  const query = useQuery({
    queryKey: ["leads", tenantId, unitId],
    queryFn: () => webhooksService.listLeads({ clinicId: unitId || tenantId || undefined }),
  });

  const filtered = useMemo(() => {
    const list = query.data ?? [];
    const s = values.search?.toLowerCase().trim();
    return list.filter((l) => {
      if (values.state && l.conversationState !== values.state) return false;
      if (values.stage && l.currentStage !== values.stage) return false;
      if (values.source && (l.source ?? "") !== values.source) return false;
      if (s) {
        const hay = `${l.name ?? ""} ${l.phone ?? ""} ${l.email ?? ""} ${l.id} ${l.externalId ?? ""}`.toLowerCase();
        if (!hay.includes(s)) return false;
      }
      return true;
    });
  }, [query.data, values]);

  const page = Math.max(1, parseInt(values.page || "1", 10));
  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const pageItems = filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE);

  const stageOptions = useMemo(
    () => uniq((query.data ?? []).map((l) => l.currentStage).filter(Boolean) as string[]),
    [query.data]
  );
  const sourceOptions = useMemo(
    () => uniq((query.data ?? []).map((l) => l.source).filter(Boolean) as string[]),
    [query.data]
  );

  function exportCsv() {
    const rows = [
      ["id", "nome", "telefone", "estado", "etapa", "origem", "criado"],
      ...filtered.map((l) => [
        l.id,
        l.name ?? "",
        l.phone ?? "",
        l.conversationState ?? "",
        l.currentStage ?? "",
        l.source ?? "",
        l.createdAt ?? "",
      ]),
    ];
    const csv = rows
      .map((r) => r.map((c) => `"${String(c).replace(/"/g, '""')}"`).join(","))
      .join("\n");
    const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `leads-${Date.now()}.csv`;
    document.body.appendChild(a);
    a.click();
    a.remove();
  }

  return (
    <>
      <PageHeader
        title="Leads"
        description={`${formatNumber(filtered.length)} leads encontrados`}
        actions={
          <>
            <Button variant="outline" size="sm" onClick={exportCsv}>
              <Download className="h-4 w-4" /> Exportar CSV
            </Button>
          </>
        }
      />

      <Card className="p-4 mb-4">
        <div className="grid grid-cols-1 md:grid-cols-4 gap-3">
          <Input
            icon={<Search className="h-4 w-4" />}
            placeholder="Nome, telefone, email ou ID..."
            value={values.search}
            onChange={(e) => setFilter({ search: e.target.value, page: "1" })}
          />
          <Select
            value={values.state}
            onChange={(e) => setFilter({ state: e.target.value, page: "1" })}
          >
            <option value="">Todos os estados</option>
            <option value="bot">Bot</option>
            <option value="queue">Fila</option>
            <option value="service">Atendimento</option>
            <option value="concluido">Concluído</option>
          </Select>
          <Select
            value={values.stage}
            onChange={(e) => setFilter({ stage: e.target.value, page: "1" })}
          >
            <option value="">Todas as etapas</option>
            {stageOptions.map((s) => (
              <option key={s} value={s}>{s.replace(/_/g, " ")}</option>
            ))}
          </Select>
          <Select
            value={values.source}
            onChange={(e) => setFilter({ source: e.target.value, page: "1" })}
          >
            <option value="">Todas as origens</option>
            {sourceOptions.map((s) => (
              <option key={s} value={s}>{s}</option>
            ))}
          </Select>
        </div>
        <div className="flex items-center gap-2 mt-3">
          <Badge tone="slate">
            <Filter className="h-3 w-3" /> Filtros persistentes na URL
          </Badge>
          {(values.search || values.state || values.stage || values.source) && (
            <Button variant="ghost" size="sm" onClick={reset}>
              <X className="h-3 w-3" /> Limpar
            </Button>
          )}
        </div>
      </Card>

      <Card className="p-0">
        {query.isLoading ? (
          <div className="p-4 space-y-2">
            {Array.from({ length: 8 }).map((_, i) => (
              <div key={i} className="skeleton h-10 w-full rounded" />
            ))}
          </div>
        ) : pageItems.length ? (
          <>
            <Table>
              <THead>
                <Tr>
                  <Th>Lead</Th>
                  <Th>Contato</Th>
                  <Th>Estado</Th>
                  <Th>Etapa</Th>
                  <Th>Origem</Th>
                  <Th>Atendente</Th>
                  <Th>Criado</Th>
                </Tr>
              </THead>
              <TBody>
                {pageItems.map((l) => (
                  <Tr key={l.id} clickable onClick={() => (window.location.href = `/leads/${l.id}`)}>
                    <Td>
                      <Link
                        to={`/leads/${l.id}`}
                        className="flex items-center gap-3 hover:text-brand-300"
                      >
                        <div className="h-8 w-8 rounded-full bg-gradient-to-br from-brand-400 to-violet-600 grid place-items-center text-xs font-semibold text-white">
                          {(l.name ?? "?").charAt(0).toUpperCase()}
                        </div>
                        <div>
                          <div className="text-sm font-medium">{l.name ?? "Sem nome"}</div>
                          <div className="text-[11px] text-slate-500 font-mono">
                            {String(l.id).slice(0, 8)}…
                          </div>
                        </div>
                      </Link>
                    </Td>
                    <Td className="text-slate-300">{l.phone ?? "—"}</Td>
                    <Td>
                      <StateBadge state={l.conversationState ?? undefined} />
                    </Td>
                    <Td>
                      <StageBadge stage={l.currentStage ?? undefined} />
                    </Td>
                    <Td className="text-slate-300">{l.source ?? "—"}</Td>
                    <Td className="text-slate-300">{l.attendantName ?? "—"}</Td>
                    <Td className="text-slate-400 text-xs">{formatDate(l.createdAt)}</Td>
                  </Tr>
                ))}
              </TBody>
            </Table>

            <div className="flex items-center justify-between p-3 border-t border-white/10">
              <span className="text-xs text-slate-400">
                Página {page} de {totalPages} · {formatNumber(filtered.length)} leads
              </span>
              <div className="flex items-center gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  disabled={page <= 1}
                  onClick={() => setFilter({ page: String(page - 1) })}
                >
                  Anterior
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  disabled={page >= totalPages}
                  onClick={() => setFilter({ page: String(page + 1) })}
                >
                  Próxima
                </Button>
              </div>
            </div>
          </>
        ) : (
          <EmptyState
            title="Nenhum lead encontrado"
            description="Tente ajustar os filtros ou verifique o Clinic ID na topbar."
          />
        )}
      </Card>
    </>
  );
}

function uniq<T>(arr: T[]) {
  return Array.from(new Set(arr));
}
