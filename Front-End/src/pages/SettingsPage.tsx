import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { KeyRound, ShieldCheck, Trash2, Link as LinkIcon } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/layout/PageHeader";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { Input } from "@/components/ui/Input";
import { Button } from "@/components/ui/Button";
import { Badge } from "@/components/ui/Badge";
import { configService } from "@/services/config";
import {
  setAdminKey,
  setCloudiaBaseUrl,
  setCloudiaBearerToken,
} from "@/lib/api";
import { formatDate } from "@/lib/utils";

export default function SettingsPage() {
  const qc = useQueryClient();
  const [adminKey, setAdmin] = useState(localStorage.getItem("admin_key") ?? "");
  const [apiKey, setApiKey] = useState("");
  const [expiresAt, setExpiresAt] = useState("");
  const [cloudiaBearer, setCloudiaBearer] = useState(
    localStorage.getItem("cloudia_bearer_token") ?? ""
  );
  const [cloudiaUrl, setCloudiaUrl] = useState(
    localStorage.getItem("cloudia_base_url") ?? ""
  );

  const status = useQuery({
    queryKey: ["cloudia-status"],
    queryFn: () => configService.status(),
    retry: false,
  });

  const save = useMutation({
    mutationFn: () =>
      configService.setCloudiaKey({
        apiKey,
        expiresAt: expiresAt ? new Date(expiresAt).toISOString() : undefined,
      }),
    onSuccess: () => {
      toast.success("Chave Cloudia atualizada");
      setApiKey("");
      qc.invalidateQueries({ queryKey: ["cloudia-status"] });
    },
  });

  const remove = useMutation({
    mutationFn: () => configService.remove(),
    onSuccess: () => {
      toast.success("Chave removida");
      qc.invalidateQueries({ queryKey: ["cloudia-status"] });
    },
  });

  const persistAdmin = useMutation({
    mutationFn: (key: string) => configService.setAdminKey(key),
  });

  async function saveAdminKey() {
    setAdminKey(adminKey || null);
    if (!adminKey) {
      toast.success("Admin key removida (local)");
      return;
    }
    try {
      await persistAdmin.mutateAsync(adminKey);
      toast.success("Admin key salva (local + backend)");
    } catch {
      toast.success("Admin key salva localmente (backend indisponível)");
    }
  }

  function saveCloudiaLocalConfig() {
    setCloudiaBearerToken(cloudiaBearer || null);
    setCloudiaBaseUrl(cloudiaUrl || null);
    toast.success("Configuração Cloudia local salva");
  }
  return (
    <>
      <PageHeader
        title="Configurações"
        description="Integrações e credenciais do painel"
      />

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <Card>
          <CardHeader
            title="Admin key"
            subtitle="Header X-Admin-Key para rotas protegidas"
          />
          <CardBody className="space-y-3">
            <label className="label">Chave</label>
            <Input
              type="password"
              icon={<ShieldCheck className="h-4 w-4" />}
              value={adminKey}
              onChange={(e) => setAdmin(e.target.value)}
              placeholder="••••••••"
            />
            <p className="text-xs text-slate-400">
              Armazenada localmente; enviada apenas nos requests para rotas admin.
            </p>
            <Button onClick={saveAdminKey} className="w-full justify-center">
              Salvar admin key
            </Button>
          </CardBody>
        </Card>

        <Card>
          <CardHeader
            title="Cloudia local (Bearer + URL)"
            subtitle="Salvo no navegador; útil para testar e evitar 502 de URL incorreta"
          />
          <CardBody className="space-y-3">
            <label className="label">Bearer token da Cloudia</label>
            <Input
              type="password"
              icon={<KeyRound className="h-4 w-4" />}
              value={cloudiaBearer}
              onChange={(e) => setCloudiaBearer(e.target.value)}
              placeholder="Bearer ..."
            />

            <label className="label">URL base da Cloudia</label>
            <Input
              type="url"
              icon={<LinkIcon className="h-4 w-4" />}
              value={cloudiaUrl}
              onChange={(e) => setCloudiaUrl(e.target.value)}
              placeholder="https://api.cloudia.com.br"
            />

            <p className="text-xs text-slate-400">
              O app envia isso automaticamente nos headers <code>X-Cloudia-Bearer</code> e <code>X-Cloudia-Base-Url</code>.
            </p>

            <Button onClick={saveCloudiaLocalConfig} className="w-full justify-center">
              Salvar Cloudia local
            </Button>
          </CardBody>
        </Card>

        <Card className="lg:col-span-2">
          <CardHeader
            title="API Cloudia"
            subtitle="Token salvo no backend (AppConfiguration)"
            action={
              <Badge tone={status.data?.configured ? "green" : "slate"}>
                {status.data?.configured ? "Configurada" : "Não definida"}
              </Badge>
            }
          />
          <CardBody className="space-y-3">
            {status.data?.expiresAt && (
              <div className="rounded-lg border border-white/10 bg-white/[0.03] p-3 text-xs text-slate-300">
                Expira em <strong>{formatDate(status.data.expiresAt)}</strong>
              </div>
            )}

            <label className="label">Nova chave</label>
            <Input
              type="password"
              icon={<KeyRound className="h-4 w-4" />}
              placeholder="Cloudia API key"
              value={apiKey}
              onChange={(e) => setApiKey(e.target.value)}
            />

            <label className="label">Expira em (opcional)</label>
            <Input
              type="datetime-local"
              value={expiresAt}
              onChange={(e) => setExpiresAt(e.target.value)}
            />

            <div className="flex gap-2">
              <Button
                onClick={() => save.mutate()}
                loading={save.isPending}
                disabled={!apiKey}
                className="flex-1 justify-center"
              >
                Atualizar chave
              </Button>
              {status.data?.configured && (
                <Button
                  variant="danger"
                  onClick={() => remove.mutate()}
                  loading={remove.isPending}
                >
                  <Trash2 className="h-4 w-4" />
                </Button>
              )}
            </div>
          </CardBody>
        </Card>
      </div>
    </>
  );
}
