# Jobs migrados para o n8n (alertas + syncs)

Os `BackgroundService` de alerta saíram da API. O .NET agora só **detecta**
e **muda estado**; o **quando** (cron) e o **avisar alguém** (notificação) vivem
no n8n. Isso deixa o núcleo igual para todo cliente Kommo — configura o canal de
notificação por cliente no n8n, sem recompilar a API.

## Endpoints (protegidos por `X-Admin-Key`)

| Antigo job | Endpoint | Método | Cadência sugerida |
|---|---|---|---|
| `AlertaPagamentoAtrasadoJob` | `/internal/alerts/overdue-installments/run` | POST | 1x/dia |
| `AlertaPreenchimentoPendenteJob` | `/internal/alerts/pending-fills?hours=24` | GET | de hora em hora |

- **overdue-installments/run**: marca parcelas `pendente` vencidas como `atrasado`
  (transição de estado, fica no .NET) e devolve `{ count, items[] }` do que mudou.
- **pending-fills**: read-only, devolve `{ count, items[] }` de tratamentos
  `aguardando_dados` há mais de `hours` (default 24, máx 720).

Cada item traz `tenantId` / `unitId` / `leadName` / `leadPhone` para o n8n rotear
a notificação por cliente.

## Syncs agendados migrados

Os 3 `BackgroundService` de sync saíram da API. A **lógica de ingestão continua
no .NET** (`KommoSyncService` / `AdsSpendSyncService`); o que virou n8n foi só o
**agendamento e o ritmo**: o cron dispara, o n8n lista os itens e itera **1 por
vez com um `Wait`** entre eles (o antigo `Task.Delay` de 2s do rate-limit da Kommo).

| Antigo job | Endpoint | Método | Cadência |
|---|---|---|---|
| `KommoSyncPeriodicJob` | `/internal/sync/kommo/units` → `…/units/{id}?maxLeads=500` | GET → POST | 30min |
| `KommoNightlySyncJob` | `/internal/sync/kommo/units` → `…/units/{id}?maxLeads=5000` | GET → POST | 03h BRT |
| `AdsSpendSyncJob` | `/internal/sync/ads/accounts` → `…/accounts/{id}` | GET → POST | 6h |

Cada `POST` sincroniza **um** item e responde rápido — nada de request gigante.
Workflows: `kommo-sync-incremental.json`, `kommo-sync-noturno.json`, `ads-spend-sync.json`.
O padrão do loop é `SplitOut → Loop Over Items (batch 1) → POST → Wait → volta pro Loop`.

## URL: rede interna do Swarm (já configurada nos JSONs)

Na VPS o n8n e a API estão na **mesma overlay `portainer-next`**, então os HTTP
Request já apontam para `http://ddapi_api:8080` — o alias do serviço `ddapi_api`,
porta 8080. Verificado: `wget http://ddapi_api:8080/health` de dentro do container
do n8n devolve `{"status":"ok"}`. Não passa pelo Traefik nem pela internet — mais
rápido e não expõe os `/internal/*` publicamente.

> Se um dia rodar o n8n fora dessa rede, troque a base por `https://<API_DOMAIN>`
> (o subdomínio público via Traefik).

## Fuso horário

O n8n desta VPS roda em **`America/Sao_Paulo`** (`GENERIC_TIMEZONE`). Os crons dos
JSONs já estão em horário de Brasília — em especial o noturno em `17 3 * * *`
(03h17 BRT). Se mudar o TZ do n8n, revise os crons.

## Como importar

1. **Deployar a API primeiro** — os `/internal/*` só existem depois do deploy
   (o commit em produção precisa ser o que adiciona esses endpoints). Antes disso
   o n8n recebe **404**.
2. n8n → **Workflows → Import from File** → importe os `.json` desta pasta.
3. `X-Admin-Key`: troque `COLE_SUA_ADMIN_API_KEY` pelo valor de `ADMIN_API_KEY` da
   VPS. Ideal: criar uma credencial **Header Auth** (`X-Admin-Key`) e reusar nos nós.
4. Nos de alerta, ligue o nó `→ Notificar` ao canal (Resend/e-mail, WhatsApp, Slack…).
5. Teste com **Execute Workflow** e só então **ative**.
