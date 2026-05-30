# Remoção da Cloudia — guia do que falta

A **Parte 1** (Unidades multi-tenant + webhook da Kommo por unidade + automação de
etapas) está **pronta e compilando**. A Cloudia foi deixada **inerte ao lado** — nada do
caminho novo depende dela, então o build segue verde com ela presente.

Este documento lista exatamente o que remover para apagar o **código** da Cloudia, na
ordem segura (de fora pra dentro), mantendo o build verde a cada passo.

## 1. Endpoints / controllers
- `Controllers/MetaWebhookController.cs` → apagar o método `ReceiveCloudiaWebhook`
  (`[HttpPost("cloudia")]`) e os campos `_cloudiaAdapter` / `cloudiaAdapter` do construtor,
  e o `using LeadAnalytics.Api.DTOs.Cloudia`.
- `Controllers/LeadController.cs` → apagar a action `Cloudia` (`[HttpPost("cloudia")]`, ~l.124-153)
  e a action `origem-cloudia` (`[HttpGet("origem-cloudia")]`, ~l.433-439). Remover
  `using LeadAnalytics.Api.DTOs.Cloudia`. `ActiveLeadDto`/`GetActiveLeadsAsync` (`/webhooks/active`)
  NÃO são da Cloudia — manter (ver passo 4).
- `Controllers/ConfigurationController.cs` → apagar os 3 endpoints `cloudia-api-key`
  (POST/GET status/DELETE) **se** não usar mais a API key da Cloudia.
- `Controllers/SdrLeadsController.cs` → o `sync-from-cloudia` lê da tabela `leads` por
  unidade; ainda funciona após a remoção (não usa tipos Cloudia). Renomear é opcional.

## 2. Pipeline de webhook (era exclusivo da Cloudia)
- `Service/WebhookEnqueueService.cs` — apagar
- `Jobs/ProcessarWebhooksJob.cs` — apagar
- `Service/Stages/StageWebhookDispatcher.cs` — apagar (substituído por `KommoStageProcessor`)
- `Service/Stages/CloudiaStages.cs` — apagar (substituído por `CanonicalStages`)
- `Jobs/ReconciliacaoCloudiaJob.cs` — apagar (era stub)
- `Adapters/CloudiaAdapter.cs` — apagar
- Em `Program.cs`, remover os registros: `ReconciliacaoCloudiaJob`, `ProcessarWebhooksJob`,
  `WebhookEnqueueService`, `StageWebhookDispatcher`, `CloudiaAdapter`.

## 3. LeadService (cirurgia — fazer com build após cada corte)
Remover estes métodos (todos recebem/retornam tipos Cloudia):
- `SaveLeadAsync(CloudiaWebhookDto)` (~l.24)
- `CreateLeadAsync(CloudiaLeadDataDto)` (~l.341)
- `UpdateLeadAsync(CloudiaLeadDataDto)` (~l.567)
- `UpdateUserTagAsync(CloudiaWebhookDto)` (~l.769)
- `GetProcessAssignment(CloudiaWebhookDto)` (~l.1692)
- `ResolverTracking(CloudiaLeadDataDto)` (~l.1646) e `ResolverChannel(CloudiaLeadDataDto)` (~l.1677)
- `GetCheckSourceCloudia(int)` (~l.1488) — só agrupa por Source; renomear p/ `GetSourceBreakdown`
  ou apagar junto com a action `origem-cloudia`.
Remover o `using LeadAnalytics.Api.DTOs.Cloudia;` do topo.
**Manter** `GetActiveLeadsAsync` (usa `ActiveLeadDto`, namespace Response).

## 4. DTOs
- `DTOs/Cloudia/` — apagar a pasta INTEIRA, **exceto** mover `ActiveLeadDto.cs` (contém
  `ActiveLeadDto` e `LeadsCountDto`, ambos `namespace ...DTOs.Response`) para
  `DTOs/Response/ActiveLeadDto.cs`. `DashboardOverviewDto.cs` usa `LeadsCountDto` — só
  trocar o `using ...DTOs.Cloudia` por nada (já está no namespace Response).

## 5. Banco (tabela e config)
- `Models/WebhookEnvelope.cs` — apagar; remover `DbSet<WebhookEnvelope>` e o bloco
  `modelBuilder.Entity<WebhookEnvelope>` em `Data/AppDbContext.cs`.
- Gerar migration `DropWebhookEnvelopes` (`dotnet ef migrations add DropWebhookEnvelopes`).
- `appsettings.Development.json` — remover a seção `"Cloudia"`.

## 6. Dados (rodar você, com a connection string de produção)
Script pronto: `scripts/cleanup_cloudia_data.sql` (começa em ROLLBACK/dry-run).
Esta máquina **não tem** a connection string do banco (ela é injetada em runtime via env
`DefaultConnection`, ex.: Railway), então a limpeza precisa ser rodada por você:

```bash
# 1) backup primeiro!
pg_dump "<DATABASE_URL_DE_PRODUCAO>" > backup_antes_cloudia.sql
# 2) dry-run (confere as contagens; o script termina em ROLLBACK)
psql "<DATABASE_URL_DE_PRODUCAO>" -f scripts/cleanup_cloudia_data.sql
# 3) quando tiver certeza, troque ROLLBACK por COMMIT no fim do .sql e rode de novo
```
