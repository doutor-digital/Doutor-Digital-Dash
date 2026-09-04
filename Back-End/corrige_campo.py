"""Corrige o fieldId do KPI de receita nas 3 unidades com ponteiro morto.

O id apontado nao existe na conta (a Kommo responde NotSupportedChoice). Efeito
colateral silencioso: o KPI lia o campo errado, entao 'ja preenchido' vinha
sempre falso — a gravacao teria duplicado valor se o 400 nao tivesse barrado.

So o fieldId muda; stageIds e porEntradaNaEtapa ficam como estao.
"""
import json, subprocess

CORRECOES = {18: ("Canaã", 2425058, 2446042),
             20: ("Parauapebas", 2425374, 2451106),
             23: ("Marabá", 2425206, 2446028)}

def psql(sql):
    cid = subprocess.check_output(["docker", "ps", "-qf", "name=kommodb_db"]).decode().split()[0]
    return subprocess.check_output(
        ["docker", "exec", cid, "psql", "-U", "kommo", "-d", "kommo_dashboard", "-At", "-c", sql]).decode().strip()

for uid, (nome, velho, novo) in CORRECOES.items():
    atual = psql(f'select "ConfigJson"::text from kpi_configurations '
                 f'where "UnitId"={uid} and "KpiKey"=\'receita\'')
    cfg = json.loads(atual)
    if cfg.get("fieldId") != velho:
        print(f"{nome}: esperava {velho}, achei {cfg.get('fieldId')} — NÃO mexi")
        continue
    cfg["fieldId"] = novo
    novo_json = json.dumps(cfg, ensure_ascii=False).replace("'", "''")
    psql(f'update kpi_configurations set "ConfigJson"=\'{novo_json}\' '
         f'where "UnitId"={uid} and "KpiKey"=\'receita\'')
    print(f"{nome}: {velho} → {novo}   {psql(chr(115) + 'elect \"ConfigJson\"::text from kpi_configurations where \"UnitId\"=' + str(uid) + ' and \"KpiKey\"=' + chr(39) + 'receita' + chr(39))}")
