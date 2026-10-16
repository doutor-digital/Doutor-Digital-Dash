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
#  COMO RODAR
#     ./01-etapa-perdido.sh            → só CONFERE (padrão). Não grava nada.
#     MODO=gravar ./01-etapa-perdido.sh → grava, e confere de novo no fim.
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

  echo "== 2. Corrige a ETAPA ATUAL dos leads =="
  chamar POST "/internal/stage-map/corrigir-etapa-atual?unitId=$UNIDADE&simular=false"

  echo "== 3. Corrige o RÓTULO do histórico (últimos $DIAS_HISTORICO dias) =="
  chamar POST "/internal/stage-map/corrigir-rotulos?unitId=$UNIDADE&dias=$DIAS_HISTORICO&simular=false"

  # Os leads que sobrarem em "semFunil" (143/142 sem funil no banco) se resolvem num sync
  # da unidade: ele lê o funil de cada cartão na Kommo. Caminho recomendado: botão
  # "Sincronizar com a Kommo" da unidade no painel (roda em segundo plano). Por aqui é
  # síncrono e o proxy pode cortar a conexão de um sync longo — por isso fica opcional.
  if [[ "${SYNC_PROFUNDO:-0}" == "1" ]]; then
    echo "== 4. Sync profundo da unidade (lê a Kommo; pode levar vários minutos) =="
    curl -sS --fail-with-body --max-time 1800 -X POST "$API/internal/sync/kommo/units/$UNIDADE?maxLeads=20000" \
      -H "X-Admin-Key: $ADMIN_KEY" -H "Content-Length: 0"
    echo
  fi
fi

echo "== Conferência (simulação: mostra o que AINDA seria corrigido) =="
echo "-- etapa atual: 'mudancas' lista de→para; esperado ANTES de gravar:"
echo "   TRATAMENTO_CANCELADO → PERDIDO (~180) e ALTA → 09_FECHOU_TRATAMENTO (GANHO do COMERCIAL)."
echo "   'semFunil' = 143/142 sem funil conhecido; resolve com o sync da unidade (ver passo 4)."
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
