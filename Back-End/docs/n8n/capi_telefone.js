// ═══ CASAMENTO DE TELEFONE ════════════════════════════════════════════════
// A maior fonte de perda de atribuição nesse tipo de integração, e não é
// exagero: o WhatsApp entrega "5599981991934" e a recepção digitou
// "(99) 8199-1934". Comparar as duas strings acerta quase nada.
//
// A regra é comparar só dígitos e gerar as variantes brasileiras: com e sem o
// DDI 55, com e sem o nono dígito. Os 8 últimos são o que sobra sempre, mas
// sozinhos casam pessoas diferentes de DDDs diferentes — por isso as variantes
// vêm antes, e o sufixo de 8 é o último recurso.

function variantes(bruto) {
  const d = String(bruto || '').replace(/\D/g, '');
  if (d.length < 8) return [];

  const v = new Set([d]);
  const semDdi = d.startsWith('55') ? d.slice(2) : d;
  v.add(semDdi);
  v.add('55' + semDdi);

  // Nono dígito: entra e sai conforme a época do cadastro.
  if (semDdi.length === 11 && semDdi[2] === '9') {
    const sem9 = semDdi.slice(0, 2) + semDdi.slice(3);
    v.add(sem9); v.add('55' + sem9);
  }
  if (semDdi.length === 10) {
    const com9 = semDdi.slice(0, 2) + '9' + semDdi.slice(2);
    v.add(com9); v.add('55' + com9);
  }
  return [...v];
}

return $input.all().map((item) => {
  const t = item.json.telefone || item.json.phone || '';
  return { json: { ...item.json, variantes: variantes(t), sufixo8: String(t).replace(/\D/g,'').slice(-8) } };
});
