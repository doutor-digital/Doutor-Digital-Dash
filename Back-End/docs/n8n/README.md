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

## Meta Ads: gasto real (n8n puxa do Graph, API grava)

`meta-ads-spend.json` — o **n8n autentica no Meta** (1 token de System User do
Business Manager, que enxerga todas as contas) e puxa o gasto direto do Graph;
a API só **recebe e grava**. Assim não é preciso implementar o cliente do Graph
em C# (o `MetaAdsProvider` continua stub e sai de cena nesse caminho).

Fluxo: `cron 6h → GET /me/adaccounts → 1 conta por vez → GET act_<id>/insights
(level=campaign, time_increment=1, last_30d) → Code monta o payload →
POST /internal/ads/spend`.

O endpoint `POST /internal/ads/spend` faz upsert em `CampaignDailySpend`
(a mesma tabela que o dashboard lê), por conta+campanha+dia. Payload:

```json
{ "provider": "meta", "externalAccountId": "123456789",
  "rows": [ { "campaignId": "23851", "campaignName": "…",
              "date": "2026-07-20", "spend": 123.45, "currency": "BRL" } ] }
```

### ⚠️ Passo obrigatório: mapear a conta → unidade
A API resolve a conta por **`Provider` + `ExternalAccountId`**. Para cada conta
do Meta, precisa existir uma linha em `AdAccounts` com:
- `Provider = "meta"`
- `ExternalAccountId` = o **`account_id` numérico** do Meta (sem o prefixo `act_`)
- `UnitId` / `ClinicId` = a unidade a que o gasto pertence

Sem esse mapeamento a API responde `{"matched": false}` (HTTP 200, não quebra o
loop) e **nada é gravado** — é assim que você descobre quais contas faltam mapear.

### O que preencher no workflow
Os 2 nós do Meta usam o **nó nativo `Facebook Graph API`**, então o token NÃO fica
no JSON — vai numa **credencial** do n8n:

1. n8n → **Credentials → New → "Facebook Graph API"** → cola o token de System
   User (`ads_read`) → salva (ex.: nome "Meta System User").
2. Abra os nós **"Meta: lista contas do BM"** e **"Meta: gasto por campanha/dia"**
   e **selecione essa credencial** em cada um (credencial nunca vem no import).
3. No nó **"POST gasto p/ API"**, troque `COLE_SUA_ADMIN_API_KEY` pela
   `ADMIN_API_KEY` da VPS.

Config dos nós do Meta (já vem preenchida, confira após importar):

| | Node | Edge | Query |
|---|---|---|---|
| Listar contas | `me` | `adaccounts` | `fields=account_id,name,currency`, `limit=200` |
| Gasto | `act_{{ $json.account_id }}` | `insights` | `level=campaign`, `fields=campaign_id,campaign_name,spend`, `time_increment=1`, `date_preset=last_30d` |

Versão do Graph fixada em `v21.0` — suba quando quiser.

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

## Doutor Hérnia (API Spine) — histórico da agenda

`spine-agenda-historico.json` — cron diário 03:17, sem passar pela API .NET.

O sistema clínico do Doutor Hérnia **não guarda histórico e só aceita consultas
de até 100 dias**. Passado esse prazo, a agenda de um mês antigo some — não dá
para perguntar depois. Este workflow existe para preservar esse dado.

**Como funciona:** janela móvel de 7 dias, uma requisição por categoria
(`POST /api/schedules/search` com `idCategory` 1/2/3), normalização e
`append or update` numa planilha, casando por `idSchedule`.

Dois detalhes que parecem arbitrários e não são:

- **Por que 7 dias e não 1:** o status muda depois. Um horário `AGENDADO` hoje
  vira `ATENDIDO` ou `DESMARCADO` amanhã. Reprocessar a semana corrige as linhas
  já gravadas — daí o `append or update` em vez de `append`.
- **Por que uma chamada por categoria:** a resposta de `/schedules/search` **não
  traz** o campo de categoria. A única forma de distinguir avaliação de sessão é
  pedir filtrado por `idCategory` e carimbar a resposta.

O nó de normalização converte `dateAttendance` de UTC para o horário local
(UTC−3). Não é cosmético: há consultas às 00:15 UTC que são 21h15 do **dia
anterior** em Imperatriz.

**Antes de ativar:** preencher `documentId` com o ID da planilha do Google e
conferir a credencial *Header Auth* (`Authorization: Bearer <token do Spine>`).
O workflow falha alto se algum dia a janela passar de 100 linhas por categoria —
melhor quebrar do que truncar em silêncio.

## Doutor Hérnia — horários livres (tool da Sofia)

`spine-horarios-livres.json` — webhook que o agente-dt (LangGraph) chama como
tool para descobrir horários vagos antes de oferecer agendamento.

A API do Doutor Hérnia só devolve horário **ocupado**; não existe rota de
disponibilidade (testadas 6, todas 404). Horário livre é sempre calculado:

```
livres = expediente da unidade
       − (AGENDADO + CONFIRMADO + ATENDIDO)   ← só estes ocupam
       − bloqueios fixos (almoço, folga)
```

**A pegadinha que este workflow resolve:** DESMARCADO (57) e REMARCADO (41) NÃO
ocupam — devolvem o horário. Tratá-los como ocupados esconderia vaga da Sofia.
Validado contra a agenda real: um slot desmarcado volta a aparecer como livre.

**Regra da unidade fica no node "Calcula horários livres"**, editável sem
redeploy: `EXPEDIENTE` (por dia da semana), `BLOQUEIOS` (almoço etc.) e
`PASSO_MIN`. Os valores atuais (07h–19h, passo 30 min) foram medidos na agenda
da Imperatriz; **almoço e sábado são suposição — confirmar com a clínica**.

Contrato da tool (o que a Sofia envia e recebe):

```
POST /webhook/spine-horarios-livres
  { "data": "2026-07-28", "duracaoMin": 30, "idCategoria": 1 }
→ { "data": "...", "fechado": false,
    "livres": ["07:00","07:30", ...],
    "ocupados": ["14:00"],
    "resumo": "21 horários livres em 2026-07-28: 07:00, 07:30, ..." }
```

O campo `resumo` já vem em linguagem natural para a Sofia repassar direto.
`idCategoria` default 1 (avaliação); `duracaoMin` default 30. Antes de ativar,
apontar a credencial Header Auth do token do Spine.
