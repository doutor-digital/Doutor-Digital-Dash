"""Distribuicao dos precos de tratamento por unidade, agosto/2026.

Serve para uma decisao: existe um piso abaixo do qual o valor e quase certamente
erro de digitacao? Chutar o piso sem olhar seria trocar um numero errado por outro.
"""
import json, subprocess, urllib.request, urllib.error

api = subprocess.check_output(["docker", "ps", "-qf", "name=ddapi_api"]).decode().split()[0]
env = subprocess.check_output(["docker", "exec", api, "printenv"]).decode()
KEY = next(l.split("=", 1)[1] for l in env.splitlines() if l.startswith("Admin__ApiKey="))

UNIDADES = [(14, "Açailândia"), (15, "Imperatriz"), (16, "Balsas"), (17, "Porto Nacional"),
            (18, "Canaã"), (20, "Parauapebas"), (23, "Marabá"), (24, "Serra"), (26, "Boa Vista"),
            (7, "Araguaína")]

todos = []
for uid, nome in UNIDADES:
    url = ("https://api-vps.doutordigitalconsultoria.com/internal/spine/tratamentos/diagnostico"
           f"?unitId={uid}&de=2026-08-01&ate=2026-08-31")
    try:
        req = urllib.request.Request(url, headers={"X-Admin-Key": KEY})
        with urllib.request.urlopen(req, timeout=600) as r:
            d = json.loads(r.read())
    except Exception as e:
        print(f"{nome}: {type(e).__name__}")
        continue
    precos = [t["price"] for t in (d.get("tratamentos") or []) if t.get("price")]
    baixos = sorted(p for p in precos if p < 1000)
    altos = sorted(p for p in precos if p >= 1000)
    todos += [(nome, p) for p in precos]
    print(f"{nome:<16} n={len(precos):<4} abaixo de 1.000: {len(baixos):<3} {baixos if baixos else ''}")
    if altos:
        print(f"{'':16} menores acima de 1.000: {altos[:5]}")

print("\n== todos os valores abaixo de R$ 1.000 na rede ==")
for nome, p in sorted([(n, p) for n, p in todos if p < 1000], key=lambda x: x[1]):
    print(f"  {nome:<16} R$ {p:,.2f}")
