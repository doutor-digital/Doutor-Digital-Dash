#!/usr/bin/env python3
"""Faz o grupo PACIENTE apontar para o MESMO id que o código escolhe.

Por que: `kommo-schema.campoPorNome` devolve `camposPorNome(nome)[0]` — o PRIMEIRO
campo com aquele nome na ordem da API (= menor id). Na Salto ficaram 10 nomes com
cópia (defeito das retentativas do apply_blueprint, 26/10/2026) e o grupo ficou com
a cópia de id MAIOR: o robô escreveria no órfão e a SDR veria o cartão vazio.

Não apaga nada. Só troca quais ids o grupo lista, na ordem do blueprint.

Uso (seco por padrão):
  python3 casar-grupo-com-o-codigo.py --blueprint bp.json --lock lock.json \
      --subdomain doutorherniasalto --cookie-file cookie.txt [--apply]
"""
import argparse, json, re, sys, urllib.error, urllib.parse, urllib.request
from pathlib import Path

ap = argparse.ArgumentParser()
ap.add_argument('--blueprint', required=True, type=Path)
ap.add_argument('--lock', required=True, type=Path)
ap.add_argument('--subdomain', required=True)
ap.add_argument('--cookie-file', required=True, type=Path)
ap.add_argument('--campos', required=True, type=Path, help='PSV id|nome|tipo|grupo|sort lido da API')
ap.add_argument('--apply', action='store_true')
a = ap.parse_args()

sub = a.subdomain.replace('.kommo.com', '').strip('/ ')
cookie = a.cookie_file.read_text().strip()
m = re.search(r'csrf_token=([^;]+)', cookie)
if not m:
    sys.exit('RECUSADO: cookie sem csrf_token')
csrf = m.group(1)

bp = json.loads(a.blueprint.read_text(encoding='utf-8'))
lock = json.loads(a.lock.read_text(encoding='utf-8'))
gid = lock['groups']['paciente']['id']

# menor id por nome = o que o código escolhe
menor: dict[str, int] = {}
for linha in a.campos.read_text(encoding='utf-8').splitlines():
    p = linha.split('|')
    if len(p) < 3:
        continue
    fid, nome = int(p[1]), p[2]
    if nome not in menor or fid < menor[nome]:
        menor[nome] = fid

ordem, ausentes = [], []
for f in bp['fields']:
    fid = menor.get(f['name'])
    if fid is None:
        ausentes.append(f['name'])
    else:
        ordem.append(fid)
if ausentes:
    sys.exit(f'RECUSADO: campo do blueprint não existe na conta: {ausentes}')
if len(ordem) != len(set(ordem)):
    sys.exit('RECUSADO: id repetido na ordem — dois campos do blueprint casaram no mesmo id')

print(f'grupo {gid}: {len(ordem)} campos na ordem do blueprint')
print(f'nomes com cópia na conta: {sum(1 for n in menor if n in {f["name"] for f in bp["fields"]})} conferidos')
if not a.apply:
    print('SECO — nada escrito. Rode com --apply.')
    raise SystemExit(0)

req = urllib.request.Request(
    f'https://{sub}.kommo.com/ajax/v4/leads/custom_fields/groups/{gid}',
    data=json.dumps({'fields': ordem}).encode(),
    headers={
        'Cookie': cookie,
        'Content-Type': 'application/json',
        'X-Requested-With': 'XMLHttpRequest',
        'X-CSRF-Token': csrf,
    },
    method='PATCH',
)
try:
    resp = urllib.request.urlopen(req).read()
    print('PATCH ok:', resp[:200])
except urllib.error.HTTPError as e:
    sys.exit(f'PATCH falhou {e.code}: {e.read()[:300]}')
