# API Compatibility Audit — LeadAnalytics Front-end

## Scope
Audit focused on:
- `src/services/*`
- `src/hooks/*`
- `src/lib/api.ts`, `src/lib/http.ts`
- data types in `src/types/index.ts`
- pages that consume those services

## Compatibility matrix (front vs OpenAPI)

| Arquivo | Função | Endpoint anterior | Endpoint contrato | Status | Problema | Correção |
|---|---|---|---|---|---|---|
| `src/services/webhooks.ts` | `consultaPeriodos` | `/webhooks/consulta-periodos` com `clinicId, ano, mes, dia` | `/webhooks/consulta-periodos` com `ClinicId, Ano, Mes, Semana, Dia` | **INCORRETO** | nomes de query params divergentes do contrato | atualizado para nomes exatos do Swagger |
| `src/services/units.ts` | `updateName` | `PUT /units/{clinicId}` com body objeto `{ name }` | `PUT /units/{clinicId}` com body `string` | **INCORRETO** | payload incompatível com contrato | body alterado para string |
| `src/services/webhooks.ts` | `activeLeads` | sem normalização de `unitId` | `/webhooks/active?unitId&limit` | **INCOMPLETO** | `unitId` podia chegar inválido | `toInt` + `cleanParams` aplicados |
| `src/services/analytics.ts` | `leadMetrics`, `unitSummary`, etc. | ids string sem validação | path params `int32` | **INCOMPLETO** | sem validação numérica para path params | validação `toInt` e erro técnico explícito |
| `src/services/reports.ts` | `daily` | `tenantId: string` sem validação | `/daily-relatory/generate?tenantId=int32` | **INCOMPLETO** | possível envio inválido | validação/parse numérico |
| `src/lib/api.ts` | interceptor erro | `AxiosError<any>` | `ProblemDetails` | **INCORRETO** | tipagem fraca | troca para `AxiosError<ProblemDetails>` |
| `src/pages/DashboardPage.tsx` | ativos | `/webhooks/active` sem `unitId` | `/webhooks/active?unitId` | **INCOMPLETO** | dados não escopados por unidade | agora passa `unitId` da unidade ativa |

## Rotas marcadas como OBSOLETA

1. **OBSOLETA (no consumo atual):** `/webhooks/consulta-periodos` com query params minúsculos (`clinicId`, `ano`, `mes`, `dia`).
   - **Correto:** `ClinicId`, `Ano`, `Mes`, `Semana`, `Dia`.

2. **OBSOLETA (no consumo atual):** `PUT /units/{clinicId}` enviando `{ name }`.
   - **Correto:** body string conforme OpenAPI.

## Derivações analíticas front-end (sem alterar layout)

Implementadas em `useOperationalKpis` (`src/hooks/useLeadAnalytics.ts`):

1. **Taxa de backlog**
   - Fórmula: `queue / total`
   - Endpoints: `/webhooks/count-by-state`
   - Limitação: depende da atualização do agregador backend.

2. **Taxa de leads sem responsável**
   - Fórmula: `ativos sem attendantId / ativos totais`
   - Endpoints: `/webhooks/active`
   - Limitação: restrito aos leads ativos retornados.

3. **Faixas de envelhecimento da fila**
   - Fórmula: diferença em minutos entre `now` e `updatedAt|createdAt` para estado `queue`.
   - Faixas: `recente <= 10`, `atenção 10-30`, `crítico > 30`.
   - Endpoints: `/webhooks/active`
   - Limitação: depende da consistência temporal do backend.

4. **Score operacional (0-100)**
   - Fórmula heurística documentada no hook.
   - Endpoints: `/webhooks/count-by-state` + `/webhooks/active`
   - Limitação: score operacional heurístico, não financeiro.

## Limitações do backend para nível enterprise (gaps de contrato)

- Falta schema explícito para múltiplas respostas 200 (diversos endpoints retornam `OK` sem payload definido).
- Falta endpoint de metadados de unidade com identidade estável para branding (logo, slug, tenant mapping).
- Falta endpoint de série temporal já agregada com granularidade configurável e baseline para sazonalidade.
- Falta endpoint consolidado de KPIs executivos por unidade com semântica oficial de negócio.

## Sugestões de próximos endpoints (evolução)

1. `GET /api/analytics/units/{unitId}/kpis/executive`
2. `GET /api/analytics/units/{unitId}/queue-aging`
3. `GET /api/analytics/units/{unitId}/origins/coverage`
4. `GET /api/analytics/units/{unitId}/team/load-balance`

