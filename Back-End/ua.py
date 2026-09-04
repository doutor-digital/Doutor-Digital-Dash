"""Mesma chamada, mesmo token: com User-Agent x sem User-Agent."""
import json, subprocess, urllib.request, urllib.error

def psql(sql):
    cid = subprocess.check_output(["docker", "ps", "-qf", "name=kommodb_db"]).decode().split()[0]
    envolto = "select coalesce(json_agg(t),'[]') from (" + sql.rstrip(";") + ") t;"
    return json.loads(subprocess.check_output(
        ["docker", "exec", cid, "psql", "-U", "kommo", "-d", "kommo_dashboard", "-At", "-c", envolto]).decode())

u = psql('select "KommoSubdomain" as sub, "KommoAccessToken" as tok from units where "Id"=23')[0]
fones = [l["fone"] for l in psql(
    'select "Telefone" as fone from franquia_lead_link where "UnitId"=23 '
    'and "Telefone" is not null limit 10')]

class SemUA(urllib.request.OpenerDirector): pass

def busca(f, ua):
    h = {"Authorization": "Bearer " + u["tok"], "Accept": "application/json"}
    if ua: h["User-Agent"] = ua
    req = urllib.request.Request(
        f"https://{u['sub']}.kommo.com/api/v4/leads?query={f}&limit=10&with=contacts", headers=h)
    if not ua:
        # urllib poe um UA proprio se a gente nao mandar; remove de verdade.
        req.add_unredirected_header("User-Agent", "")
    try:
        with urllib.request.urlopen(req, timeout=45) as r:
            return str(r.status)
    except urllib.error.HTTPError as e:
        return str(e.code)
    except Exception as e:
        return type(e).__name__

def placar(res):
    d = {}
    for r in res: d[r] = d.get(r, 0) + 1
    return d

print("com UA de navegador:", placar([busca(f, "Mozilla/5.0") for f in fones]))
print("com UA do .NET:     ", placar([busca(f, "Mozilla/5.0 (compatible; DoutorDigital/1.0)") for f in fones]))
print("SEM User-Agent:     ", placar([busca(f, None) for f in fones]))
