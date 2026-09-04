"""Simula o preenchimento do valor do tratamento na Kommo, unidade por unidade.

aplicar=false: a rota so LISTA o que gravaria. Em serie e com pausa — o lote
anterior levou 401/429 porque duas execucoes se atropelaram na mesma conta.
"""
import json, subprocess, sys, time, urllib.request, urllib.error

DE, ATE = sys.argv[1], sys.argv[2]
APLICAR = len(sys.argv) > 3 and sys.argv[3] == "aplicar"
BASE = "https://api-vps.doutordigitalconsultoria.com/internal/spine/reconciliacao/preencher"

api = subprocess.check_output(["docker", "ps", "-qf", "name=ddapi_api"]).decode().split()[0]
env = subprocess.check_output(["docker", "exec", api, "printenv"]).decode()
KEY = next(l.split("=", 1)[1] for l in env.splitlines() if l.startswith("Admin__ApiKey="))

# Unidades que tem token da franquia E campo de valor mapeado no KPI de receita.
UNIDADES = [
    (14, "Açailândia"), (15, "Imperatriz"), (16, "Balsas"), (17, "Porto Nacional"),
    (18, "Canaã"), (20, "Parauapebas"), (23, "Marabá"), (24, "Serra"), (26, "Boa Vista"),
]

print(f"janela {DE} → {ATE} · modo {'GRAVANDO' if APLICAR else 'simulação'}\n")
resumo = []
for uid, nome in UNIDADES:
    url = f"{BASE}?unitId={uid}&de={DE}&ate={ATE}&aplicar={'true' if APLICAR else 'false'}"
    req = urllib.request.Request(url, method="POST", headers={"X-Admin-Key": KEY})
    try:
        with urllib.request.urlopen(req, timeout=1200) as r:
            d = json.loads(r.read())
    except urllib.error.HTTPError as e:
        corpo = e.read()[:160].decode(errors="replace")
        print(f"== {nome}: HTTP {e.code} — {corpo}")
        resumo.append((nome, None, None, None, f"HTTP {e.code}"))
        continue
    except Exception as e:
        print(f"== {nome}: {type(e).__name__} — {e}")
        resumo.append((nome, None, None, None, type(e).__name__))
        continue

    leads = d.get("leads") or []
    soma = sum(int(l["valor"]) for l in leads)
    print(f"== {nome}: {d.get('tratamentos')} tratamentos · "
          f"{d.get('jaPreenchidos')} já com valor · {d.get('semLeadNaKommo')} sem lead na Kommo · "
          f"{d.get('alterados')} a preencher (R$ {soma:,})".replace(",", "."))
    if d.get("erroKommo"):
        print(f"   ⚠ kommo: {d['erroKommo'][:110]}")
    for l in leads:
        print(f"   lead {l['leadId']:<12} {str(l['paciente'])[:34]:<36} {l['whatsapp']}  R$ {l['valor']}")
    resumo.append((nome, d.get("tratamentos"), d.get("alterados"), soma, d.get("erroKommo")))
    time.sleep(4)

print("\n" + "=" * 62)
print(f"{'unidade':<18}{'trat.':>7}{'a preencher':>13}{'R$':>12}")
for nome, t, a, s, err in resumo:
    if t is None:
        print(f"{nome:<18}{'—':>7}{'—':>13}{'—':>12}  {err}")
    else:
        print(f"{nome:<18}{t:>7}{a:>13}{s:>12,}".replace(",", "."))
