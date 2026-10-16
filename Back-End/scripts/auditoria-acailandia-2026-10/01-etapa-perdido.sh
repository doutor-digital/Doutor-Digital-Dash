#!/usr/bin/env bash
# ============================================================================
#  Conserto de dado · PERDIDO gravado como "tratamento cancelado"
#  Auditoria do dashboard da Açailândia (01–16/10/2026)
#
#  O QUE ACONTECEU
#  O webhook traduzia a etapa só pelo número. O 143 existe nos dois funis — no
#  COMERCIAL é "PERDIDO", no TRATAMENTO é "TRATAMENTO CANCELADO" — e o último
#  funil lido vencia. Resultado: 180 de 180 perdidos da Açailândia gravados como
#  TRATAMENTO_CANCELADO (e o 142 GANHO do COMERCIAL como ALTA). O código novo
#  já grava certo; este script conserta o que ficou gravado errado.
#
#  ANTES DE RODAR
#  1. O back novo precisa estar no ar (as rotas abaixo só existem nele; a coluna
#     nova "CurrentPipelineId" é criada sozinha pela migration no deploy).
#  2. A chave de admin vai pela variável ADMIN_KEY — o script nunca a imprime:
#        read -rs ADMIN_KEY && export ADMIN_KEY
#  3. Confira o id da unidade (padrão 14 = Açailândia):
#        SELECT "Id" FROM units WHERE "Slug" = 'doutor-hernia-acailandia';
#
#  COMO RODAR (nesta ordem)
#     1) No painel, unidade Açailândia → "Sincronizar com a Kommo" e espere terminar.
#        É o sync que grava o funil certo de cada lead, lido direto da Kommo, e já
#        corrige a maioria dos perdidos. O conserto abaixo NÃO adivinha funil (review
#        de 16/10: adivinhar pelo histórico gravava ALTA como "fechou tratamento").
#     2) ./01-etapa-perdido.sh                          → só CONFERE. Não grava nada.
#     3) SYNC_FEITO=1 MODO=gravar ./01-etapa-perdido.sh → grava o que sobrou.
#        Sem SYNC_FEITO=1 o modo gravar roda o sync aqui mesmo, antes (pode levar minutos).
#
#  O QUE CADA PASSO MEXE
#   • etapa atual do lead (leads."CurrentStage")  → corrigir-etapa-atual
#   • rótulo do histórico (lead_stage_histories."StageLabel") → corrigir-rotulos
#   O id da etapa (o número) nunca é alterado. Nada é escrito na Kommo nem na
#   franquia: o sync só LÊ a Kommo e grava no banco do dashboard.
# ============================================================================
set -euo pipefail

API="${API:-https://api-vps.doutordigitalconsultoria.com}"
UNIDADE="${UNIDADE:-14}"
MODO="${MODO:-conferir}"
DIAS_HISTORICO="${DIAS_HISTORICO:-120}"

if [[ -z "${ADMIN_KEY:-}" ]]; then
  echo "Defina ADMIN_KEY antes (read -rs ADMIN_KEY && export ADMIN_KEY)." >&2
  exit 1
fi

# --fail-with-body: resposta 4xx/5xx PARA o script (set -e) e mostra o motivo. Sem isso,
# um erro no passo 1 deixaria os passos 2 e 3 rodarem mesmo assim.
chamar() { # método, caminho
  curl -sS --fail-with-body --max-time 300 -X "$1" "$API$2" \
    -H "X-Admin-Key: $ADMIN_KEY" -H "Content-Length: 0"
  echo
}

echo "== Placar do histórico (só leitura) =="
chamar GET "/internal/stage-map/saude?dias=$DIAS_HISTORICO"

# O mapa id→nome das etapas (tabela kommo_stages) é a régua das duas correções. Se a
# conferência responder "unidade sem mapa de etapas", rode com ATUALIZAR_MAPA=1: só LÊ
# os funis na Kommo e regrava esse mapa — não mexe em lead nem em histórico.
if [[ "$MODO" == "gravar" || "${ATUALIZAR_MAPA:-0}" == "1" ]]; then
  echo "== 1. Relê os funis da Kommo e atualiza o mapa id→nome (só leitura na Kommo) =="
  chamar POST "/internal/stage-map/sync?unitId=$UNIDADE"
fi

if [[ "$MODO" == "gravar" ]]; then

  # O sync vem ANTES do conserto, sempre: é ele que grava o funil de cada lead a partir da Kommo.
  if [[ "${SYNC_FEITO:-0}" != "1" ]]; then
    echo "== 1b. Sync profundo da unidade ANTES do conserto (lê a Kommo; pode levar vários minutos) =="
    echo "   (se você já clicou em 'Sincronizar com a Kommo' no painel e ele terminou, rode com SYNC_FEITO=1)"
    curl -sS --fail-with-body --max-time 1800 -X POST "$API/internal/sync/kommo/units/$UNIDADE?maxLeads=20000" \
      -H "X-Admin-Key: $ADMIN_KEY" -H "Content-Length: 0"
    echo
  fi

  echo "== 2. Corrige a ETAPA ATUAL dos leads (só os que têm funil conhecido) =="
  chamar POST "/internal/stage-map/corrigir-etapa-atual?unitId=$UNIDADE&simular=false"

  echo "== 3. Corrige o RÓTULO do histórico (últimos $DIAS_HISTORICO dias) =="
  chamar POST "/internal/stage-map/corrigir-rotulos?unitId=$UNIDADE&dias=$DIAS_HISTORICO&simular=false"

  # Quem sobrar em "semFunil" depois do sync não está mais na Kommo (cartão apagado) ou passou
  # do limite de leads do sync: fica como está — melhor errado à vista do que "corrigido" no chute.
fi

echo "== Conferência (simulação: mostra o que AINDA seria corrigido) =="
echo "-- etapa atual: 'mudancas' lista de→para."
echo "   ANTES do sync: os 142/143 aparecem em 'semFunil' (o conserto não adivinha funil) — é normal."
echo "   DEPOIS do sync: TRATAMENTO_CANCELADO → PERDIDO só para quem o sync ainda não corrigiu."
echo "   'semFunil' que sobra depois do sync = cartão que não está mais na Kommo: não é tocado."
echo "   'funisPreenchidos' = leads que só ganham o funil (a etapa já estava certa)."
echo "   Só entram etapas 142/143 e etapas gravadas como número cru; o resto não é tocado."
chamar POST "/internal/stage-map/corrigir-etapa-atual?unitId=$UNIDADE&simular=true"
echo "-- histórico:"
chamar POST "/internal/stage-map/corrigir-rotulos?unitId=$UNIDADE&dias=$DIAS_HISTORICO&simular=true"

if [[ "$MODO" == "gravar" ]]; then
  echo "Depois de gravar, a conferência acima deve mostrar 'corrigidos': 0."
else
  echo "Nada foi gravado. Se os números baterem, rode de novo com MODO=gravar."
fi
