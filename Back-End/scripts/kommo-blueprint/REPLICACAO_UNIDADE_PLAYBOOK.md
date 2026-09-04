# PLAYBOOK — Replicar uma unidade no padrão IMPERATRIZ (Kommo + I.A Sofia)

> **PRINCÍPIO CANÔNICO:** a **Imperatriz** (`attivacorpoementeitz`) é o PADRÃO. Tudo que existe nela
> se replica 1:1 na unidade nova. **Meta = bater 100%** (funis, etapas, campos, enums, obrigatórios por
> etapa, motivos de perda, persona, ações da IA, tags, bots, templates).
>
> **REGRA #1 — NÃO REINVENTAR A RODA.** O provisionamento JÁ ESTÁ CODIFICADO em 2 ferramentas.
> Use-as. Não escreva script ad-hoc pro que elas já fazem (foi o erro de hoje).

---

## AS 2 FERRAMENTAS (a "base")

### A) Estrutura no Kommo → `kommo-blueprint/apply_blueprint.py`
Uma rodada `--apply` faz **tudo** de forma idempotente: `apply_pipelines` + `apply_groups` +
`apply_fields (+enums)` + **`apply_required_statuses`** + **`apply_loss_reasons`** + `apply_main_pipeline`.
Ela **já embute** os gotchas de API (paginação `order[id]=asc`, `patch_bisect`, `currency` em monetary,
sort). NÃO reimplemente required_statuses nem motivos de perda à mão.

```bash
# 1) exportar a MATRIZ (só leitura, não escreve na Imperatriz)
python3 export_account.py --subdomain attivacorpoementeitz --token-file token_itz.txt \
    --skip-pipeline "NÂO USAR" --out blueprint.clinica-v1.json
# 2) DRY-RUN no destino (nada é escrito)
python3 apply_blueprint.py --blueprint blueprint.clinica-v1.json \
    --subdomain <DESTINO> --token-file token_<destino>.txt --lock blueprint.<destino>.lock.json
# 3) APLICAR
python3 apply_blueprint.py ... --apply --confirm-subdomain <DESTINO>
# 4) VERIFICAR: reexportar o destino e comparar com o blueprint
```
O `--lock` grava o mapa `chave→id` do destino → **usar esse lock no n8n/Sofia, nunca id chumbado**.
`--skip-group <chave>` tira um grupo naquela conta (ex: `asaas`, `3c-ligacoes`). ⚠️ Tirar
`attivacorpoementeitz` da lista `PROTECTED` OU rodar via `fase1_campos.py` (bypass sancionado) —
mas só use fase1 se precisar de subset; o normal é o apply completo.

### B) IA no backend (a "Fase E") → `backend/src/scripts/replicate-serra.ts` (MOLDE)
A Serra foi provisionada assim. Clona **persona + UnitActions** da Imperatriz e troca só o que é único
do destino. Copiar pra `replicate-<unidade>.ts`, ajustar as constantes e ENV:
```bash
<UNIDADE>_KOMMO_TOKEN=... <UNIDADE>_GEMINI_KEY=... \
<UNIDADE>_PAUSED_FIELD_ID=... <UNIDADE>_REPLY_FIELD_ID=... \
<UNIDADE>_WON_STATUS_IDS=142 <UNIDADE>_ALLOWED_STATUS_IDS=<etapas> \
  pnpm --filter agente-dt-backend exec tsx src/scripts/replicate-<unidade>.ts
```
Idempotente (aborta se a unidade já tem ações). **Não copia** creds/IDs de etapa/campo (diferem por
conta) → setar via ENV (do `--lock` do apply_blueprint) ou painel. Depois:
- Webhook Kommo `add_message` → `api-vps.doutordigitalconsultoria.com/webhooks/kommo/<slug>`.
- As **tags** usadas pelas ações `add_tag` precisam EXISTIR no Kommo destino (string exata).
- **DESLIGAR o sistema antigo** da unidade (ver Fase 7) pra não ter resposta dupla.

---

## FLUXO A — UNIDADE NOVA (Kommo limpo) — o caso simples
1. **Fase 0** — token admin + moeda.
2. **Fase 1** — `apply_blueprint.py --apply` (faz funis+etapas+campos+enums+obrigatórios+motivos DE UMA VEZ).
3. **Fase 2** — `replicate-<unidade>.ts` (persona + ações da Imperatriz) + setar field/stage IDs do lock.
4. **Fase 3** — webhook Kommo `add_message` pro backend; criar tags no Kommo; ligar workers no piloto.
5. **Fase 4** — verificar 100% vs Imperatriz (script de comparação). Pronto.

## FLUXO B — UNIDADE ANTIGA com estrutura divergente (tipo Araguaína, Canaã)
Igual ao A, MAS entre as fases entram 3 passos. **Desde Canaã (ago/2026) eles TAMBÉM estão
codificados** — `fase2_funil.py` (B1+B2) e `fase3_backfill.py` (B3). Não escrever custom de novo:
o que muda de unidade são as tabelas `RENAMES`/`DEPARA` no topo dos scripts.
- **B1. Restruturar o funil por RENAME-NO-LUGAR** (não usar apply_pipelines, que CRIA funil novo):
  renomear etapa preserva o ID → leads não movem, automações seguem. Renomear com `name+color+sort`
  juntos, em 2 passadas de sort. 142/143 e entrada renomeiam NA TELA.
- **B2. Migrar leads** entre etapas por de-para (mover `status_id`), com `order[id]=asc` + throttle +
  bisecção. **Guardar a lista de IDs por etapa ANTES.** Confirmar de-para de fechamento/no-show com o dono.
- **B3. Backfill de dados legado→canônico** (de-para de campo + enum-map por NOME), idempotente, convergir
  até 0. Só depois da **Fase 7** (religar IA pro canônico) é seguro **apagar os campos legados**.

### As ferramentas do fluxo B (ordem de execução)
```bash
# B0 — SEMPRE primeiro: dump de leads COM custom_fields_values. É o desfazer E o backup dos legados.
python3 fase2_funil.py --subdomain <sub> --token-file t.txt --lock <lock> \
    --snapshot snapshot.<sub>.json --step snapshot
cp <dump> backup_campos_antigos_<sub>.json
# B1/B2 — um passo por vez, dry-run antes de cada --apply
... --step webhook-off | rename | migrate | cleanup | sort | webhook-on
# B3 — backfill legado -> canônico (roda contra o BACKUP, não contra a conta viva)
python3 fase3_backfill.py --subdomain <sub> --token-file t.txt --lock <lock> \
    --backup backup_campos_antigos_<sub>.json [--apply --confirm-subdomain <sub>]
# Fechamento — a 2ª passada do apply completo (obrigatórios por etapa + is_main)
python3 apply_blueprint.py ... --force-protected --apply --confirm-subdomain <sub>
python3 verificar.py --blueprint blueprint.itz-fresh.json --subdomain <sub> --token-file t.txt
```
`--force-protected` é a porta para conta que já está na lista `PROTECTED` e virou destino legítimo
(Imperatriz continua bloqueada em separado, sem porta).

**O dump de B0 é inegociável.** Em Canaã o dono apagou 14 campos legados no meio da replicação, antes
do backfill. Os dados só voltaram porque o dump de antes existia — `fase3_backfill.py` lê do backup
justamente por isso. Sem o dump, `⚑ Origem` (957 leads), `★ Qualificação` (923) e `⚥ Sexo` (828) teriam
sumido sem cópia.

---

## GOTCHAS — o que deu errado hoje (aplicar/lembrar SEMPRE)

**As ferramentas já tratam** (por isso use-as): paginação `order[id]=asc`, `patch_bisect` no 400,
`currency` em PATCH monetary, sort de motivo 1-100000, `is_unsorted_on` no POST de pipeline.

**Ainda mordem, mesmo com as ferramentas — VERIFICAR:**
1. **Monetary por API = HTTP 500 — RESOLVIDO (22/08/2026): use `criar_monetary.py`.**
   `POST /api/v4/leads/custom_fields` com `type=monetary` dá 500 em toda conta de clínica: com Bearer
   E com cookie, com/sem grupo, um a um e em lote, BRL e USD, com a conta em **zero** monetary. Não é
   limite de plano nem payload — a rota v4 não cria esse tipo. **A rota que a TELA usa cria:**
   `POST /ajax/settings/custom_fields/` (form-urlencoded + cookie de sessão, `type_id=23`). Isso está
   codificado agora:
   ```bash
   python3 criar_monetary.py --blueprint blueprint.itz-<data>.json --subdomain <sub> \
       --token-file token_<sub>.txt --cookie-file cookie_<sub>.txt --lock blueprint.<sub>.lock.json
   ... --apply --confirm-subdomain <sub>
   python3 apply_blueprint.py ... --apply --confirm-subdomain <sub>   # 2ª passada: gruda os required
   ```
   Três detalhes que custam tempo se esquecer: (a) o campo nasce **sem grupo** — quem coloca no grupo
   e ordena é `PATCH /ajax/v4/leads/custom_fields/groups/<gid>` com a lista COMPLETA de ids; (b) esse
   PATCH **não reescreve `sort`**, e o export ordena por sort — grave sort explícito depois (o script
   faz); (c) **`code` só entra na criação** (`ASAAS_VALUE`/`ASAAS_NET_VALUE`) — `PATCH /api/v4` devolve
   `400 OnlyNull`; se esqueceu, apague e recrie. O cookie sai do navegador logado (browser-harness:
   `cdp("Network.getCookies", urls=["https://<sub>.kommo.com/"])`), precisa de `session_id`+`csrf_token`.
   **Pendente nas unidades feitas antes disto** (Araguaína, Açailândia, Porto, Canaã, Balsas, Marabá,
   Parauapebas, Boa Vista, Boituva, Olímpia): rodar o script nelas fecha os 6 `¤` de cada uma.
2. **142/143 ACEITAM `required_statuses` por API** — o comentário antigo dizia que "responde 200 e devolve
   []"; retestado em Balsas e em Canaã e GRUDA. O `apply_required_statuses` já manda sempre e **relê os
   campos** (`_verify_required`), então só cai em "marcar na UI" o que o destino recusou de fato.
   **Nunca assumir gotcha sem testar.**
3. **142/143 e etapa de entrada NÃO RENOMEIAM por API** → tela.
4. **PATCH de campo com `enums` SUBSTITUI a lista e reatribui ids** — pra adicionar opção, mandar a lista
   COMPLETA. (Vale quando precisar de valores extras de select específicos da unidade, ex: SDRs locais.)
5. **PATCH de status SEM `name` ZERA o nome**; cor cinza (#c1c1c1/#d5d8db) = 400 em etapa comum. (Só
   relevante no fluxo B1 rename-no-lugar.)
6. **Campos canônicos têm símbolos ⚑☻⬢⌂✓★⊘⚕◷¤⚥⚒✎↗№** (fácil esquecer ⚥/⚒) → parear por `code`/lock, não por símbolo.
7. **Filtro salvo NÃO tem API** (`/filters`,`/segments`=404). `POST /api/leads/custom_presets/` (ajax+cookie)
   cria mas **NÃO exibe** no painel → filtro clicável só pela TELA.

**Sequência crítica (não inverter):**
8. **Descobrir a IA da unidade pelos webhooks** (`GET /api/v4/webhooks`) ANTES de mexer. Nome entrega
   (ex: `.../webhook/cloudia-etapa` = Cloudia em n8n externo, NÃO a nossa Sofia).
9. **Apagar campo legado QUEBRA a captura se a IA ainda escreve nele.** Ordem: migrar dados → Fase 7
   (religar IA pro canônico + desligar sistema antigo) → SÓ ENTÃO apagar legado. Testar: lead recente com
   legado preenchido + canônico vazio = IA ainda no legado. NÃO apagar campo sem canônico (perde sem cópia).
10. **De-para de etapas de fechamento/no-show = revisar com o dono.** (Erramos FALTOU_CONSULTA → EM
    NEGOCIAÇÃO; certo era AGENDADO. Recuperável via API de eventos: `value_before.lead_status.id` guarda
    o id da etapa APAGADA.)

---

## VERIFICAÇÃO FINAL (comparar com Imperatriz por API — tudo 100%)
- Funis/etapas: nomes + ordem batem.
- Campos: existência por nome/code (via lock).
- **Obrigatórios por etapa**: `required_statuses` por etapa (nome) idênticos — INCLUI ALTA/CANCELADO (142/143).
- Motivos de perda: mesmo conjunto.
- (Fluxo B) Backfill: canônico >= legado em todo campo migrado; convergência com 0 gravações.
- IA: unidade no `ddagentdb` com persona + N ações da Imperatriz; webhook `add_message` apontando pro backend.

---

## PROMPT PRA COLAR NA PRÓXIMA REPLICAÇÃO
"Replique a unidade `<SUBDOMINIO>` no padrão 100% da Imperatriz (matriz `attivacorpoementeitz`).
**Use as ferramentas existentes, não reinvente:** estrutura Kommo via `apply_blueprint.py --apply`
(faz pipelines+campos+enums+required_statuses+motivos de perda de uma vez, idempotente); IA via
`replicate-serra.ts` como molde (clona persona+ações da Imperatriz, troca só creds/IDs do destino).
Se a unidade já tiver funil divergente com leads, aí sim faça custom: rename-no-lugar do funil
(preserva IDs/leads), migração de leads por de-para (com order[id]=asc + bisecção, guardando IDs
antes), e backfill legado→canônico. NÃO apague campos legados antes de religar a IA pro canônico e
desligar o sistema antigo. Verifique 100% vs Imperatriz ao fim de cada fase. Nunca assuma um gotcha
sem testar (ex: 142/143 aceita obrigatório por API). Aplique todos os gotchas do playbook."
