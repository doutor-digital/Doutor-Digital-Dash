"""O token que levou 401 e o mesmo que esta no banco agora?

Se a digital nao bate, alguem trocou o token entre a execucao e agora — e o 401
foi credencial velha, nao permissao faltando.
"""
import json, re, subprocess

def psql(sql):
    cid = subprocess.check_output(["docker", "ps", "-qf", "name=kommodb_db"]).decode().split()[0]
    envolto = "select coalesce(json_agg(t),'[]') from (" + sql.rstrip(";") + ") t;"
    return json.loads(subprocess.check_output(
        ["docker", "exec", cid, "psql", "-U", "kommo", "-d", "kommo_dashboard", "-At", "-c", envolto]).decode())

banco = {r["sub"]: r for r in psql(
    'select "KommoSubdomain" as sub, "Name" as nome, length("KommoAccessToken") as len, '
    'left("KommoAccessToken",6) as ini, right("KommoAccessToken",6) as fim '
    'from units where coalesce("KommoAccessToken",\'\') <> \'\'')}

padrao = re.compile(r"https://([a-z0-9]+)\.kommo\.com.*?token len=(\d+) (\S+)…(\S+?):")
vistos = {}
for linha in open("/tmp/401.txt"):
    if "query=" not in linha:
        continue
    m = padrao.search(linha)
    if m:
        vistos[m.group(1)] = (int(m.group(2)), m.group(3), m.group(4))

print(f"{'subdominio':<26}{'no 401':<24}{'no banco agora':<24}{'veredito'}")
print("-" * 92)
for sub, (ln, ini, fim) in sorted(vistos.items()):
    b = banco.get(sub)
    atual = f"{b['len']} {b['ini']}…{b['fim']}" if b else "(sem token)"
    igual = b and b["len"] == ln and b["ini"] == ini and b["fim"] == fim
    print(f"{sub:<26}{f'{ln} {ini}…{fim}':<24}{atual:<24}"
          f"{'MESMO token' if igual else 'TROCADO depois'}")
