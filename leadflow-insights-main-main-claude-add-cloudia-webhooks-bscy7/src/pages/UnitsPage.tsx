import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Building2, Check, Edit2, Plus } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/layout/PageHeader";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { Input } from "@/components/ui/Input";
import { Button } from "@/components/ui/Button";
import { Table, TBody, THead, Td, Th, Tr } from "@/components/ui/Table";
import { EmptyState } from "@/components/ui/EmptyState";
import { unitsService } from "@/services/units";
import { formatNumber } from "@/lib/utils";

export default function UnitsPage() {
  const qc = useQueryClient();
  const units = useQuery({
    queryKey: ["units"],
    queryFn: () => unitsService.list(),
  });

  const [newClinicId, setNewClinicId] = useState("");
  const [editing, setEditing] = useState<number | string | null>(null);
  const [editName, setEditName] = useState("");

  const createMut = useMutation({
    mutationFn: (clinicId: string) => unitsService.getOrCreate(clinicId),
    onSuccess: () => {
      toast.success("Unidade criada / carregada");
      setNewClinicId("");
      qc.invalidateQueries({ queryKey: ["units"] });
    },
  });

  const updateMut = useMutation({
    mutationFn: (p: { clinicId: number | string; name: string }) =>
      unitsService.updateName(p.clinicId, p.name),
    onSuccess: () => {
      toast.success("Nome atualizado");
      setEditing(null);
      qc.invalidateQueries({ queryKey: ["units"] });
    },
  });

  return (
    <>
      <PageHeader
        title="Unidades"
        description="Gerencie as clínicas e unidades conectadas"
        actions={
          <div className="flex items-center gap-2">
            <Input
              className="w-56"
              placeholder="Novo Clinic ID"
              value={newClinicId}
              onChange={(e) => setNewClinicId(e.target.value)}
            />
            <Button
              size="sm"
              onClick={() => newClinicId && createMut.mutate(newClinicId)}
              loading={createMut.isPending}
            >
              <Plus className="h-4 w-4" /> Adicionar
            </Button>
          </div>
        }
      />

      <Card className="p-0">
        {units.isLoading ? (
          <div className="p-4 space-y-2">
            {Array.from({ length: 5 }).map((_, i) => (
              <div key={i} className="skeleton h-10 w-full rounded" />
            ))}
          </div>
        ) : units.data && units.data.length > 0 ? (
          <Table>
            <THead>
              <Tr>
                <Th>Unidade</Th>
                <Th>Clinic ID</Th>
                <Th>Leads</Th>
                <Th>Ações</Th>
              </Tr>
            </THead>
            <TBody>
              {units.data.map((u) => (
                <Tr key={u.id}>
                  <Td>
                    <div className="flex items-center gap-3">
                      <div className="h-8 w-8 rounded-lg bg-white/5 grid place-items-center">
                        <Building2 className="h-4 w-4 text-slate-400" />
                      </div>
                      {editing === u.clinicId ? (
                        <div className="flex items-center gap-2">
                          <Input
                            className="h-8 w-56"
                            value={editName}
                            onChange={(e) => setEditName(e.target.value)}
                            autoFocus
                          />
                          <Button
                            size="sm"
                            onClick={() =>
                              updateMut.mutate({
                                clinicId: u.clinicId,
                                name: editName,
                              })
                            }
                            loading={updateMut.isPending}
                          >
                            <Check className="h-3 w-3" />
                          </Button>
                        </div>
                      ) : (
                        <span className="font-medium">{u.name ?? "—"}</span>
                      )}
                    </div>
                  </Td>
                  <Td className="font-mono text-xs text-slate-400">{u.clinicId}</Td>
                  <Td>{formatNumber(u.leadsCount ?? 0)}</Td>
                  <Td>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => {
                        setEditing(u.clinicId);
                        setEditName(u.name ?? "");
                      }}
                    >
                      <Edit2 className="h-3 w-3" /> Renomear
                    </Button>
                  </Td>
                </Tr>
              ))}
            </TBody>
          </Table>
        ) : (
          <EmptyState title="Nenhuma unidade cadastrada" />
        )}
      </Card>
    </>
  );
}
