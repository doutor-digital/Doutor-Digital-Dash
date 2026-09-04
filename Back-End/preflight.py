"""Antes de refazer o lote: nada rodando? quais unidades tem campo de valor mapeado?"""
import json, subprocess

def psql(sql):
    cid = subprocess.check_output(["docker", "ps", "-qf", "name=kommodb_db"]).decode().split()[0]
    envolto = "select coalesce(json_agg(t),'[]') from (" + sql.rstrip(";") + ") t;"
    return json.loads(subprocess.check_output(
        ["docker", "exec", cid, "psql", "-U", "kommo", "-d", "kommo_dashboard", "-At", "-c", envolto]).decode())

vivos = subprocess.run(["pgrep", "-af", "recon.sh"], capture_output=True, text=True).stdout.strip()
print("lote em andamento:", vivos or "nenhum")

linhas = psql('''
    select u."Id" as uid, u."Name" as nome,
           coalesce(u."KommoSubdomain",'') as sub,
           (u."KommoAccessToken" is not null and u."KommoAccessToken" <> '') as tem_kommo,
           (select k."ConfigJson" from kpi_configurations k
             where k."UnitId" = u."Id" and k."KpiKey" = 'receita' limit 1) as receita
    from units u where u."IsActive" order by u."Name"
''')

print(f"\n{'unidade':<18}{'kommo':<8}{'campo de valor (receita)'}")
print("-" * 62)
for r in linhas:
    cfg = r["receita"]
    campo = "—"
    if cfg:
        try:
            d = json.loads(cfg)
            campo = str(d.get("fieldId") or d.get("field_id") or d.get("metric") or "—")
        except Exception:
            campo = "(json inválido)"
    nome = r["nome"].replace("Doutor Hérnia ", "")
    print(f"{nome:<18}{('ok' if r['tem_kommo'] else 'sem'):<8}{campo}")
