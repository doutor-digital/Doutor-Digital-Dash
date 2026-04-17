# Prompt mestre — Frontend **completo** em Next.js + pnpm + vários gráficos

Copie e cole o prompt abaixo em outra IA (ou use comigo na próxima mensagem):

---

Você é um **Staff Frontend Engineer** especialista em **Next.js 15 (App Router)**, **TypeScript**, **performance web** e **data visualization**.

Quero que você construa um frontend **completo e pronto para produção**, consumindo a API listada abaixo, seguindo rigorosamente os requisitos.

## 1) Objetivo do produto

Construir um painel operacional e analítico para gestão de leads com:
- visão executiva (dashboard)
- funil e conversão
- operação em tempo real (fila/atendimento)
- analytics de lead e de unidade
- alertas de atraso
- relatórios PDF
- área admin de configuração

## 2) Stack obrigatória (foco em leveza)

- **Next.js 15+** (App Router)
- **TypeScript strict**
- **pnpm** (gerenciador de pacotes)
- **TanStack Query** (cache e data fetching)
- **Axios** (com interceptors)
- **Zod + React Hook Form**
- **Tailwind CSS + shadcn/ui**
- **Recharts** para gráficos básicos + **ECharts** somente para gráficos avançados
- **next/dynamic** para lazy-load de componentes pesados (gráficos)
- **date-fns** para datas

> Prioridade: performance e bundle pequeno. Só importe o necessário.

## 3) Regras de arquitetura

1. Criar arquitetura por domínio:
   - `src/modules/dashboard`
   - `src/modules/leads`
   - `src/modules/analytics`
   - `src/modules/metrics`
   - `src/modules/reports`
   - `src/modules/admin`
2. Camada de API separada:
   - `src/api/client.ts`
   - `src/api/endpoints/*.ts`
   - `src/api/types/generated.ts`
3. Server Components por padrão; Client Components apenas quando necessário.
4. Separar claramente:
   - `components` (UI)
   - `hooks` (data layer)
   - `adapters` (normalização de payload)
5. Criar `ErrorBoundary`, `Loading`, `EmptyState` reutilizáveis.
6. Criar sistema de filtros globais (unidade, período, estado do lead).

## 4) Performance obrigatória

- Meta de performance:
  - LCP < 2.5s
  - CLS < 0.1
  - JS inicial enxuto
- Implementar:
  - code splitting por rota
  - lazy dos gráficos pesados
  - prefetch inteligente
  - memoização (`useMemo`, `select` do React Query)
  - tabelas com paginação e virtualização quando necessário
- Evitar:
  - libs duplicadas
  - importar locale completo desnecessário
  - uso de `any`

## 5) Endpoints do backend (contrato atual)

## Webhooks/Leads
- `GET /webhooks`
- `POST /webhooks/cloudia`
- `GET /webhooks/consultas?clinicId={id}`
- `GET /webhooks/sem-pagamento?clinicId={id}`
- `GET /webhooks/com-pagamento?clinicId={id}`
- `GET /webhooks/source-final?clinicId={id}`
- `GET /webhooks/origem-cloudia?clinicId={id}`
- `GET /webhooks/fim-de-semana?clinicId={id}`
- `GET /webhooks/etapa-agrupada?clinicId={id}`
- `GET /webhooks/buscar-inicio-fim?clinicId={id}&dataInicio={iso}&dataFim={iso}`
- `GET /webhooks/consulta-periodos?clinicId={id}&ano={ano}&mes={mes?}&dia={dia?}`
- `GET /webhooks/active?limit=100&unitId={id?}`
- `GET /webhooks/count-by-state?unitId={id?}`
- `GET /webhooks/sync/health`

## Metrics
- `GET /metrics/dashboard?clinicId={id}&attendantType=HUMAN`
- `GET /metrics/resumo?clinicId={id}`
- `GET /metrics/fila?clinicId={id}`
- `GET /metrics/completo?clinicId={id}`

## Units
- `GET /units`
- `GET /units/{clinicId}`
- `PUT /units/{clinicId}` (body: string)
- `GET /units/quantity-leads?clinicId={id}`

## Assignments
- `GET /assignments/attendants`
- `GET /assignments/lead/{externalLeadId}?clinicId={id}`
- `GET /assignments/ranking?clinicId={id}`
- `POST /assignments/sync`

## Analytics
- `GET /api/analytics/leads/{id}/metrics`
- `GET /api/analytics/units/{unitId}/leads-metrics?startDate={iso?}&endDate={iso?}&state={state?}`
- `GET /api/analytics/units/{unitId}/summary?startDate={iso?}&endDate={iso?}`
- `GET /api/analytics/units/{unitId}/alerts`
- `GET /api/analytics/units/{unitId}/dashboard/today`

## Relatórios
- `GET /api/relatorios/mensal?clinicId={id}&mes={1-12}&ano={yyyy}` (PDF)

## Configuração admin
- `POST /api/config/cloudia-api-key` (header `X-Admin-Key`)
- `GET /api/config/cloudia-api-key/status` (header `X-Admin-Key`)
- `DELETE /api/config/cloudia-api-key` (header `X-Admin-Key`)

## Webhook Meta
- `GET /api/webhooks/meta`
- `POST /api/webhooks/meta`
- `POST /api/webhooks/meta/n8n`
- `POST /api/webhooks/cloudia`

## 6) Mapa de telas completo

1. **/dashboard (visão executiva)**
   - KPIs em cards
   - Resumo de estado dos leads
   - Evolução temporal
   - Top atendentes
   - Alertas prioritários
2. **/leads**
   - tabela com paginação, busca e filtros
   - coluna de estado, origem, unidade, data
   - drawer/modal com detalhes
3. **/analytics**
   - analytics por unidade
   - comparativo por período
   - métricas por estado
4. **/analytics/lead/[id]**
   - timeline do lead
   - tempo por estado
   - eventos de mudança
5. **/operations/queue**
   - fila e aguardando resposta
   - cards operacionais em tempo real
6. **/reports**
   - seleção mês/ano/unidade
   - download PDF
7. **/admin/config**
   - gerenciamento da API key Cloudia

## 7) Gráficos obrigatórios (vários)

Implemente **no mínimo 12 gráficos**, com skeleton/loading e tooltips:

1. **Funnel Chart**: etapas do lead (origem em `/webhooks/etapa-agrupada`).
2. **Line Chart**: evolução diária de leads por período.
3. **Bar Chart empilhado**: bot/queue/service/concluido (`/webhooks/count-by-state`).
4. **Pie Chart**: distribuição por origem (`/webhooks/origem-cloudia`).
5. **Donut Chart**: sem pagamento vs com pagamento.
6. **Area Chart**: volume de leads por hora (derivado de timestamps).
7. **Heatmap semanal**: atividade por dia/hora (leads fim de semana).
8. **Ranking Bar Horizontal**: atendentes e performance (`/assignments/ranking`).
9. **Gauge Chart**: SLA de resposta média (`/metrics/resumo`).
10. **Waterfall**: variação de backlog na fila (comparação entre snapshots).
11. **Scatter Plot**: tempo de resposta vs conversão por unidade.
12. **Radar Chart**: score operacional por unidade (fila, serviço, conclusão, atraso).

Para cada gráfico, gerar:
- componente
- hook de dados
- adapter de transformação
- legenda e explicação de negócio

## 8) UX/UI esperado

- Design clean, profissional, responsivo.
- Tema claro/escuro.
- Layout com sidebar + header + breadcrumbs.
- Filtros persistidos na URL (`searchParams`).
- Suporte a pt-BR (datas e número).
- Acessibilidade mínima (aria-label, contraste, foco teclado).

## 9) Segurança e robustez no front

- Nunca expor `X-Admin-Key` hardcoded.
- Ler segredos de `process.env.NEXT_PUBLIC_*` apenas para dados públicos.
- Criar middleware de erro global para API.
- Tratar status HTTP:
  - 400 (validação)
  - 401/403 (acesso)
  - 404 (não encontrado)
  - 500+ (erro interno)
- Retry inteligente em GET, sem retry em mutações sensíveis.

## 10) Entregáveis (quero tudo)

1. Estrutura completa de pastas e arquivos.
2. `package.json` com scripts **pnpm**:
   - `pnpm dev`
   - `pnpm build`
   - `pnpm start`
   - `pnpm lint`
   - `pnpm typecheck`
3. `.env.example` com:
   - `NEXT_PUBLIC_API_BASE_URL=http://localhost:5000`
4. Camada API completa tipada.
5. Hooks React Query por endpoint.
6. Todas as páginas listadas com componentes reais.
7. Biblioteca de gráficos com componentes prontos e reutilizáveis.
8. README completo com setup, arquitetura e deploy.
9. Estratégia de testes:
   - unitário (Vitest)
   - componente (RTL)
   - e2e (Playwright)

## 11) Ordem de geração (resposta esperada)

Responda em 6 blocos:
1. Estrutura de pastas
2. Dependências e comandos pnpm
3. Base da app (layout/providers/theme)
4. Camada API + tipos + hooks
5. Páginas + componentes + gráficos
6. README + próximos passos

Gere código real, não resumo conceitual.

---

## Extra recomendado (geração de tipos via OpenAPI)

```bash
pnpm dlx openapi-typescript http://localhost:5000/swagger/v1/swagger.json -o src/api/types/generated.ts
```

## Bootstrap inicial do projeto Next.js com pnpm

```bash
pnpm create next-app@latest lead-analytics-frontend --ts --eslint --app --src-dir --import-alias "@/*"
cd lead-analytics-frontend
pnpm add @tanstack/react-query axios zod react-hook-form @hookform/resolvers date-fns recharts echarts echarts-for-react clsx tailwind-merge lucide-react
pnpm add -D vitest @testing-library/react @testing-library/jest-dom @playwright/test
```
