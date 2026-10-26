#!/usr/bin/env python3
"""Gera blueprint.clinica-v5-enxuto.json a partir do export da Açailândia.

Seleciona campos (não redesenha): nome, tipo, enums e is_api_only vêm do molde.
Tudo cai num único grupo PACIENTE. Ordem = ordem dos blocos abaixo.
"""
import json, re, sys, unicodedata
from pathlib import Path

ORIGEM = Path(sys.argv[1])
SAIDA = Path(sys.argv[2])


def slug(text: str) -> str:
    """Mesma função do export_account.py — a chave tem que casar na releitura."""
    norm = unicodedata.normalize('NFKD', text or '')
    norm = ''.join(c for c in norm if not unicodedata.combining(c))
    norm = re.sub(r'[^A-Za-z0-9]+', '-', norm).strip('-').lower()
    return norm or 'campo'

# (chave do export, bloco). Separador = nome começando com * ou ##.
PLANO = [
    ('*FRANQUIA*', 'paciente::franquia#1'),
    ('', 'paciente::idclient-franquia#1'),
    ('', 'paciente::idschedule-franquia#1'),
    ('', 'paciente::idade#1'),
    ('', 'paciente::sexo#1'),
    ('', 'paciente::cidade#1'),
    ('', 'comercial::estado#1'),
    ('', 'paciente::endereco#1'),
    ('', 'paciente::origem-na-franquia#1'),
    ('', 'paciente::status-do-paciente#1'),

    ('*EM QUALIFICAÇÃO*', 'paciente::em-qualificacao#1'),
    ('', 'paciente::qualificacao-quente-morno-frio#1'),
    ('', 'paciente::tipo-de-lead#1'),
    ('', 'paciente::regiao-da-dor#1'),
    ('', 'paciente::queixa#1'),
    ('', 'paciente::observacoes#1'),

    ('*EM ESPERA*', 'paciente::em-espera#1'),
    ('', 'paciente::motivo-da-espera#1'),
    ('', 'paciente::retomar-em#1'),

    ('*AGENDADO*', 'paciente::agendado#1'),
    ('', 'comercial::agendou#1'),
    ('', 'paciente::data-da-consulta#1'),
    ('', 'paciente::agendado-pela-sdr-em#1'),
    ('', 'TIPO_AGENDAMENTO'),
    ('', 'paciente::responsavel-agendamento#1'),
    ('', 'IA_SCHEDULED_BY'),
    ('', 'paciente::valor-da-consulta#1'),
    ('', 'comercial::consulta-pg-antecipado#1'),
    ('', 'SPINE_SCHEDULE_CATEGORY'),
    ('', 'SPINE_SCHEDULE_STATUS'),
    ('', 'paciente::fisioterapeuta#1'),
    ('', 'paciente::compareceu#1'),

    ('*COMPARECEU*', 'paciente::compareceu#2'),
    ('', 'paciente::tratamento-indicado#1'),


    ('*EM NEGOCIAÇÃO*', 'paciente::em-negociacao#1'),
    ('', 'paciente::motivo-de-nao-fechamento-do-tratamento#1'),

    ('*GANHO / CONCLUÍDO*', 'paciente::ganho-concluido#1'),
    ('', 'paciente::fechou-tratamento#1'),
    ('', 'paciente::tratamento-fechado#1'),
    ('', 'paciente::valor-do-tratamento#1'),
    ('', 'ASAAS_BILLING_TYPE'),
    ('', 'TIPO_FECHAMENTO'),
    ('', 'paciente::semaforo#1'),

    ('*PERDIDO*', 'paciente::perdido#1'),
    ('', 'paciente::motivo-do-nao-agendamento#1'),

    ('*EM TRATAMENTO*', 'paciente::em-tratamento#1'),
    ('', 'paciente::idtreatment-franquia#1'),
    ('', 'paciente::proxima-sessao#1'),
    ('', 'paciente::ultima-sessao#1'),
    ('', 'paciente::sessoes-realizadas#1'),
    ('', 'paciente::sessoes-agendadas-por-vir#1'),
    ('', 'paciente::sessoes-no-historico#1'),
    ('', 'paciente::no-de-faltas#1'),
    ('', 'paciente::local-do-tratamento#1'),
    ('', 'paciente::grau#1'),
    ('', 'paciente::status-do-tratamento#1'),

    ('*AUTOMÁTICO · NÃO PREENCHER', 'paciente::automatico-nao-preencher#1'),
    ('', 'paciente::resposta-da-ia#1'),
    ('', 'paciente::pausar-ia#1'),
    ('', 'IA_CONV_STATUS'),
    ('', 'paciente::atendimento-assumido-por-humano#1'),
    ('', 'paciente::data-do-primeiro-contato#1'),
    ('', 'paciente::interacao#1'),
    ('', 'paciente::tipo-de-resgate#1'),
    ('', 'paciente::tentativas-de-resgate#1'),
    ('', 'paciente::no-de-tentativas-de-contato#1'),

    ('*MARKETING*', 'paciente::marketing#1'),
    ('', 'paciente::origem#1'),
    ('', 'paciente::campanha#1'),
    ('', 'paciente::anuncio-ad#1'),
    ('', 'paciente::titulo-do-anuncio#1'),
    ('', 'paciente::id-do-anuncio#1'),
    ('', 'paciente::plataforma-de-origem#1'),
    ('', 'paciente::url-de-origem-do-clique#1'),
    ('', 'paciente::ctwa-clid#1'),
]

# obrigatórios do dia 1: só os que NÃO travam cartão sem franquia.
OBRIGATORIOS_DIA1 = {
    'paciente::motivo-do-nao-agendamento#1': [{'pipeline': 'COMERCIAL', 'status': 'PERDIDO'}],
    'paciente::tratamento-indicado#1': [{'pipeline': 'COMERCIAL', 'status': 'COMPARECEU'}],
}

# enums que não se copiam da Açailândia (são da unidade / tinham lixo)
ENUMS_LIMPOS = {
    # tira o typo "Faxada" (duplicata de "Fachada"); mantém as (franquia) p/ o dia D
    'paciente::origem#1': lambda es: [e for e in es if e['value'] != 'Faxada'],
    # Açailândia tem DOIS "⬢ Forma de pagamento"; fica o enxuto de 5 opções
    'ASAAS_BILLING_TYPE': lambda es: [
        {'value': v, 'sort': i + 1}
        for i, v in enumerate(['À vista / PIX', 'Cartão de crédito', 'Cartão de débito', 'Boleto', 'Misto'])
    ],
    # nomes de fisio/SDR são da unidade: nasce só com o genérico
    'paciente::fisioterapeuta#1': lambda es: [{'value': 'A definir', 'sort': 1}],
    'paciente::responsavel-agendamento#1': lambda es: [
        {'value': v, 'sort': i + 1} for i, v in enumerate(['SDR', 'I.A SOFIA', 'DOUTOR DIGITAL'])
    ],
}

MOTIVOS_PERDA = [
    'Sem condições financeiras', 'Não interagiu', 'Informação para terceiro', 'Outra patologia',
    'Clicou por engano', 'Está viajando', 'Sem interesse', 'Não deu continuidade ao atendimento',
    'Vai se organizar', 'Plano de saúde', 'Outra cidade', 'Solicitado exames',
]

def main() -> int:
    src = json.loads(ORIGEM.read_text(encoding='utf-8'))
    por_chave = {}
    for f in src['fields']:
        por_chave.setdefault(f['key'], f)

    campos, faltando, sort, vistos = [], [], 10, {}
    for rotulo, chave in PLANO:
        f = por_chave.get(chave)
        if not f:
            faltando.append(chave)
            continue
        # A chave do blueprint TEM que ser a mesma que o export do DESTINO vai calcular.
        # Chave de `code` (TIPO_AGENDAMENTO…) e chave com prefixo de outro grupo
        # (comercial::estado#1) não casam: a Kommo não persiste o `code` no POST e o
        # campo cai no grupo paciente. Resultado medido na Salto em 26/10/2026:
        # 27 campos duplicados em 3 aplicações. Chave sempre paciente::<slug>#n.
        base = f"paciente::{slug(f['name'])}"
        vistos[base] = vistos.get(base, 0) + 1
        chave_destino = f'{base}#{vistos[base]}'
        novo = {'key': chave_destino, 'name': f['name'], 'type': f['type'], 'group': 'paciente', 'sort': sort}
        if f.get('is_api_only'):
            novo['is_api_only'] = True
        enums = f.get('enums')
        if chave in ENUMS_LIMPOS:
            enums = ENUMS_LIMPOS[chave](enums or [])
        if enums:
            novo['enums'] = enums
        if chave in OBRIGATORIOS_DIA1:
            novo['required_statuses'] = OBRIGATORIOS_DIA1[chave]
        campos.append(novo)
        sort += 10

    if faltando:
        print('ERRO: chaves ausentes no export:', faltando)
        return 1

    out = {
        'blueprint_version': 1,
        'exported_at': src['exported_at'],
        'source': {**src.get('source', {}), 'nota': 'v5 enxuto — seleção medida de doutorherniaacailandia, grupo único PACIENTE'},
        'pipelines': src['pipelines'],
        'field_groups': [{'key': 'paciente', 'name': 'PACIENTE', 'sort': 1}],
        'fields': campos,
        'loss_reasons': [{'name': n, 'sort': i + 1} for i, n in enumerate(MOTIVOS_PERDA)],
        'notes': [
            'Grupo único PACIENTE: o cartão tem uma aba só.',
            'Obrigatório só em PERDIDO (Motivo do não agendamento) e COMPARECEU (Tratamento indicado).',
            'Nenhum campo é escondido — logo nenhum escondido pode ser obrigatório.',
            'Os 12 campos utm_*/gclid/fbclid do grupo Statistics a Kommo cria sozinha.',
            'Nomes dos campos são contrato: o código resolve por nome (kommo-schema.campoPorNome).',
        ],
    }
    SAIDA.write_text(json.dumps(out, ensure_ascii=False, indent=1), encoding='utf-8')
    seps = [c for c in campos if c['name'].startswith('*') or c['name'].startswith('##')]
    print(f'campos={len(campos)} (separadores={len(seps)}, dados={len(campos)-len(seps)})')
    print(f'obrigatorios={len(OBRIGATORIOS_DIA1)} api_only={sum(1 for c in campos if c.get("is_api_only"))}')
    print(f'motivos_de_perda={len(out["loss_reasons"])}')
    print('escrito em', SAIDA)
    return 0

raise SystemExit(main())
