#!/usr/bin/env python3
"""Renomeia 142/143 pela rota legada /private/api/v2/json/pipelines/set (Bearer).

A UI diz que só ela renomeia essas etapas; a rota v2 aceita — descoberto em Mossoró,
repetido em Rio Verde. Regra da rota: statuses vai como OBJETO indexado por id e
precisa levar TODOS os statuses do funil (name+sort+color), senão ela zera o que faltar.
"""
import argparse, json, urllib.request

ap = argparse.ArgumentParser()
ap.add_argument("--subdomain", required=True)
ap.add_argument("--token-file", required=True)
ap.add_argument("--pipeline-id", type=int, required=True)
ap.add_argument("--set", action="append", required=True, metavar="ID=NOME")
ap.add_argument("--apply", action="store_true")
a = ap.parse_args()

tk = open(a.token_file).read().strip()
base = f"https://{a.subdomain}.kommo.com"
def req(url, data=None):
    r = urllib.request.Request(base + url, data=data,
        headers={"Authorization": "Bearer " + tk, "Content-Type": "application/json"})
    with urllib.request.urlopen(r) as x:
        return json.loads(x.read())

pipes = req("/api/v4/leads/pipelines")["_embedded"]["pipelines"]
pipe = next(p for p in pipes if p["id"] == a.pipeline_id)
alvo = dict(kv.split("=", 1) for kv in a.set)

statuses = {}
for s in pipe["_embedded"]["statuses"]:
    nome = alvo.get(str(s["id"]), s["name"])
    marca = " -> " + nome if str(s["id"]) in alvo else ""
    print(f"  {s['id']} {s['name']!r}{marca}")
    statuses[str(s["id"])] = {"id": s["id"], "name": nome, "sort": s["sort"], "color": s["color"]}

if not a.apply:
    print("DRY-RUN — nada escrito."); raise SystemExit

payload = {"request": {"pipelines": {"update": {str(pipe["id"]): {
    "id": pipe["id"], "name": pipe["name"], "sort": pipe["sort"],
    "is_main": "on" if pipe.get("is_main") else "off", "statuses": statuses}}}}}
req("/private/api/v2/json/pipelines/set", json.dumps(payload).encode())

vivo = {s["id"]: s["name"] for p in req("/api/v4/leads/pipelines")["_embedded"]["pipelines"]
        if p["id"] == a.pipeline_id for s in p["_embedded"]["statuses"]}
for sid, nome in alvo.items():
    ok = vivo.get(int(sid)) == nome
    print(("OK  " if ok else "FALHOU ") + f"{sid} agora {vivo.get(int(sid))!r}")
