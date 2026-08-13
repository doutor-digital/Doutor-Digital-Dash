#!/usr/bin/env python3
"""Cria o grupo e os campos da 3C nas unidades que ainda não têm.

RITMO: a Kommo permite 7 req/s POR IP e bloqueia a conta inteira com 403 se o
429 se repetir. Como as 12 unidades saem do mesmo IP, elas dividem o orçamento —
por isso 4 req/s e pausa entre contas. Um bloqueio derrubaria junto o rastreio e
o sync, que também vivem nessa VPS.

IDEMPOTENTE: confere o que já existe antes de criar. Rodar duas vezes não
duplica campo.
"""
import json, subprocess, sys, time

PAUSA = 0.25          # 4 req/s
PAUSA_UNIDADE = 2.0

GRUPO = "☎ 3C — LIGAÇÕES"
CAMPOS = [
    ("*INFORMAÇÕES*",            "text",      None),
    ("☎ Última ligação",         "date_time", None),
    ("☎ Resultado",              "select",    ["Atendida","Não atendida","Caixa postal","Falha","Ocupado","Número inválido"]),
    ("☎ Duração da conversa",    "text",      None),
    ("☎ Motivo (qualificação)",  "text",      None),
    ("☎ Agente",                 "text",      None),
    ("☎ Campanha 3C",            "text",      None),
    ("☎ Tentativas",             "numeric",   None),
    ("☎ Gravação",               "url",       None),
]

def sql(q):
    return subprocess.run(
        ["ssh","-i","/home/joaoof/.ssh/doutordigital_vps","-o","StrictHostKeyChecking=no",
         "root@89.116.214.130",
         "docker exec $(docker ps -qf name=kommodb_db|head -1) psql -U kommo -d kommo_dashboard -At -F'|' -c \"%s\"" % q],
        capture_output=True, text=True).stdout.strip()

def api(metodo, url, token, corpo=None):
    args = ["curl","-s","-X",metodo,url,"-H","Authorization: Bearer "+token,
            "-H","Content-Type: application/json","-w","\n%{http_code}"]
    if corpo is not None:
        args += ["-d", json.dumps(corpo, ensure_ascii=False)]
    r = subprocess.run(args, capture_output=True, text=True).stdout
    partes = r.rsplit("\n",1)
    time.sleep(PAUSA)
    try: return json.loads(partes[0]) if partes[0].strip() else {}, partes[-1]
    except Exception: return {"_raw": partes[0][:200]}, partes[-1]

linhas = sql("select \\\"Id\\\", \\\"Name\\\", \\\"KommoSubdomain\\\", \\\"KommoAccessToken\\\" from units where \\\"KommoAccessToken\\\" is not null and \\\"KommoSubdomain\\\" is not null order by \\\"Id\\\";")

total_criados = 0
for linha in linhas.split("\n"):
    if not linha.strip(): continue
    uid, nome, sub, token = linha.split("|", 3)
    sub = sub.replace(".kommo.com", "").strip("/ ")
    base = f"https://{sub}.kommo.com/api/v4/leads/custom_fields"

    atuais, code = api("GET", base+"?limit=250&page=1", token)
    if code != "200":
        print(f"  {nome:<30} PULADA — API respondeu {code}"); continue
    existentes = {c["name"].strip() for c in atuais.get("_embedded",{}).get("custom_fields",[])}

    faltando = [(n,t,e) for (n,t,e) in CAMPOS if n not in existentes]
    if not faltando:
        print(f"  {nome:<30} já tem os {len(CAMPOS)} campos"); time.sleep(PAUSA_UNIDADE); continue

    # Grupo próprio, para os campos não caírem soltos no cartão.
    gid = None
    grupos, _ = api("GET", f"https://{sub}.kommo.com/api/v4/leads/custom_fields/groups", token)
    for g in grupos.get("_embedded",{}).get("custom_field_groups",[]):
        if g.get("name","").strip() == GRUPO: gid = g["id"]; break
    if not gid:
        r, c = api("POST", f"https://{sub}.kommo.com/api/v4/leads/custom_fields/groups", token,
                   [{"name": GRUPO, "sort": 900}])
        gs = r.get("_embedded",{}).get("custom_field_groups",[])
        gid = gs[0]["id"] if gs else None

    corpo = []
    for i,(n,t,enums) in enumerate(faltando):
        c = {"name": n, "type": t, "sort": 900+i}
        if gid: c["group_id"] = gid
        if enums: c["enums"] = [{"value": v, "sort": k+1} for k,v in enumerate(enums)]
        corpo.append(c)

    r, code = api("POST", base, token, corpo)
    if code == "200":
        criados = len(r.get("_embedded",{}).get("custom_fields",[]))
        total_criados += criados
        print(f"  {nome:<30} +{criados} campos criados")
    else:
        print(f"  {nome:<30} FALHOU {code}: {json.dumps(r,ensure_ascii=False)[:150]}")
    time.sleep(PAUSA_UNIDADE)

print(f"\ntotal de campos criados: {total_criados}")
