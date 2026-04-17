import { useQuery } from "@tanstack/react-query";
import { PageHeader } from "@/components/layout/PageHeader";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { Table, TBody, THead, Td, Th, Tr } from "@/components/ui/Table";
import { EmptyState } from "@/components/ui/EmptyState";
import { assignmentsService } from "@/services/assignments";
import { useClinic } from "@/hooks/useClinic";
import { formatNumber } from "@/lib/utils";

export default function AttendantsPage() {
  const { clinicId } = useClinic();

  const atts = useQuery({
    queryKey: ["attendants"],
    queryFn: () => assignmentsService.listAttendants(),
  });
  const rank = useQuery({
    queryKey: ["attendants-ranking", clinicId],
    queryFn: () => assignmentsService.ranking(clinicId || undefined),
  });

  return (
    <>
      <PageHeader
        title="Atendentes"
        description="Equipe cadastrada e ranking por atribuições"
      />

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <Card>
          <CardHeader title="Equipe" subtitle="Cadastrados no sistema" />
          <CardBody className="p-0">
            {atts.isLoading ? (
              <div className="p-4 space-y-2">
                {Array.from({ length: 6 }).map((_, i) => (
                  <div key={i} className="skeleton h-8 w-full rounded" />
                ))}
              </div>
            ) : atts.data && atts.data.length > 0 ? (
              <Table>
                <THead>
                  <Tr>
                    <Th>Nome</Th>
                    <Th>Email</Th>
                    <Th>Atribuições</Th>
                  </Tr>
                </THead>
                <TBody>
                  {atts.data.map((a) => (
                    <Tr key={a.id}>
                      <Td className="font-medium">{a.name}</Td>
                      <Td className="text-slate-300">{a.email ?? "—"}</Td>
                      <Td>{formatNumber(a.totalAssignments ?? 0)}</Td>
                    </Tr>
                  ))}
                </TBody>
              </Table>
            ) : (
              <EmptyState title="Nenhum atendente cadastrado" />
            )}
          </CardBody>
        </Card>

        <Card>
          <CardHeader
            title="Ranking"
            subtitle="Top atendentes por número de atribuições"
          />
          <CardBody className="p-0">
            {rank.isLoading ? (
              <div className="p-4 space-y-2">
                {Array.from({ length: 6 }).map((_, i) => (
                  <div key={i} className="skeleton h-8 w-full rounded" />
                ))}
              </div>
            ) : rank.data && rank.data.length > 0 ? (
              <div className="p-4 space-y-3">
                {rank.data.slice(0, 10).map((r, i) => {
                  const max = Math.max(...rank.data!.map((x) => x.total));
                  const pct = (r.total / max) * 100;
                  return (
                    <div key={r.attendantId}>
                      <div className="flex items-center justify-between text-sm mb-1">
                        <span className="text-slate-200">
                          <span className="text-slate-500 mr-2">#{i + 1}</span>
                          {r.name}
                        </span>
                        <span className="font-semibold text-slate-100">
                          {formatNumber(r.total)}
                        </span>
                      </div>
                      <div className="h-2 rounded-full bg-white/5 overflow-hidden">
                        <div
                          className="h-full bg-gradient-to-r from-brand-500 to-violet-500"
                          style={{ width: `${pct}%` }}
                        />
                      </div>
                    </div>
                  );
                })}
              </div>
            ) : (
              <EmptyState title="Sem ranking para exibir" />
            )}
          </CardBody>
        </Card>
      </div>
    </>
  );
}
