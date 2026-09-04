"""Mesmo token, mesma rota: sozinho x em paralelo.

Se o 401 so aparece sob concorrencia, o problema e limite de requisicao da Kommo
disfarcado de credencial invalida — e a correcao e ritmo, nao token novo.
"""
import json, subprocess, urllib.request, urllib.error
from concurrent.futures import ThreadPoolExecutor

UA = ("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/128.0 Safari/537.36")

def psql(sql):
    cid = subprocess.check_output(["docker", "ps", "-qf", "name=kommodb_db"]).decode().split()[0]
    envolto = "select coalesce(json_agg(t),'[]') from (" + sql.rstrip(";") + ") t;"
    return json.loads(subprocess.check_output(
        ["docker", "exec", cid, "psql", "-U", "kommo", "-d", "kommo_dashboard", "-At", "-c", envolto]).decode())

u = psql('select "KommoSubdomain" as sub, "KommoAccessToken" as tok from units where "Id"=23')[0]
fones = [l["fone"] for l in psql(
    'select "Telefone" as fone from franquia_lead_link where "UnitId"=23 '
    'and "Telefone" is not null limit 12')]

def busca(f):
    req = urllib.request.Request(
        f"https://{u['sub']}.kommo.com/api/v4/leads?query={f}&limit=10&with=contacts",
        headers={"Authorization": "Bearer " + u["tok"], "User-Agent": UA})
    try:
        with urllib.request.urlopen(req, timeout=45) as r:
            return str(r.status)
    except urllib.error.HTTPError as e:
        return str(e.code)
    except Exception as e:
        return type(e).__name__

def placar(res):
    d = {}
    for r in res:
        d[r] = d.get(r, 0) + 1
    return d

print("sequencial, sem pausa:", placar([busca(f) for f in fones]))
with ThreadPoolExecutor(max_workers=12) as ex:
    print("12 em paralelo:      ", placar(list(ex.map(busca, fones))))
with ThreadPoolExecutor(max_workers=12) as ex:
    print("12 em paralelo (2x): ", placar(list(ex.map(busca, fones))))
