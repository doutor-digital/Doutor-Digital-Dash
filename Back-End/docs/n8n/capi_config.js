// ═══ CONFIGURAÇÃO ÚNICA ═══════════════════════════════════════════════════
// Todo id vive aqui. Id espalhado pelo fluxo é o que faz ninguém ter coragem
// de mexer depois — e é o que transforma "adicionar uma unidade" em caçada.

const cfg = {
  // ── Destino ──────────────────────────────────────────────────────────────
  graphVersion: $env.META_GRAPH_VERSION || 'v21.0',
  token:        $env.META_CAPI_TOKEN || '',

  // Código do Gerenciador de Eventos. Com ele preenchido o evento aparece em
  // "Eventos de teste" e NÃO entra na otimização. Comece com ele, apague depois.
  testEventCode: $env.META_CAPI_TEST_CODE || '',

  // Roda o fluxo inteiro e grava no log SEM chamar a Meta. Serve para conferir
  // o casamento de telefone e o mapa de etapas antes de valer para a campanha.
  modoTeste: String($env.META_CAPI_DRY_RUN || 'false').toLowerCase() === 'true',

  // ── Unidades ─────────────────────────────────────────────────────────────
  // Chave = pipeline_id do funil. É o filtro que protege o pixel: conversão de
  // outra unidade caindo no pixel de Imperatriz contamina a otimização das
  // campanhas dela, e o estrago é silencioso — ninguém vê, a campanha só piora.
  unidades: {
    '14091100': {
      nome: 'imperatriz',
      pixel: '1076495867156119',
      wabaId: '1558318502323307',
      phoneNumberId: '1192220830640227',
    },
  },

  // ── Mapa de etapas → evento (Imperatriz) ─────────────────────────────────
  etapas: {
    '108773008': 'AddToCart',          // AGENDADO
    '108773012': 'InitiateCheckout',   // COMPARECEU
    '142':       'Purchase',           // GANHO / CONCLUÍDO
  },

  // ── Lead quente ──────────────────────────────────────────────────────────
  // Não é mudança de etapa: é o campo de qualificação. O webhook de lead
  // atualizado não traz custom_fields de forma confiável, então o fluxo relê o
  // lead na API e procura por este field_id.
  qualificacao: {
    fieldId: Number($env.KOMMO_CF_QUALIFICACAO || 2440809),
    // "Quente" = 1832805. Numérico também aceito, via valorMinimo.
    enumQuente: Number($env.KOMMO_ENUM_QUENTE || 1832805),
    valorMinimo: Number($env.KOMMO_QUALIF_MINIMO || 0),
  },

  janelaDias: Number($env.META_CAPI_JANELA_DIAS || 7),
};

// Nome do evento por gatilho. LeadSubmitted é o que alimenta a otimização —
// por isso ele tem alerta de falha próprio mais adiante no fluxo.
const EVENTOS = {
  mensagem:           'ViewContent',
  lead_quente:        'LeadSubmitted',
  agendamento:        'AddToCart',
  consulta_realizada: 'InitiateCheckout',
  compra:             'Purchase',
};

return [{ json: { cfg, EVENTOS } }];
