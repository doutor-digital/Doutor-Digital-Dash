# Como criar o contrato OpenAPI sugerido no .NET (ASP.NET Core)

Este guia mostra, em texto e de forma prática, como implementar o contrato OpenAPI sugerido para as novas rotas de dashboard/analytics no seu backend em .NET.

## 1) Instalar e habilitar Swagger/OpenAPI

No projeto ASP.NET Core:

1. Garanta o pacote `Swashbuckle.AspNetCore` no `.csproj`.
2. No `Program.cs`, registre:
   - `builder.Services.AddEndpointsApiExplorer();`
   - `builder.Services.AddSwaggerGen();`
3. No pipeline (`app`), habilite:
   - `app.UseSwagger();`
   - `app.UseSwaggerUI();`

> Resultado: o JSON do contrato ficará disponível em `/swagger/v1/swagger.json`.

## 2) Organizar rotas por domínio (contrato limpo)

Crie controllers dedicados para separar responsabilidades:

- `DashboardController` -> `/api/dashboard/*`
- `AnalyticsTimeSeriesController` -> `/api/analytics/timeseries/*`
- `OperationsController` -> `/api/operations/*`
- `MetaController` -> `/api/meta/*`
- `ReportsController` -> `/api/reports/*`

Use:

- `[ApiController]`
- `[Route("api/[controller]")]` ou rota fixa por domínio.

Para manter o contrato previsível para o front, prefira nomes de rota estáveis e sem abreviações.

## 3) Definir DTOs de request/response (fonte real do OpenAPI)

O Swagger gera o schema a partir dos tipos C#. Então você deve modelar DTOs explícitos.

### Exemplo de request base para filtros

Crie um DTO compartilhado para filtros:

- `Guid? UnitId`
- `DateTime? From`
- `DateTime? To`
- `string? Granularity` (`day|week|month`)

No endpoint, receba via query:

- `Task<ActionResult<DashboardOverviewResponse>> GetOverview([FromQuery] DashboardQuery query)`

### Exemplo de response para overview

Monte um objeto composto com blocos do dashboard:

- `Kpis`
- `Funnel`
- `Sources`
- `Queue`
- `Alerts`
- `TopAttendants`

Quanto mais tipado estiver, melhor fica a geração de tipos no frontend com `openapi-typescript`.

## 4) Documentar cada endpoint com atributos

Para enriquecer o contrato OpenAPI, use:

- `[HttpGet("overview")]`
- `[ProducesResponseType(typeof(DashboardOverviewResponse), StatusCodes.Status200OK)]`
- `[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]`
- `[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]`

E XML comments (resumo e descrição) nos métodos/DTOs para aparecer no Swagger UI.

## 5) Validar entrada e padronizar erros

Para o contrato ficar robusto:

1. Valide datas (`From <= To`), enum strings (`Granularity`) e obrigatoriedades.
2. Retorne erros no formato `ProblemDetails`.
3. Evite retornar mensagens internas (`ex.Message`) para cliente.

Assim o front consegue tratar erros de forma previsível e tipada.

## 6) Exemplo de mapeamento dos endpoints sugeridos

Você pode implementar os endpoints sugeridos anteriormente assim:

### Dashboard
- `GET /api/dashboard/overview?unitId=&startDate=&endDate=`
- `GET /api/dashboard/realtime?unitId=`
- `GET /api/dashboard/charts?unitId=&granularity=day|week|month`

### Time series
- `GET /api/analytics/timeseries/leads-created?unitId=&from=&to=&groupBy=day|hour`
- `GET /api/analytics/timeseries/conversion?unitId=&from=&to=&groupBy=day|week`
- `GET /api/analytics/timeseries/response-time?unitId=&from=&to=`
- `GET /api/analytics/timeseries/queue-size?unitId=&from=&to=`

### Operações/SLA
- `GET /api/operations/sla?unitId=&from=&to=`
- `GET /api/operations/bottlenecks?unitId=&from=&to=`
- `GET /api/operations/workload/attendants?unitId=&from=&to=`
- `GET /api/operations/alerts/history?unitId=&from=&to=`

### Metadados
- `GET /api/meta/filters`
- `GET /api/meta/stages`
- `GET /api/meta/health`

### Relatórios
- `POST /api/reports/export`
- `GET /api/reports/jobs/{jobId}`
- `GET /api/reports/jobs/{jobId}/download`
- `POST /api/dashboard/share-link`

## 7) Versionar contrato e proteger compatibilidade

Boas práticas:

- Versione API (`v1`, `v2`) quando quebrar contrato.
- Mantenha campos antigos por período de transição.
- Evite mudar nome/tipo de campo sem estratégia de migração.

## 8) Gerar tipos no frontend automaticamente

Com o backend rodando, no front (Next.js + pnpm):

```bash
pnpm dlx openapi-typescript http://localhost:5000/swagger/v1/swagger.json -o src/api/generated/types.ts
```

Sempre que adicionar endpoint/DTO novo, regenere os tipos para manter frontend e backend sincronizados.

## 9) Checklist mínimo antes de publicar

- Swagger abre em `/swagger`.
- JSON abre em `/swagger/v1/swagger.json`.
- Todos endpoints novos aparecem documentados.
- Responses têm schemas fortes (sem `object` genérico quando possível).
- Erros seguem `ProblemDetails`.
- Front conseguiu gerar tipos sem erro.

---

Se quiser, no próximo passo posso te entregar um esqueleto de código C# (controllers + DTOs) já pronto para copiar e colar no seu projeto.
