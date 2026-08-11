// ═══ MONTA O EVENTO E DECIDE SE ELE SAI ═══════════════════════════════════
// Ponto único de montagem: os três gatilhos convergem aqui. Triplicar esta
// lógica seria garantir que as três cópias divergissem na primeira correção.
//
// Três campos precisam andar juntos para a Meta casar o evento com o anúncio:
// action_source 'business_messaging', messaging_channel 'whatsapp' e o
// ctwa_clid dentro de user_data. Faltando um, o evento entra e não atribui —
// e o sintoma é o pior possível: aparece "recebido" no Gerenciador, com zero
// atribuição, o que parece funcionamento.

const { cfg, EVENTOS } = $('Config').first().json;
const agora = Math.floor(Date.now() / 1000);
const saida = [];
const descartes = [];

for (const item of $input.all()) {
  const d = item.json;
  const unidade = cfg.unidades[String(d.pipeline_id)] || null;

  // Filtro de unidade primeiro, antes de qualquer processamento.
  if (!unidade) {
    descartes.push({ motivo: 'unidade_fora_do_mapa', event_name: d.event_name,
      kommo_lead_id: d.kommo_lead_id, pipeline_id: String(d.pipeline_id || ''), detalhe: d });
    continue;
  }

  // Sem clique não há o que atribuir. Não é erro — é lead de outra origem.
  if (!d.ctwa_clid) {
    descartes.push({ motivo: 'sem_ctwa_clid', event_name: d.event_name,
      kommo_lead_id: d.kommo_lead_id, telefone: d.telefone, detalhe: d });
    continue;
  }

  const quando = d.event_time || agora;
  if (agora - quando > cfg.janelaDias * 86400) {
    descartes.push({ motivo: 'fora_da_janela_7d', event_name: d.event_name,
      kommo_lead_id: d.kommo_lead_id, detalhe: { quando, agora } });
    continue;
  }

  const evento = {
    event_name: d.event_name,
    event_time: quando,
    action_source: 'business_messaging',
    messaging_channel: 'whatsapp',
    event_id: d.event_id,
    user_data: {
      ctwa_clid: d.ctwa_clid,
      whatsapp_business_account_id: unidade.wabaId,
    },
  };
  // Valor só em Purchase, e só quando existe. Mandar 0 ensina a Meta que a
  // venda vale zero.
  if (d.event_name === EVENTOS.compra && Number(d.valor) > 0) {
    evento.custom_data = { currency: 'BRL', value: Number(d.valor) };
  }

  saida.push({ json: {
    ...d,
    unidade: unidade.nome,
    pixel: unidade.pixel,
    corpo: { data: [evento], ...(cfg.testEventCode ? { test_event_code: cfg.testEventCode } : {}) },
  }});
}

// O nó Code do n8n tem UMA saída só — devolver [aprovados, descartados] faz ele
// ler uma lista de listas e recusar tudo. Sai uma lista única, marcada com
// _aprovado, e o IF seguinte separa os dois caminhos.
return [
  ...saida.map((x) => ({ json: { ...x.json, _aprovado: true } })),
  ...descartes.map((x) => ({ json: { ...x, _aprovado: false } })),
];
