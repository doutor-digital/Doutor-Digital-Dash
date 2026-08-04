#!/usr/bin/env python3
"""Folha de aprovação dos templates de WhatsApp — versão para a direção.

Só os textos e quando cada um é enviado. Sem id, sem campo, sem workflow.
Uso: python3 build_aprovacao_pdf.py
"""
import html
import json
import os
import re
import subprocess

BASE = os.path.dirname(os.path.abspath(__file__))
waba = json.load(open(os.path.join(BASE, "templates-waba-propostos.json")))
E = html.escape

BLOCOS = [
    ("Primeiro contato e retomada", ["C01", "C08", "C05", "C09"],
     "Quem chega pelo anúncio e quem some no meio da conversa."),
    ("Consulta marcada", ["C28", "C29", "C10", "C11", "C12", "C13", "C15", "C16", "C17"],
     "Confirmação, lembrete, preparo, falta e o dia seguinte."),
    ("Decisão do tratamento", ["C22", "C23", "C24"],
     "Depois da consulta, enquanto o paciente decide."),
    ("Início do tratamento", ["C26", "C27"],
     "Quem fechou e vai começar."),
    ("Durante o tratamento", ["T01", "T02", "T03", "T04", "T05", "T06", "T07", "T08"],
     "Sessões, faltas, pendências e pedido de cancelamento."),
    ("Alta e acompanhamento", ["R01", "R02", "R03", "R04"],
     "Conclusão, satisfação e retorno."),
    ("Financeiro", ["F01", "F02", "F03", "F04"],
     "Vencimento, atraso e confirmação de pagamento."),
    ("Descadastro", ["C25"],
     "Quando o paciente pede para não receber mais."),
]

QUANDO = {
    "C01": "Assim que o lead chega, se ele ainda não escreveu",
    "C08": "2 dias sem resposta",
    "C05": "4 dias sem resposta",
    "C09": "5 dias sem resposta — último contato",
    "C28": "Três dias antes da consulta",
    "C29": "Quando o paciente aceita garantir o horário",
    "C10": "Ao marcar a consulta",
    "C11": "Um dia antes",
    "C12": "Na manhã do dia",
    "C13": "Uma hora depois de marcar",
    "C15": "No fim do dia, se não compareceu",
    "C16": "Dois dias depois da falta",
    "C17": "Um dia depois da consulta",
    "C22": "Quatro dias depois da consulta",
    "C23": "Sete dias depois — encerra o contato",
    "C24": "Trinta dias depois de perder o lead",
    "C25": "Quando o paciente responde SAIR",
    "C26": "Ao fechar o tratamento",
    "C27": "Ao agendar a primeira sessão",
    "T01": "Um dia antes da sessão",
    "T02": "Na manhã da sessão",
    "T03": "No fim do dia, se faltou",
    "T04": "Na metade das sessões previstas",
    "T05": "Quando falta exame ou documento",
    "T06": "Ao receber pedido de cancelamento",
    "T07": "No dia seguinte, se o motivo foi financeiro",
    "T08": "Um dia depois do cancelamento",
    "R01": "Ao dar alta",
    "R02": "Dois dias depois da alta",
    "R03": "Só para quem deu nota 9 ou 10",
    "R04": "Sessenta dias depois da alta",
    "F01": "Três dias antes do vencimento",
    "F02": "No dia do vencimento",
    "F03": "Dois dias depois de vencer",
    "F04": "Quando o pagamento é confirmado",
}

FALTANDO = [
    ("Oferta de pré-pagamento", "Marketing", "Três dias antes da consulta",
     "A consulta é R$ 350; garantindo no PIX fica R$ 150. Quem paga antes falta menos — "
     "por isso vale uma mensagem própria. <strong>Precisa da sua aprovação do valor.</strong>"),
    ("Dados do PIX", "Utility", "Quando o paciente aceita garantir o horário",
     "Chave e favorecido. <strong>Precisa da chave PIX e do nome do favorecido.</strong>"),
]

CSS = """
@page { size: A4; margin: 16mm 14mm; }
* { box-sizing: border-box; }
body { font-family: "Helvetica Neue", Helvetica, Arial, sans-serif; font-size: 10pt; line-height: 1.5; color: #1a1a1a; }
h1 { font-size: 22pt; margin: 0 0 4px; letter-spacing: -0.4px; }
h2 { font-size: 12.5pt; margin: 24px 0 3px; padding-bottom: 5px; border-bottom: 1.5px solid #1a1a1a; page-break-after: avoid; }
.sub { color: #666; font-size: 9pt; }
.capa { border-bottom: 2px solid #1a1a1a; padding-bottom: 14px; margin-bottom: 14px; }
p { margin: 0 0 8px; }
table { width: 100%; border-collapse: collapse; margin: 8px 0 16px; font-size: 9pt; }
th { text-align: left; background: #f2f2f2; border-bottom: 1px solid #ccc; padding: 5px 7px; font-weight: 600; }
td { border-bottom: 1px solid #e6e6e6; padding: 5px 7px; vertical-align: top; }
.card { border: 1px solid #ddd; padding: 10px 12px; margin: 0 0 10px; page-break-inside: avoid; }
.cab { display: flex; justify-content: space-between; align-items: baseline; gap: 10px; margin-bottom: 3px; }
.nome { font-weight: 600; font-size: 10pt; }
.quando { font-size: 8.5pt; color: #666; margin-bottom: 6px; }
.texto { background: #fafafa; border-left: 2px solid #bbb; padding: 8px 10px; font-size: 9.5pt; }
.var { background: #ececec; padding: 0 3px; border-radius: 2px; font-size: 9pt; }
.btns { margin-top: 6px; font-size: 8.5pt; color: #555; }
.btn { display: inline-block; border: 1px solid #bbb; padding: 1px 7px; margin-right: 5px; border-radius: 10px; }
.tag { font-size: 7.5pt; padding: 1px 6px; border: 1px solid #999; text-transform: uppercase; letter-spacing: 0.4px; white-space: nowrap; }
.tag.u { border-color: #2b6b3f; color: #2b6b3f; }
.tag.m { border-color: #8a5a1a; color: #8a5a1a; }
.nota { border-left: 3px solid #1a1a1a; padding: 9px 12px; background: #f7f7f7; font-size: 9.5pt; margin: 12px 0; }
.bloco-desc { font-size: 9pt; color: #666; margin: 0 0 9px; }
ul { margin: 4px 0 10px; padding-left: 17px; }
li { margin-bottom: 3px; }
"""


def marcar(txt):
    t = E(txt).replace("\n", "<br>")
    return re.sub(r"\{\{(\d+)\}\}", lambda m: f'<span class="var">{{{{{m.group(1)}}}}}</span>', t)


por_cod = {t["origem_mensagem_rapida"]["codigo"]: t for t in waba["templates"]}
P = [f"<style>{CSS}</style>"]

P.append("""
<div class="capa">
  <div class="sub">Doutor Hérnia Imperatriz</div>
  <h1>Mensagens de WhatsApp para aprovação</h1>
  <div class="sub">35 modelos · agosto de 2026</div>
</div>
<p>Estas são as mensagens que o WhatsApp exige aprovar antes de podermos enviar. A regra é da Meta,
não nossa: <strong>fora de 24 horas desde a última mensagem do paciente, só passa mensagem aprovada</strong>.
Dentro das 24 horas a equipe e a assistente escrevem livremente.</p>
<p>Cada modelo abaixo tem um texto fixo e alguns campos que mudam por paciente — marcados assim:
<span class="var">{{1}}</span>. Eles são preenchidos sozinhos com o nome, a data, o valor.</p>
""")

n_u = sum(1 for t in waba["templates"] if t["categoria_meta"] == "UTILITY")
n_m = len(waba["templates"]) - n_u
P.append("<table><tr><th>Tipo</th><th>Quantos</th><th>O que são</th><th>Custo</th></tr>"
         f"<tr><td><span class='tag u'>serviço</span></td><td>{n_u}</td>"
         "<td>Confirmação, lembrete, sessão, cobrança, pesquisa. Ligados a algo já marcado ou contratado.</td>"
         "<td>mais barato</td></tr>"
         f"<tr><td><span class='tag m'>divulgação</span></td><td>{n_m}</td>"
         "<td>Primeiro contato, retomada de quem sumiu, convite para avaliar, retorno.</td>"
         "<td>mais caro</td></tr></table>")

P.append("""<div class="nota">Todas as mensagens de divulgação trazem a saída no rodapé
(“responda SAIR”) e um botão para parar. Isso não é formalidade: sem saída, a Meta derruba a
qualidade do nosso número, e número com qualidade baixa deixa de entregar mensagem.</div>""")

for titulo, codigos, desc in BLOCOS:
    P.append(f"<h2>{E(titulo)}</h2>")
    P.append(f"<p class='bloco-desc'>{E(desc)}</p>")
    for cod in codigos:
        t = por_cod.get(cod)
        if not t:
            continue
        cls = "u" if t["categoria_meta"] == "UTILITY" else "m"
        rot = "serviço" if cls == "u" else "divulgação"
        btns = ""
        if t.get("botoes"):
            btns = "<div class='btns'>Botões: " + "".join(
                f"<span class='btn'>{E(b['texto'])}</span>" for b in t["botoes"]) + "</div>"
        fot = f"<br><br><em>{E(t['footer'])}</em>" if t.get("footer") else ""
        P.append(f"""<div class="card">
          <div class="cab"><span class="nome">{E(t['descricao_uso'])}</span>
            <span class="tag {cls}">{rot}</span></div>
          <div class="quando">{E(QUANDO.get(cod, ''))}</div>
          <div class="texto">{marcar(t['corpo'])}{fot}</div>
          {btns}
        </div>""")

P.append("<h2>O que acontece depois da aprovação</h2>")
P.append("<ul>"
         "<li>Os modelos vão para a Meta e a revisão costuma levar de algumas horas a um dia.</li>"
         "<li>Aprovado, o texto não pode mais ser editado — mudança vira modelo novo. "
         "Por isso vale ler com atenção agora.</li>"
         "<li>Nenhuma mensagem é disparada sem um teste antes, para um número da própria equipe.</li>"
         "<li>Os lembretes de consulta só passam a funcionar quando o horário da consulta for "
         "preenchido no cadastro — hoje esse campo está em branco.</li>"
         "</ul>")

html_path = os.path.join(BASE, "aprovacao-templates.html")
pdf_path = os.path.join(BASE, "Templates_WhatsApp_Para_Aprovacao_Imperatriz.pdf")
open(html_path, "w").write(
    "<!doctype html><html lang='pt-BR'><head><meta charset='utf-8'>"
    "<title>Mensagens de WhatsApp para aprovação — Doutor Hérnia Imperatriz</title></head><body>"
    + "\n".join(P) + "</body></html>")
subprocess.run(["google-chrome", "--headless", "--disable-gpu", "--no-sandbox",
                "--no-pdf-header-footer", f"--print-to-pdf={pdf_path}", "file://" + html_path],
               check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
print("PDF:", pdf_path, os.path.getsize(pdf_path), "bytes")
