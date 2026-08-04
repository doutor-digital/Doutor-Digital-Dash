#!/usr/bin/env python3
"""Gera o PDF de apresentação da régua de follow-up de Imperatriz.

Fonte da verdade: mensagens-rapidas.json + templates-waba-propostos.json + workflows-followup.yaml
Uso: python3 build_pdf.py  (precisa de google-chrome no PATH)
"""
import html
import json
import os
import re
import subprocess

BASE = os.path.dirname(os.path.abspath(__file__))
msgs = json.load(open(os.path.join(BASE, "mensagens-rapidas.json")))
waba = json.load(open(os.path.join(BASE, "templates-waba-propostos.json")))
yaml_txt = open(os.path.join(BASE, "workflows-followup.yaml")).read()

E = html.escape


def corpo_html(t):
    return E(t).replace("\n", "<br>")


ESTRUTURA = [
    ("COMERCIAL", "14091100", [
        ("108772996", "Etapa de leads de entrada", "—"),
        ("108773004", "EM QUALIFICAÇÃO", "⚑ Origem · ⬢ Tipo de lead"),
        ("108773008", "AGENDADO", "★ Qualificação · ☻ Responsável agendamento · ◷ Data da Consulta · ◷ Data de agendamento"),
        ("108773012", "COMPARECEU", "—"),
        ("108773016", "EM NEGOCIAÇÃO", "¤ Valor do tratamento · ⚕ Tratamento indicado"),
        ("142", "GANHO / CONCLUÍDO", "¤ Pagamento antecipado · ⚕ Fisioterapeuta · ⚕ Tratamento fechado · ¤ Valor da Consulta"),
        ("143", "PERDIDO", "—"),
    ]),
    ("TRATAMENTO", "14091116", [
        ("108773164", "Etapa de leads de entrada", "—"),
        ("108773168", "EM TRATAMENTO", "¤ Valor do tratamento · ¤ Valor da Consulta"),
        ("142", "ALTA", "—"),
        ("143", "TRATAMENTO CANCELADO", "—"),
    ]),
]

GRUPOS = [
    ("Cartão do lead (comercial)", 85, "origem, cidade, qualificação, queixa, datas, valores, tratamento indicado/fechado, métricas da IA"),
    ("Tratamento", 33, "sessões previstas/realizadas, faltas, alta, cancelamento, NPS, transferência entre clínicas"),
    ("ASAAS — Financeiro", 14, "cobrança, vencimento, status do pagamento, boleto/PIX, valor líquido"),
    ("Info agente (IA)", 3, "resposta da IA, pausar IA"),
    ("Telefonia 3C", 8, "última ligação, resultado, duração, gravação"),
    ("Tracking (UTM)", 12, "utm_*, gclid, fbclid, ttad_id"),
]

MATRIZ = [
    ("COMERCIAL", "Etapa de entrada", "Acolher e abrir a qualificação", "Marketing", "Nome do contato", "C01"),
    ("COMERCIAL", "EM QUALIFICAÇÃO", "Retomar quem parou de responder — Sofia dentro de 24h, template depois", "Marketing", "Nome do contato", "C02, C03, C08, C09"),
    ("COMERCIAL", "EM QUALIFICAÇÃO", "Coletar queixa, tempo de dor e exames", "Utility", "✎ Queixa, ★ Qualificação", "C04"),
    ("COMERCIAL", "EM QUALIFICAÇÃO", "Explicar o método e convidar para consulta", "Marketing", "—", "C05, C07"),
    ("COMERCIAL", "EM QUALIFICAÇÃO", "Recuperar ligação não atendida", "Utility", "☎ Última ligação, ☎ Resultado", "C06"),
    ("COMERCIAL", "AGENDADO", "Confirmar consulta e orientar preparo", "Utility", "◷ Data de agendamento, ⌂ Unidade", "C10, C13"),
    ("COMERCIAL", "AGENDADO", "Lembrar na véspera e no dia", "Utility", "◷ Data de agendamento", "C11, C12"),
    ("COMERCIAL", "AGENDADO", "Remarcar e recuperar falta", "Utility", "◷ Data do reagendamento, ⊘ Motivo do no-show", "C14, C15, C16"),
    ("COMERCIAL", "COMPARECEU", "Abrir espaço para dúvidas no dia seguinte", "Utility", "—", "C17"),
    ("COMERCIAL", "EM NEGOCIAÇÃO", "Registrar plano e condições por escrito", "Marketing", "⚕ Tratamento indicado, ¤ Valor do tratamento", "C18, C21"),
    ("COMERCIAL", "EM NEGOCIAÇÃO", "Tratar objeção de valor e de decisão compartilhada", "Marketing", "—", "C19, C20"),
    ("COMERCIAL", "EM NEGOCIAÇÃO", "Retomar em D+4 e encerrar em D+7 (degraus da Sofia fora da janela)", "Marketing", "—", "C22, C23"),
    ("COMERCIAL", "PERDIDO", "Reengajar 30 dias depois", "Marketing", "◷ Data da perda, motivo de perda", "C24"),
    ("COMERCIAL", "GANHO", "Acolher e informar a primeira sessão", "Utility", "◷ Próxima sessão, ⌂ Unidade", "C26, C27"),
    ("TRATAMENTO", "EM TRATAMENTO", "Lembrar sessão na véspera e no dia", "Utility", "◷ Próxima sessão", "T01, T02"),
    ("TRATAMENTO", "EM TRATAMENTO", "Recuperar falta e sustentar aderência", "Utility", "✓ Compareceu à última sessão, # Sessões realizadas", "T03, T04"),
    ("TRATAMENTO", "EM TRATAMENTO", "Cobrar exame ou documento pendente", "Utility", "◷ Data de retorno com exames", "T05"),
    ("TRATAMENTO", "EM TRATAMENTO", "Acolher cancelamento e reter", "Utility / Marketing", "◷ Data solicitação de cancelamento, ⊘ Motivo", "T06, T07"),
    ("TRATAMENTO", "CANCELADO", "Oferecer retorno clínico antes de encerrar", "Utility", "—", "T08"),
    ("TRATAMENTO", "ALTA", "Comunicar conclusão e medir satisfação", "Utility", "◷ Data de alta, ★ Satisfação / NPS", "R01, R02"),
    ("TRATAMENTO", "ALTA", "Pedir avaliação pública (só promotor) e retorno em 60 dias", "Marketing", "★ NPS, ✓ Deixou avaliação", "R03, R04"),
    ("TRATAMENTO", "EM TRATAMENTO", "Lembrar vencimento, cobrar atraso e confirmar pagamento", "Utility", "◷ Vencimento, ⬢ Status do pagamento, ↗ Boleto / PIX", "F01–F04"),
]

CAMPOS_NOVOS = [
    ("✓ Opt-out WhatsApp <em>(criado — 2444107)</em>", "select (Sim/Não)", "Feito",
     "O descadastro passa a ter registro no cartão. Toda régua consulta esse campo antes de disparar."),
    ("◷ Data do opt-out <em>(criado — 2444109)</em>", "date_time", "Feito", "Auditoria de quando o paciente pediu para sair."),
    ("⌂ Endereço da unidade", "text (ou constante no n8n)", "Alta",
     "Confirmação e lembrete de consulta precisam do endereço. Não existe campo — hoje seria digitado à mão."),
    ("◷ Data e hora da consulta", "date_time", "Alta",
     "Existem dois campos concorrentes e nenhum serve: ◷ Data da Consulta está vazio em 100% da amostra, e "
     "◷ Data de agendamento só tem valor nos 67 leads da migração, todos às 00:00 — sem hora. "
     "Ou se padroniza um dos dois com data e hora, ou nenhum lembrete de consulta pode ser automático."),
    ("◷ Última mensagem automática em", "date_time", "Média", "Trava de reenvio sem depender de leitura de notas."),
    ("# Mensagens marketing nos últimos 30d", "numeric", "Média", "Aplica o teto de 3 por 30 dias sem consultar histórico."),
    ("✎ Pendência do paciente", "text", "Baixa", "Hoje T05 usa ✎ Observações de consulta, que tem outra finalidade."),
    ("◷ Enviar mensagem em / ⬢ Mensagem a enviar / ◷ Mensagem enviada em", "date_time, select, date_time", "Média",
     "Trio que existe em Boa Vista e permite a SDR agendar uma mensagem pelo próprio cartão."),
]

ACHADOS = [
    ("◷ Data da Consulta está vazia em 100% da amostra",
     "Em 750 leads lidos, o campo não aparece nenhuma vez; ◷ Data de agendamento aparece em 8%. "
     "Toda a faixa de lembrete de consulta (véspera, dia, no-show) fica bloqueada até isso ser preenchido na rotina."),
    ("Não existe campo de opt-out",
     "Há template pedindo para o paciente responder SAIR, mas nada registra esse pedido. "
     "Na prática, quem pede para parar continua elegível para a próxima régua."),
    ("62 dos 76 templates WABA legados estão como MARKETING",
     "Vários são transacionais (confirmação de agendamento, remarcação, primeira sessão). "
     "Categoria errada custa mais caro e arrisca reprovação na Meta."),
    ("58 templates de MARKETING sem opt-out",
     "Só um template legado tem botão de parar mensagens. A Meta penaliza a qualidade do número quando falta saída."),
    ("Dois templates WABA usam a variável da linguagem errada",
     "Mensagem rápida e template do WhatsApp falam línguas diferentes: a primeira usa <code>[Nome de contato]</code> "
     "(52 das 55 da conta), o segundo usa <code>{{contact.name}}</code> (61 dos 76). "
     "E01_REFORCO_2H e E02_RET_DUVIDAS são templates do WhatsApp escritos na sintaxe da mensagem rápida — "
     "nesses dois, o texto vai para o paciente como está."),
    ("Uma duplicata real, não quatro",
     "E06_NOSHOW_MESMO_DIA, E07_POS_CONSULTA_D1 e DISPARO_VIDEO_FRANQUIA_DH são pares template + mensagem rápida "
     "de mesmo nome, o que é o padrão da casa — faltou apenas o sufixo <code>_MSG_RAPIDA</code>. "
     "Duplicata de verdade só há uma: E04_DADOS_PIX, dois templates de conteúdo idêntico, um como Marketing e outro como Utility."),
    ("Nada disso se corrige pela API",
     "O endpoint de templates só enxerga o que ele mesmo criou: <code>PATCH</code> em um template legado responde "
     "<code>EntityNotFound</code>. Os cinco pontos acima são trabalho na interface da Kommo e, no caso de categoria, "
     "reenvio à Meta."),
]

BOAS_PRATICAS = [
    ("Categoria", [
        "Utility é o que decorre de um compromisso marcado ou de um serviço já contratado: confirmação, lembrete, remarcação, sessão, cobrança, pesquisa pós-alta.",
        "Marketing é o que busca trazer alguém de volta ou apresentar oferta: primeiro contato, reengajamento por silêncio, win-back, convite para avaliação.",
        "Classificar marketing como utility para pagar menos é o erro que derruba template na revisão. Na dúvida, marketing.",
    ]),
    ("Variáveis", [
        "Nunca começar nem terminar o corpo com variável — reprovação automática.",
        "Nunca duas variáveis coladas: <code>{{1}} {{2}}</code> vira “conteúdo indefinido” para o revisor.",
        "Numerar em sequência a partir de {{1}}, sem pular número.",
        "Na submissão, preencher o exemplo de cada variável com um valor real e plausível.",
        "Mensagem rápida usa <code>[Nome de contato]</code>; template do WhatsApp usa <code>{{1}}</code>. As duas sintaxes não se misturam.",
    ]),
    ("Botões", [
        "No máximo três botões de resposta rápida, texto curto (até 20 caracteres) e sem emoji.",
        "O botão precisa ser resposta à pergunta do corpo: “Melhor / Igual / Pior” para uma pergunta sobre a dor, não “Saiba mais”.",
        "Todo template de marketing carrega um botão de saída (“Parar mensagens”), além do rodapé.",
        "Confirmação e lembrete usam o par “Confirmar presença” / “Preciso remarcar”, que alimenta o campo de comparecimento.",
    ]),
    ("Conteúdo, numa clínica", [
        "Sem promessa de resultado, sem a palavra cura, sem urgência inventada (“últimas vagas”).",
        "Sem diagnóstico, exame ou queixa no corpo da mensagem: WhatsApp não é prontuário, e um template aprovado fica registrado na Meta.",
        "Três a quatro linhas. O que não couber, a SDR fala na conversa.",
        "Valor e condição de pagamento só em template de marketing, nunca em utility.",
    ]),
    ("Versionamento", [
        "Nome em minúsculo, sem acento: <code>imperatriz_&lt;categoria&gt;_&lt;assunto&gt;_v&lt;n&gt;</code>.",
        "Template aprovado não se edita: mudou o texto, sobe <code>_v2</code> e o anterior é aposentado.",
        "Um idioma por template (pt_BR). Nada de misturar variantes na mesma entrada.",
        "Antes de liberar para a equipe, um envio supervisionado para um número da casa.",
    ]),
]

PENDENCIAS = [
    ("session_id da conta de Imperatriz",
     "Salesbot e automação de funil não existem na API pública da Kommo. São API privada, autenticada por cookie de sessão. "
     "Sem ele, os bots ficam desenhados mas não entram no ar."),
    ("Endereço da unidade e link de avaliação do Google",
     "São duas constantes. Com elas, C10, C11 e R03 deixam de precisar de digitação."),
    ("Validar a macro {{lead.cf.&lt;id&gt;}}",
     "As mensagens rápidas passaram a puxar campo do cartão automaticamente. "
     "Antes de liberar para a equipe, um envio supervisionado para um número da casa confirma que a Kommo substitui o valor. "
     "Se não substituir, o texto volta para colchetes com um PATCH."),
    ("Padronizar o preenchimento da data da consulta",
     "É a decisão operacional que destrava sete workflows de uma vez."),
]

CSS = """
@page { size: A4; margin: 18mm 15mm; }
* { box-sizing: border-box; }
body { font-family: "Helvetica Neue", Helvetica, Arial, sans-serif; font-size: 10pt; line-height: 1.5; color: #1a1a1a; }
h1 { font-size: 21pt; margin: 0 0 4px; letter-spacing: -0.4px; }
h2 { font-size: 13pt; margin: 26px 0 8px; padding-bottom: 5px; border-bottom: 1.5px solid #1a1a1a; page-break-after: avoid; }
h3 { font-size: 10.5pt; margin: 16px 0 6px; color: #333; page-break-after: avoid; }
p { margin: 0 0 8px; }
.sub { color: #666; font-size: 9pt; margin-bottom: 2px; }
.capa { border-bottom: 2px solid #1a1a1a; padding-bottom: 14px; margin-bottom: 6px; }
table { width: 100%; border-collapse: collapse; margin: 8px 0 14px; font-size: 8.5pt; }
th { text-align: left; background: #f2f2f2; border-bottom: 1px solid #ccc; padding: 5px 6px; font-weight: 600; }
td { border-bottom: 1px solid #e6e6e6; padding: 5px 6px; vertical-align: top; }
tr { page-break-inside: avoid; }
code { font-family: "SF Mono", Menlo, Consolas, monospace; font-size: 8pt; background: #f2f2f2; padding: 1px 4px; }
.msg { border: 1px solid #ddd; padding: 9px 11px; margin: 0 0 9px; page-break-inside: avoid; }
.msg .cab { display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 5px; }
.msg .cod { font-family: "SF Mono", Menlo, Consolas, monospace; font-size: 8pt; font-weight: 600; }
.msg .meta { font-size: 7.5pt; color: #777; }
.msg .corpo { background: #fafafa; border-left: 2px solid #bbb; padding: 7px 9px; font-size: 8.5pt; white-space: normal; }
.tag { display: inline-block; font-size: 7pt; padding: 1px 5px; border: 1px solid #999; text-transform: uppercase; letter-spacing: 0.4px; }
.tag.u { border-color: #2b6b3f; color: #2b6b3f; }
.tag.m { border-color: #8a5a1a; color: #8a5a1a; }
.tag.auto { border-color: #2b4f7a; color: #2b4f7a; }
.tag.sdr { border-color: #777; color: #777; }
.nota { border-left: 3px solid #1a1a1a; padding: 8px 11px; background: #f7f7f7; font-size: 9pt; margin: 10px 0; }
.quebra { page-break-before: always; }
ul { margin: 4px 0 10px; padding-left: 17px; }
li { margin-bottom: 3px; }
.kv { font-size: 8.5pt; color: #555; }
"""


def tabela(cabs, linhas):
    h = "<table><tr>" + "".join(f"<th>{c}</th>" for c in cabs) + "</tr>"
    for ln in linhas:
        h += "<tr>" + "".join(f"<td>{c}</td>" for c in ln) + "</tr>"
    return h + "</table>"


P = []
P.append(f"<style>{CSS}</style>")

# ── capa
P.append("""
<div class="capa">
  <div class="sub">Doutor Hérnia Imperatriz · conta Kommo attivacorpoementeitz (36459431)</div>
  <h1>Régua de follow-up no WhatsApp</h1>
  <div class="sub">Catálogo de mensagens, templates para aprovação e automações · 4 de agosto de 2026</div>
</div>
<p>Este documento parte da estrutura real da conta: dois funis, onze etapas e 155 campos
customizados, lidos direto da API da Kommo. A partir dela, define o que cada etapa precisa
comunicar, quais mensagens cobrem essa necessidade e qual automação dispara cada uma.</p>
<div class="nota"><strong>O que já está feito:</strong> as 43 mensagens rápidas descritas na
seção 4 foram criadas na conta e já aparecem para a equipe.
<strong>O que depende de aprovação:</strong> os 32 templates da seção 5, que precisam ir
para a Meta antes de qualquer envio fora da janela de 24 horas.</div>
""")

# ── 1 estrutura
P.append("<h2>1. A estrutura encontrada</h2>")
for nome, pid, etapas in ESTRUTURA:
    P.append(f"<h3>Funil {E(nome)} <span class='kv'>({pid})</span></h3>")
    P.append(tabela(["ID", "Etapa", "Campos obrigatórios para entrar"],
                    [(f"<code>{a}</code>", E(b), E(c)) for a, b, c in etapas]))
P.append("<p class='kv'>Existe ainda o funil arquivado <strong>NÂO USAR</strong> (13713915), fora de qualquer automação.</p>")
P.append("<h3>Os 155 campos, por grupo</h3>")
P.append(tabela(["Grupo", "Campos", "Do que trata"],
                [(E(a), str(b), E(c)) for a, b, c in GRUPOS]))

# ── 2 diagnostico
P.append("<h2>2. O que precisa ser resolvido antes</h2>")
P.append("<p>Seis pontos apareceram na leitura da conta. Os dois primeiros condicionam boa parte da régua.</p>")
for i, (t, d) in enumerate(ACHADOS, 1):
    P.append(f"<h3>{i}. {E(t)}</h3><p>{d}</p>")

# ── 3 matriz
P.append("<h2>3. O que cada etapa precisa comunicar</h2>")
P.append(tabela(["Funil", "Etapa", "Objetivo da comunicação", "Categoria", "Campos usados", "Mensagens"],
                [(E(a), E(b), E(c), E(d), E(e), f"<code>{E(f)}</code>") for a, b, c, d, e, f in MATRIZ]))

# ── 4 mensagens rapidas
P.append("<h2 class='quebra'>4. Mensagens rápidas criadas na Kommo</h2>")
P.append("""<p>São as mensagens que a equipe escolhe dentro da conversa. Onde existia campo no cartão,
a mensagem passou a puxar o valor sozinha — a SDR não digita nem consulta.
<span class="tag auto">auto</span> não pede nada; <span class="tag sdr">sdr</span> ainda tem um dado
que só quem está na conversa sabe (dois horários à escolha, por exemplo).</p>""")
ordem = {"C": 1, "T": 2, "R": 3, "F": 4}
titulo_bloco = {"C": "Funil COMERCIAL", "T": "Funil TRATAMENTO", "R": "Pós-alta e reputação", "F": "Financeiro (ASAAS)"}
atual = None
for m in sorted(msgs["mensagens"], key=lambda x: (ordem[x["codigo"][0]], x["codigo"])):
    bloco = m["codigo"][0]
    if bloco != atual:
        atual = bloco
        P.append(f"<h3>{titulo_bloco[bloco]}</h3>")
    modo = m["modo"].lower()
    macros = ""
    if m.get("macros_usadas"):
        macros = " · puxa automático: " + ", ".join(x["macro"] for x in m["macros_usadas"])
    manual = ""
    if m.get("preenchimento_manual"):
        manual = " · a SDR informa: " + ", ".join(m["preenchimento_manual"])
    P.append(f"""<div class="msg">
      <div class="cab">
        <span class="cod">{E(m['codigo'])} · {E(m['nome_kommo'])}</span>
        <span><span class="tag {modo}">{E(m['modo'])}</span> <span class="meta">id {m['kommo_template_id']}</span></span>
      </div>
      <div class="meta">{E(m['etapa'])} — {E(m['objetivo'])}{E(macros)}{E(manual)}</div>
      <div class="corpo">{corpo_html(m['corpo'])}</div>
    </div>""")

# ── 4-A boas praticas
P.append("<h2 class='quebra'>5. Como escrever um template que passa</h2>")
P.append("<p>As regras abaixo valem para todo template novo desta unidade. As três primeiras são "
         "motivo de reprovação automática na Meta; as demais são o que mantém a qualidade do número.</p>")
for titulo, itens in BOAS_PRATICAS:
    P.append(f"<h3>{E(titulo)}</h3><ul>" + "".join(f"<li>{i}</li>" for i in itens) + "</ul>")

# ── 5 templates waba
P.append("<h2 class='quebra'>6. Templates de WhatsApp para aprovação</h2>")
P.append(f"""<p>Estes {waba['total']} templates precisam ser cadastrados e aprovados pela Meta —
são os únicos que podem ser enviados fora da janela de 24 horas desde a última mensagem do paciente.
Foram separados por categoria conforme a política: <span class="tag u">utility</span> para o que é
serviço já contratado ou compromisso marcado, <span class="tag m">marketing</span> para reengajamento
e oferta. Nenhum começa ou termina com variável, e todo template de marketing traz a saída no rodapé.</p>""")
P.append(tabela(["Categoria", "Quantidade", "O que entra aqui"],
                [("<span class='tag u'>utility</span>", "23", "confirmação, lembrete, remarcação, sessão, pendência, financeiro, pesquisa de satisfação"),
                 ("<span class='tag m'>marketing</span>", "9", "primeiro contato, reengajamento por silêncio, win-back, avaliação no Google, retorno de 60 dias")]))
for t in waba["templates"]:
    cls = "u" if t["categoria_meta"] == "UTILITY" else "m"
    vs = "<br>".join(f"<code>{E(v['placeholder'])}</code> {E(v['campo_kommo'])}" for v in t["variaveis"])
    bts = ""
    if t.get("botoes"):
        bts = " · botões: " + ", ".join(f"“{b['texto']}”" for b in t["botoes"])
    fot = f"<br><em>Rodapé: {E(t['footer'])}</em>" if t.get("footer") else ""
    P.append(f"""<div class="msg">
      <div class="cab">
        <span class="cod">{E(t['id'])}</span>
        <span><span class="tag {cls}">{E(t['categoria_meta'])}</span> <span class="meta">pt_BR</span></span>
      </div>
      <div class="meta">{E(t['funil'])} · {E(t['etapa'])} — {E(t['descricao_uso'])}{E(bts)}</div>
      <div class="corpo">{corpo_html(t['corpo'])}{fot}</div>
      <div class="meta" style="margin-top:5px">{vs}</div>
    </div>""")

# ── 6-A IA + régua
P.append("<h2 class='quebra'>7. A régua e a Sofia: um cérebro só</h2>")
P.append("""<p>A Sofia já reengaja quem some. Existe uma escada em produção no <code>agente-dt</code> que
vai de 5 minutos a 20 horas, e cada degrau não é texto pronto: o modelo lê a conversa real e escreve
a mensagem a partir do que aquele paciente disse. Cinco mensagens fixas em sequência são reconhecíveis
como robô já na segunda.</p>
<p>O que limita essa escada não é estratégia, é o WhatsApp: <strong>texto livre só é entregue dentro de
24 horas desde a última fala do paciente</strong>. Por isso o último degrau da Sofia é em 20h — última
chance antes de a porta fechar. Depois disso, só template aprovado.</p>
<div class="nota"><strong>É exatamente aí que a régua entra.</strong> Ela não é um segundo motor de
follow-up: é o repertório de fora da janela. Quem continua decidindo quando e qual mensagem sai é o
worker da Sofia — o Kommo só entrega.</div>
<p>Essa regra não é preferência minha: está escrita no próprio <code>follow-up-worker.ts</code>. Dois
relógios mirando o mesmo lead disparam sem se ver, e mensagem duplicada no WhatsApp não tem desfazer.
Por isso <strong>sete automações de silêncio que eu tinha desenhado no Salesbot foram removidas</strong> —
elas seriam esse segundo relógio.</p>""")
P.append(tabela(["Quando", "Quem fala", "Como", "Custo"],
                [("Até 20h de silêncio", "Sofia (agente-dt)", "texto livre, escrito na hora a partir da conversa", "tokens"),
                 ("Depois de 24h", "Sofia decide, template entrega", "escolhe um dos templates ITZ_* aprovados", "por envio"),
                 ("Evento de serviço", "n8n", "vencimento, sessão, alta, NPS, no-show por data", "por envio"),
                 ("Entrada em etapa", "Salesbot", "só o transacional que não compete com conversa", "por envio")]))
P.append("""<h3>O que precisa ser acrescentado no agente-dt</h3>
<p>Quatro degraus novos em <code>follow-up-presets.ts</code>, apontando para template em vez de texto
livre. O comentário do arquivo já os previa como “possíveis quando a clínica aprovar templates na Meta”:</p>""")
P.append(tabela(["Degrau", "Etapa", "Template"],
                [("D+2", "EM QUALIFICAÇÃO", "<code>ITZ_C08_TERMOMETRO_72H_AUTO</code>"),
                 ("D+4", "COMPARECEU / EM NEGOCIAÇÃO", "<code>ITZ_C22_RETOMADA_NEGOCIACAO_D3_AUTO</code>"),
                 ("D+7", "COMPARECEU / EM NEGOCIAÇÃO", "<code>ITZ_C23_ENCERRAMENTO_D7_AUTO</code>"),
                 ("D+30", "PERDIDO, por motivo", "<code>ITZ_C24_CHECKIN_30D_PERDIDO_AUTO</code>")]))
P.append("""<p>E uma trava que já existe na Sofia e a régua passa a herdar: os <strong>dez motivos de perda
intocáveis</strong> — quem declarou não ter condições financeiras, bandeira vermelha clínica, sem interesse,
clicou por engano, mora em outra cidade. Insistir aí não é conversão, é dano, e bloqueio no WhatsApp
derruba a reputação do número inteiro.</p>""")

# ── 6-B dashboard
P.append("<h2>8. A Sofia no dashboard</h2>")
P.append("""<p>Hoje o dashboard mede a IA por dentro: conversas, mensagens, taxa de handoff, tokens.
São números de operação — respondem “a IA está rodando?”, não “a IA está trazendo paciente?”.
Como cada conversa já guarda o lead a que pertence, dá para cruzar com o funil e responder a segunda
pergunta. Abaixo o que eu recomendo medir, em ordem de valor.</p>""")
P.append(tabela(["Métrica", "O que responde", "De onde sai"],
                [("Agendamento após conversa com IA", "de cada 100 conversas, quantas viraram consulta marcada", "AgentConversations.LeadId → etapa AGENDADO"),
                 ("IA × humano no agendamento", "quanto do resultado é da IA e quanto é da equipe", "campo 2443031 ⬢ Agendamento feito por"),
                 ("Degrau que trouxe de volta", "qual passo da escada converte — permite cortar os que não servem", "follow-up-worker + etapa do lead"),
                 ("Dentro × fora da janela", "quantos toques foram grátis e quantos foram template pago, e a conversão de cada", "timestamp do toque vs última fala do paciente"),
                 ("Handoff por motivo", "onde a IA trava: pedido do lead, não soube, fora do horário", "campo 2443037 ⬢ Motivo do handoff"),
                 ("Resgate × cadastro", "separa quem a IA trouxe de volta de quem já ia agendar", "campo 2443059 ⬢ Tipo de agendamento"),
                 ("Tempo até 1ª resposta e até agendar", "a velocidade que justifica a IA existir", "campos 2443015 e 2443019"),
                 ("Custo por agendamento", "tokens + envios de template dividido por consulta marcada", "TokensIn/Out + contagem de disparos"),
                 ("Sentimento × desfecho", "se conversa que azedou perde mais, e em que etapa", "campos 2443039 e 2443041"),
                 ("Conversas paradas", "ativas há dias sem agendar nem handoff — fila que ninguém está olhando", "AgentConversations.Status + última mensagem")]))
P.append("""<div class="nota">As três primeiras linhas são as que mudam decisão de gestão. As demais
explicam o porquê quando o número cai. A implementação é um endpoint novo em <code>/api/agent</code>
cruzando conversa com etapa do lead — não depende de nada da Meta nem do session_id.</div>""")

# ── 7 workflows
P.append("<h2 class='quebra'>9. As automações</h2>")
P.append("""<p>Dois motores, escolhidos pelo tipo de gatilho. <strong>Salesbot</strong> cobre evento:
o lead entrou numa etapa, ou o paciente parou de responder há N horas. <strong>n8n</strong> cobre data e
condição: véspera da consulta, vencimento em três dias, NPS abaixo de 6. Essa divisão não é preferência —
o gatilho por data da Kommo não é configurável por API, então ele vira rotina no n8n.</p>""")

wf_rows = []
for bloco in re.split(r"\n  - id: ", yaml_txt.split("workflows:")[1]):
    if not bloco.strip():
        continue
    wid = bloco.split("\n")[0].strip()
    if not wid.startswith("itz_wf"):
        continue
    def campo(nome):
        m = re.search(rf"^\s*{nome}: (.+)$", bloco, re.M)
        return m.group(1).strip() if m else ""
    motor = campo("motor")
    desc = campo("descricao").strip('>').strip()
    gat = campo("gatilho")
    if not gat:  # gatilho em bloco de várias linhas
        m = re.search(r"^\s*gatilho:\s*\n((?:\s{6,}.+\n)+)", bloco, re.M)
        gat = " ".join(l.strip() for l in m.group(1).splitlines()) if m else ""
    tpl = re.search(r"template: (\w+)", bloco)
    status = "bloqueado" if "status: BLOQUEADO" in bloco else "pronto"
    g = re.sub(r"[{}]", "", gat)
    g = re.sub(r"\s*cron: \"[^\"]+\"", "", g).strip(" ,")
    wf_rows.append((f"<code>{E(wid)}</code>", E(motor), E(g[:110]),
                    f"<code>{E(tpl.group(1)) if tpl else '—'}</code>",
                    "<strong>bloqueado</strong>" if status == "bloqueado" else "pronto"))
P.append(tabela(["Workflow", "Motor", "Gatilho", "Mensagem", "Situação"], wf_rows))
P.append("""<div class="nota"><strong>Por que seis workflows estão bloqueados:</strong> todos dependem
da data e hora da consulta, que hoje não é preenchida. Não é limitação técnica — é dado que falta.
Assim que o agendamento passar a gravar esse campo, os seis entram sem mudança de código.</div>""")
P.append("<h3>Regras que valem para toda a régua</h3><ul>"
         "<li>Nenhum temporizador de silêncio vive no Kommo: quem decide o quando é a Sofia.</li>"
         "<li>Quem pediu para sair não recebe mais nada, de nenhuma régua.</li>"
         "<li>No máximo uma mensagem de marketing a cada 72 horas, e três em 30 dias, por paciente.</li>"
         "<li>Qualquer resposta do paciente zera a régua de silêncio em andamento.</li>"
         "<li>Envio só entre 8h e 20h.</li>"
         "<li>IA pausada ou conversa assumida por humano: nenhum reengajamento automático.</li>"
         "<li>Paciente com NPS 6 ou menos não recebe automação: vai para contato humano em 24 horas.</li>"
         "<li>Lead importado, sem conversa, nunca entra em régua de silêncio.</li>"
         "</ul>")

# ── 7 campos novos
P.append("<h2>10. Campos que precisam ser criados</h2>")
P.append(tabela(["Campo", "Tipo", "Prioridade", "Por quê"],
                [(f"<strong>{E(a)}</strong>", f"<code>{E(b)}</code>", E(c), E(d)) for a, b, c, d in CAMPOS_NOVOS]))

# ── 8 pendencias
P.append("<h2>11. O que falta para ligar</h2>")
for i, (t, d) in enumerate(PENDENCIAS, 1):
    P.append(f"<h3>{i}. {E(t)}</h3><p>{d}</p>")

html_path = os.path.join(BASE, "regua-followup-imperatriz.html")
pdf_path = os.path.join(BASE, "Regua_Followup_WhatsApp_Imperatriz.pdf")
open(html_path, "w").write(
    "<!doctype html><html lang='pt-BR'><head><meta charset='utf-8'>"
    "<title>Régua de follow-up — Doutor Hérnia Imperatriz</title></head><body>"
    + "\n".join(P) + "</body></html>")

subprocess.run(["google-chrome", "--headless", "--disable-gpu", "--no-sandbox",
                "--no-pdf-header-footer", f"--print-to-pdf={pdf_path}",
                "file://" + html_path], check=True,
               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
print("PDF:", pdf_path, os.path.getsize(pdf_path), "bytes")
