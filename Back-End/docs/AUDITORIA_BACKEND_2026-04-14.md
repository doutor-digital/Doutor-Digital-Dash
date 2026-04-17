# Auditoria técnica do backend (`LeadAnalytics.Api`)

Data: 2026-04-14
Escopo: análise estática de código (controllers, services, configuração e modelagem).

## 1) Resumo executivo

### Riscos críticos / altos

1. **Webhook Meta com token hardcoded (`"seu_token"`)** — risco de segurança e quebra em produção.
2. **Ausência de autenticação/autorização robusta na API** — quase todos endpoints públicos.
3. **Uso de reflection para acessar campo privado do service** em `ConfigurationController` — quebra encapsulamento e manutenção.
4. **Mensagens de erro internas retornadas ao cliente (`ex.Message`)** — vazamento de detalhes internos.

### Riscos médios

5. **Bug lógico em transição de `ConversationState` no update de lead** (comparação após sobrescrita do valor).
6. **Inconsistência de rotas e semântica REST** (`/webhooks` com endpoints de leitura analítica).
7. **Validação de payload insuficiente em alguns DTOs e endpoints administrativos.**
8. **Falta de suíte de testes automatizados no repositório.**

### Riscos baixos / dívida técnica

9. **Mensagens e nomes misturados (pt/en), ortografia e padronização de rotas/controladores.**
10. **Comentários de código temporário sem plano de remoção (TODOs críticos).**

---

## 2) Principais achados com evidência técnica

## 2.1 Segurança

- `MetaWebhookController.VerifyWebhook` usa `const string VERIFY_TOKEN = "seu_token"`.
  - Impacto: token previsível e potencial bypass de verificação em ambientes mal configurados.
  - Recomendação: mover para configuração (`Meta:VerifyToken`) + rotação segura.

- API expõe endpoints sem autenticação centralizada (`UseAuthorization()` sem `UseAuthentication()` e sem policies globais).
  - Impacto: acesso indevido a dados e operações administrativas.
  - Recomendação: JWT/API Key com políticas por rota (admin/webhook/internal).

- `ConfigurationController.DeleteCloudiaApiKey` usa reflection para obter `_db` de `ConfigurationService`.
  - Impacto: acoplamento frágil, risco de falha em refactor e comportamento inesperado.
  - Recomendação: adicionar método explícito no service/repositório para exclusão segura.

- Retornos de erro com `ex.Message` em múltiplos endpoints.
  - Impacto: vazamento de detalhes de infraestrutura/negócio para cliente externo.
  - Recomendação: retornar mensagens genéricas e registrar detalhes apenas em log estruturado.

## 2.2 Confiabilidade e regras de negócio

- Em `LeadService.UpdateLeadAsync`, `lead.ConversationState` é atualizado antes da comparação que deveria detectar mudança de estado.
  - Impacto: histórico de conversas/interações pode não ser criado em cenários válidos.
  - Recomendação: comparar `estadoAnterior` vs `dto.ConversationState` antes de sobrescrever.

- Validação de dados críticos está comentada em `CreateLeadAsync` (telefone/email).
  - Impacto: persistência de leads com placeholders (`AGUARDANDO_COLETA`) sem estratégia de saneamento.
  - Recomendação: reativar validação com feature flag e fila de enriquecimento.

## 2.3 API design e experiência de integração

- Controlador `WebhooksController` mistura ingestão webhook e consultas analíticas.
  - Impacto: documentação OpenAPI menos clara para frontend e integrações.
  - Recomendação: separar por bounded context:
    - `/api/webhooks/*` (entrada de eventos)
    - `/api/leads/*` (consulta operacional)
    - `/api/analytics/*` (indicadores)

- Existem logs/descrições citando `/api/leads/*`, mas rota real está em `/webhooks/*`.
  - Impacto: confusão para front e automações.

## 2.4 Observabilidade e operação

- Boa prática observada: logs estruturados existem em vários pontos.
- Lacuna: sem correlação de requisição (trace id), sem métricas técnicas e sem health checks completos (db/downstream).

## 2.5 Qualidade de código e manutenção

- Projeto em `net10.0` e pacotes recentes; validar compatibilidade de ambiente CI/CD.
- Ausência de testes unitários e integração detectáveis no repositório.

---

## 3) Plano de ação recomendado (priorizado)

## Sprint 0 (urgente)

1. Remover token hardcoded e configurar segredo por ambiente.
2. Implementar autenticação (JWT ou API key por policy) e travar endpoints administrativos.
3. Corrigir bug de transição de `ConversationState` no `UpdateLeadAsync`.
4. Eliminar reflection do controller e mover operação para service.
5. Padronizar tratamento de exceção (problem details + mensagem genérica).

## Sprint 1

6. Separar rotas por domínio e versionar API (`/api/v1/...`).
7. Adicionar validação formal de DTOs (FluentValidation já existe como pacote).
8. Criar testes mínimos:
   - unitários de `LeadService`
   - integração para principais endpoints.

## Sprint 2

9. Adicionar health checks (db + cloudia) e métricas de observabilidade.
10. Definir contrato OpenAPI estável para geração de SDK do frontend.

---

## 4) Como gerar o cliente do frontend baseado no backend

Pré-requisito: API rodando localmente com Swagger habilitado.

### Opção A (recomendada): gerar tipos TypeScript

```bash
npx openapi-typescript http://localhost:5000/swagger/v1/swagger.json -o src/api/generated/types.ts
```

### Opção B: gerar client completo (fetch/axios)

```bash
npx @openapitools/openapi-generator-cli generate \
  -i http://localhost:5000/swagger/v1/swagger.json \
  -g typescript-axios \
  -o src/api/generated/client
```

### Observação importante

Antes de gerar o client, é recomendável corrigir a taxonomia de rotas para evitar contratos confusos no front (ex.: endpoints de consulta em `/webhooks`).

---

## 5) Nota metodológica

Esta auditoria foi feita por análise estática do código-fonte e configuração do projeto. Não foi possível executar build/testes automatizados no ambiente atual por indisponibilidade do `dotnet` no PATH.
