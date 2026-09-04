"""O campo de valor apontado pelo KPI de receita existe na conta da unidade?

A gravacao em Canaa morreu com NotSupportedChoice: field_id invalido. Se o ponteiro
esta errado, o KPI de receita da unidade tambem esta somando um campo que nao existe
— erro silencioso, do tipo que ninguem percebe porque o card simplesmente mostra
menos dinheiro.
"""
import json, subprocess, urllib.request, urllib.error

UA = "Mozilla/5.0 (compatible; DoutorDigital/1.0)"

def psql(sql):
    cid = subprocess.check_output(["docker", "ps", "-qf", "name=kommodb_db"]).decode().split()[0]
    envolto = "select coalesce(json_agg(t),'[]') from (" + sql.rstrip(";") + ") t;"
    return json.loads(subprocess.check_output(
        ["docker", "exec", cid, "psql", "-U", "kommo", "-d", "kommo_dashboard", "-At", "-c", envolto]).decode())

def campos(sub, tok):
    todos, pagina = {}, 1
    while pagina <= 10:
        req = urllib.request.Request(
            f"https://{sub}.kommo.com/api/v4/leads/custom_fields?limit=250&page={pagina}",
            headers={"Authorization": "Bearer " + tok, "User-Agent": UA})
        try:
            with urllib.request.urlopen(req, timeout=60) as r:
                if r.status == 204: break
                d = json.loads(r.read())
        except urllib.error.HTTPError as e:
            return None, f"HTTP {e.code}"
        except Exception as e:
            return None, type(e).__name__
        itens = ((d.get("_embedded") or {}).get("custom_fields")) or []
        if not itens: break
        for c in itens:
            todos[c["id"]] = c.get("name") or ""
        pagina += 1
    return todos, None

linhas = psql('''
    select u."Id" as uid, u."Name" as nome, u."KommoSubdomain" as sub,
           u."KommoAccessToken" as tok, k."ConfigJson"::text as cfg
    from units u join kpi_configurations k on k."UnitId" = u."Id"
    where k."KpiKey" = 'receita' and k."SourceType" = 'custom_field_sum'
      and coalesce(u."KommoAccessToken",'') <> ''
    order by u."Name"
''')

for r in linhas:
    cfg = json.loads(r["cfg"])
    fid = cfg.get("fieldId")
    nome = r["nome"].replace("Doutor Hérnia ", "")
    todos, err = campos(r["sub"], r["tok"])
    if todos is None:
        print(f"{nome:<16} campo {fid:<9} → {err}")
        continue
    if fid in todos:
        print(f"{nome:<16} campo {fid:<9} OK  «{todos[fid]}»")
    else:
        cand = [(i, n) for i, n in todos.items()
                if "valor" in n.lower() and "trat" in n.lower()]
        alt = "  ".join(f"{i}=«{n}»" for i, n in cand) or "(nenhum campo com 'valor…tratamento')"
        print(f"{nome:<16} campo {fid:<9} NÃO EXISTE nesta conta → candidatos: {alt}")
