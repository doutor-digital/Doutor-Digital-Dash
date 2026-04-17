# LeadFlow Insights — Dashboard

Frontend profissional para o backend LeadFlow (Cloudia + Meta + Analytics).

## Stack

- Vite + React 18 + TypeScript
- TailwindCSS
- React Router
- TanStack Query (cache, refetch, invalidation)
- Recharts (gráficos)
- Axios + interceptors (auth, admin-key, erros)
- Zustand (estado persistente: auth, clinic)

## Rodando localmente

```bash
cp .env.example .env
# preencha VITE_API_BASE_URL com a URL do backend

npm install
npm run dev     # http://localhost:5173
npm run build   # bundle para produção
```

## Rotas do app

| Rota | Finalidade |
|---|---|
| `/login` | Entrada (sessão local enquanto o backend não tiver JWT) |
| `/` | Dashboard consolidado (KPIs, funil, origens, evolução, ao vivo, ativos) |
| `/leads` | Lista com busca, filtros (estado, etapa, origem), paginação e export CSV |
| `/leads/:id` | Detalhe do lead: métricas de tempo, timeline, atendentes, interações |
| `/funnel` | Funil de conversão + drop-off + distribuição por etapa |
| `/sources` | Origens, donut Cloudia vs Meta, tabela de conversão por canal |
| `/evolution` | Série temporal mensal com date range |
| `/live` | Métricas ao vivo (Cloudia): atendentes, fila, tempos |
| `/analytics` | Analytics local por unidade e período |
| `/alerts` | Leads fora do SLA (auto-refresh 30s) |
| `/attendants` | Equipe + ranking |
| `/units` | Unidades (criar, renomear) |
| `/reports` | Relatório mensal (PDF) e diário (JSON) |
| `/settings` | Admin key + chave API Cloudia |

## Integração com o backend

Todos os endpoints do backend estão mapeados em `src/services/*`:

- `webhooks.ts` → `/webhooks/*`
- `metrics.ts` → `/metrics/*`
- `analytics.ts` → `/api/analytics/*`
- `assignments.ts` → `/assignments/*`
- `units.ts` → `/units/*`
- `reports.ts` → `/api/relatorios/mensal`, `/daily-relatory/*`
- `config.ts` → `/api/config/cloudia-api-key*`

## Observações

Sessão de login armazenada em `localStorage` (`leadflow.auth`). Assim que o backend subir JWT,
trocar `LoginPage.onSubmit` por `POST /auth/login` e o token passa a fluir automaticamente pelos interceptors.
