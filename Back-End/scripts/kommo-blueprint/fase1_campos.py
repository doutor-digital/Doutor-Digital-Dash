import json, sys, collections
from pathlib import Path
from kommo import KommoClient
from apply_blueprint import Applier
blueprint, sub_in, token_file = sys.argv[1], sys.argv[2], sys.argv[3]
do_apply = '--apply' in sys.argv
confirm = next((a.split('=',1)[1] for a in sys.argv if a.startswith('--confirm=')), None)
lock_out = next((a.split('=',1)[1] for a in sys.argv if a.startswith('--lock=')), None)
sub = sub_in.replace('.kommo.com','').strip('/ ')
if sub in ('attivacorpoementeitz',):
    print('BLOQUEADO: Imperatriz e a matriz, nunca destino.'); sys.exit(2)
if do_apply and confirm != sub:
    print('SEGURANCA: para --apply precisa --confirm=%s' % sub); sys.exit(2)
bp = json.loads(Path(blueprint).read_text())
token = Path(token_file).read_text().strip()
cli = KommoClient(sub, token, read_only=not do_apply)
acc = cli.get('account')
print("destino: %s (id %s) — modo %s — SO CAMPOS (funis intactos)\n" % (acc['name'], acc['id'], 'APLICAR' if do_apply else 'DRY-RUN'))
app = Applier(cli, bp, apply=do_apply)
gids = app.apply_groups()
app.apply_fields(gids)
cnt = collections.Counter()
for l in app.plan:
    if '+ criar' in l: cnt['criar'] += 1
    elif '= já existe' in l: cnt['ja_existe'] += 1
    elif '~ ajustar' in l: cnt['ajustar'] += 1
print("\n=== RESUMO (grupos+campos) ===")
for k,v in cnt.items(): print("  %s: %d" % (k,v))
if do_apply and lock_out:
    Path(lock_out).write_text(json.dumps(app.lock, ensure_ascii=False, indent=2)+"\n")
    print("\nlock escrito em %s (grupos:%d campos:%d)" % (lock_out, len(app.lock['groups']), len(app.lock['fields'])))
if app.warnings:
    print("\nAVISOS:"); [print("  ! "+w) for w in app.warnings]
