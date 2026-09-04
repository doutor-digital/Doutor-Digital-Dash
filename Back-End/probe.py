"""O 401 da Kommo e intermitente ou constante? Mede antes de chutar."""
import json, subprocess, time, urllib.request, urllib.error

UA = ("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/128.0 Safari/537.36")

def psql(servico, usuario, banco, sql):
    cid = subprocess.check_output(["docker", "ps", "-qf", f"name={servico}"]).decode().split()[0]
    envolto = "select coalesce(json_agg(t),'[]') from (" + sql.rstrip(";") + ") t;"
    return json.loads(subprocess.check_output(
        ["docker", "exec", cid, "psql", "-U", usuario, "-d", banco, "-At", "-c", envolto]).decode())

SQL_UNIDADE = 'select "KommoSubdomain" as sub, "KommoAccessToken" as tok from units where "Id"=23'
SQL_FONES = ('select "Telefone" as fone from franquia_lead_link '
             'where "UnitId"=23 and "Telefone" is not null limit 25')

u = psql("kommodb_db", "kommo", "kommo_dashboard", SQL_UNIDADE)[0]
fones = [l["fone"] for l in psql("kommodb_db", "kommo", "kommo_dashboard", SQL_FONES)]
print(f"{len(fones)} telefones de Marabá para testar\n")

placar = {}
for i, f in enumerate(fones, 1):
    req = urllib.request.Request(
        f"https://{u['sub']}.kommo.com/api/v4/leads?query={f}&limit=10&with=contacts",
        headers={"Authorization": "Bearer " + u["tok"], "User-Agent": UA})
    t0 = time.time()
    try:
        with urllib.request.urlopen(req, timeout=45) as r:
            st = str(r.status)
    except urllib.error.HTTPError as e:
        st = str(e.code)
    except Exception as e:
        st = type(e).__name__
    placar[st] = placar.get(st, 0) + 1
    print(f"  {i:>2}. …{f[-8:]}  {st}  ({time.time() - t0:.2f}s)")

print("\nplacar:", placar)
