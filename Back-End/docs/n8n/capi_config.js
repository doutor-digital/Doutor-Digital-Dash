// ─── Config: resolve a unidade e o pixel ────────────────────────────────────
//
// O multi-tenant sai do phone_number_id: cada unidade tem o seu número de
// WhatsApp, e ele vem em toda mensagem. Assim o mesmo fluxo serve a rede inteira
// sem variável por unidade — acrescentar clínica é acrescentar uma linha no mapa.
//
// META_CAPI_UNIDADES é um JSON no ambiente do n8n, por exemplo:
// {"728123456789012":{"unidade":"imperatriz","pixel":"1076495867156119"}}
//
// Se o mapa não tiver o número, o evento NÃO é enviado. Mandar para o pixel
// errado suja o aprendizado da campanha de outra clínica, e isso não tem desfazer.

const mapa = (() => {
  try { return JSON.parse($env.META_CAPI_UNIDADES || '{}'); }
  catch { return {}; }
})();

const cfg = {
  mapa,
  token: $env.META_CAPI_TOKEN || '',
  graphVersion: $env.META_GRAPH_VERSION || 'v23.0',
  // Pixel único de fallback, para quem roda uma clínica só.
  pixelPadrao: $env.META_PIXEL_ID || '',
  // Código de teste do Gerenciador de Eventos. Com ele preenchido, o evento
  // aparece em "Eventos de teste" e NÃO entra na otimização — é o que permite
  // conferir antes de valer para a campanha.
  testEventCode: $env.META_CAPI_TEST_CODE || '',
  // Janela de atribuição do clique no WhatsApp. Evento mais velho que isso a
  // Meta aceita mas não credita ao anúncio; enviar só gera ruído.
  janelaDias: Number($env.META_CAPI_JANELA_DIAS || 7),
};

// Cada etapa da Kommo vira um evento do funil da Meta.
const EVENTOS = {
  mensagem:           'ViewContent',
  lead_quente:        'Lead',
  agendamento:        'AddToCart',
  consulta_realizada: 'InitiateCheckout',
  compra:             'Purchase',
};

return [{ json: { cfg, EVENTOS } }];
