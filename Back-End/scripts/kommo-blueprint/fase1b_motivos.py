"""Aplica SÓ os motivos de perda do blueprint (aditivo, não apaga os existentes).

Mesmo bypass sancionado do fase1_campos.py: contas da lista PROTECTED de
apply_blueprint.py já existem em produção, mas continuam sendo destino legítimo
de replicação. Motivo de perda é aditivo — nunca apaga o que a conta já tem.

    python3 fase1b_motivos.py blueprint.itz-fresh.json <sub> token.txt \
        --apply --confirm=<sub> --lock=blueprint.<sub>.lock.json
"""

import json, sys
from pathlib import Path
from kommo import KommoClient
from apply_blueprint import Applier

blueprint, sub_in, token_file = sys.argv[1], sys.argv[2], sys.argv[3]
do_apply = "--apply" in sys.argv
confirm = next((a.split("=", 1)[1] for a in sys.argv if a.startswith("--confirm=")), None)
lock_out = next((a.split("=", 1)[1] for a in sys.argv if a.startswith("--lock=")), None)
sub = sub_in.replace(".kommo.com", "").strip("/ ")
if sub in ("attivacorpoementeitz",):
    print("BLOQUEADO: Imperatriz e a matriz, nunca destino."); sys.exit(2)
if do_apply and confirm != sub:
    print("SEGURANCA: para --apply precisa --confirm=%s" % sub); sys.exit(2)

bp = json.loads(Path(blueprint).read_text())
cli = KommoClient(sub, Path(token_file).read_text().strip(), read_only=not do_apply)
acc = cli.get("account")
print("destino: %s (id %s) — modo %s — SO MOTIVOS DE PERDA\n"
      % (acc["name"], acc["id"], "APLICAR" if do_apply else "DRY-RUN"))

app = Applier(cli, bp, apply=do_apply)
app.apply_loss_reasons()

if do_apply and lock_out:
    """  O lock da fase 1 já existe: mescla os motivos sem perder grupos/campos.  """
    path = Path(lock_out)
    lock = json.loads(path.read_text()) if path.exists() else app.lock
    lock["loss_reasons"] = app.lock["loss_reasons"]
    path.write_text(json.dumps(lock, ensure_ascii=False, indent=2) + "\n")
    print("\nlock atualizado em %s (motivos:%d)" % (lock_out, len(app.lock["loss_reasons"])))

if app.warnings:
    print("\nAVISOS:"); [print("  ! " + w) for w in app.warnings]
