#!/usr/bin/env python3
"""Deriva o catálogo de templates WABA a partir das mensagens rápidas já criadas na Kommo.

Regras aplicadas: nenhum corpo começa ou termina com variável; MARKETING sempre com opt-out.
"""
import json, re, os
BASE = os.path.dirname(os.path.abspath(__file__))
cat = json.load(open(os.path.join(BASE, "mensagens-rapidas.json")))
by = {m["codigo"]: m for m in cat["mensagens"]}
CAMPOS = {"2440909": "◷ Data de agendamento (data e hora da consulta)", "2440965": "◷ Próxima sessão",
          "2442765": "⌂ Unidade", "2440847": "⚕ Tratamento indicado", "2440829": "¤ Valor do tratamento",
          "2442715": "⬢ Forma de pagamento", "2442717": "# Nº de parcelas", "2442963": "↗ Boleto / PIX",
          "2442965": "✎ Descrição", "2443057": "✎ Observações de consulta", "2442967": "¤ Valor da cobrança",
          "2442955": "◷ Vencimento"}
PROP = {"C01": ("MARKETING", "Primeiro contato com lead vindo de anúncio ou formulário."),
        "C05": ("MARKETING", "Explicação do método e convite para consulta."),
        "C08": ("MARKETING", "Reengajamento de quem parou de responder."), "C09": ("MARKETING", "Encerramento da régua comercial após 120h."),
        "C28": ("MARKETING", "Oferta de pré-pagamento da consulta."),
        "C29": ("UTILITY", "Dados do PIX para garantir o horário."),
        "C10": ("UTILITY", "Confirmação de consulta agendada."), "C11": ("UTILITY", "Lembrete de consulta na véspera."),
        "C12": ("UTILITY", "Confirmação de consulta no dia."), "C13": ("UTILITY", "Orientação de preparo para a consulta."),
        "C15": ("UTILITY", "Recuperação de falta na consulta, no mesmo dia."), "C16": ("UTILITY", "Segunda tentativa de remarcação."),
        "C17": ("UTILITY", "Acompanhamento no dia seguinte à consulta."), "C22": ("MARKETING", "Retomada de decisão 3 dias após a consulta."),
        "C23": ("MARKETING", "Encerramento respeitoso da negociação."), "C24": ("MARKETING", "Reengajamento 30 dias após a perda."),
        "C25": ("UTILITY", "Confirmação de descadastro."), "C26": ("UTILITY", "Boas-vindas ao tratamento contratado."),
        "C27": ("UTILITY", "Informação da primeira sessão."), "T01": ("UTILITY", "Lembrete de sessão na véspera."),
        "T02": ("UTILITY", "Confirmação de sessão no dia."), "T03": ("UTILITY", "Falta em sessão de tratamento contratado."),
        "T04": ("UTILITY", "Acompanhamento de aderência no meio do plano."), "T05": ("UTILITY", "Pendência de exame ou documento."),
        "T06": ("UTILITY", "Confirmação de pedido de cancelamento."), "T07": ("MARKETING", "Alternativa financeira para retenção."),
        "T08": ("UTILITY", "Retorno clínico antes do encerramento."), "R01": ("UTILITY", "Comunicado de conclusão do tratamento."),
        "R02": ("UTILITY", "Pesquisa de satisfação pós-alta."), "R03": ("MARKETING", "Convite para avaliação pública no Google."),
        "R04": ("MARKETING", "Reengajamento 60 dias após a alta."), "F01": ("UTILITY", "Lembrete de vencimento 3 dias antes."),
        "F02": ("UTILITY", "Lembrete de vencimento no dia."), "F03": ("UTILITY", "Aviso de parcela em aberto."),
        "F04": ("UTILITY", "Confirmação de pagamento recebido.")}
MANUAL = {"endereço da unidade": "constante da unidade (a definir)", "data 1": "escolha da SDR na hora",
          "data 2": "escolha da SDR na hora", "valor da parcela": "calculado (n8n)",
          "alternativa": "escolha da SDR", "link do Google": "constante da unidade (a definir)"}
out, probs = [], []
for cod, (mcat, desc) in PROP.items():
    m = by[cod]; corpo = m["corpo"]; variaveis = []; n = 1
    if "{{contact.name}}" in corpo:
        corpo = corpo.replace("{{contact.name}}", "{{1}}")
        variaveis.append({"placeholder": "{{1}}", "campo_kommo": "Nome do contato"}); n = 2
    for fid in dict.fromkeys(re.findall(r"{{lead\.cf\.(\d+)}}", corpo)):
        corpo = corpo.replace("{{lead.cf.%s}}" % fid, "{{%d}}" % n)
        variaveis.append({"placeholder": "{{%d}}" % n, "campo_kommo": f"{fid} {CAMPOS.get(fid, '?')}"}); n += 1
    for ph in dict.fromkeys(re.findall(r"\[([^\]]+)\]", corpo)):
        corpo = corpo.replace(f"[{ph}]", "{{%d}}" % n)
        variaveis.append({"placeholder": "{{%d}}" % n, "campo_kommo": MANUAL.get(ph, ph)}); n += 1
    if re.match(r"^\s*{{", corpo): probs.append((cod, "começa com variável"))
    if re.search(r"}}\s*$", corpo): probs.append((cod, "termina com variável"))
    slug = re.sub(r"_(AUTO|SDR)$", "", re.sub(r"^ITZ_[A-Z0-9]+_", "", m["nome_kommo"])).lower()
    it = {"id": f"imperatriz_{mcat.lower()}_{slug}_v1", "categoria_meta": mcat, "idioma": "pt_BR",
          "descricao_uso": desc, "funil": m["funil"], "etapa": m["etapa"],
          "origem_mensagem_rapida": {"codigo": cod, "nome_kommo": m["nome_kommo"], "kommo_id": m["kommo_template_id"]},
          "variaveis": variaveis, "corpo": corpo}
    botoes = [{"tipo": "quick_reply", "texto": b["text"]} for b in m.get("botoes", [])]
    if mcat == "MARKETING":
        it["footer"] = "Para não receber mais mensagens deste tipo, responda SAIR."
        if len(botoes) < 3 and not any(p in b["texto"].lower() for b in botoes for p in ("parar", "pausar")):
            botoes.append({"tipo": "quick_reply", "texto": "Parar mensagens"})
    if botoes:
        it["botoes"] = botoes
    out.append(it)
json.dump({"conta": "attivacorpoementeitz", "total": len(out),
           "regras_aplicadas": ["nenhum corpo começa ou termina com variável", "MARKETING sempre com opt-out no rodapé"],
           "templates": out}, open(os.path.join(BASE, "templates-waba-propostos.json"), "w"), ensure_ascii=False, indent=2)
print("propostos:", len(out), "| UTILITY", sum(1 for t in out if t["categoria_meta"] == "UTILITY"),
      "| MARKETING", sum(1 for t in out if t["categoria_meta"] == "MARKETING"))
print("violações:", probs or "nenhuma")
