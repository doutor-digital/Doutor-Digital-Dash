"""Verificação final: a conta destino bate 100% com o blueprint da matriz?

Compara o que o playbook manda comparar — funis/etapas (nome + ordem), campos
(por chave), obrigatórios por etapa (INCLUSIVE 142/143) e motivos de perda. Sai
com código 1 se sobrou divergência, para dar para encadear em verificação.

    python3 verificar.py --blueprint blueprint.itz-fresh.json \
        --subdomain <sub> --token-file token.txt
"""

import argparse
import json
import sys
import unicodedata
from pathlib import Path

from kommo import KommoClient
from apply_blueprint import slug


def norm(text: str) -> str:
    return (text or "").strip().casefold()


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--blueprint", required=True, type=Path)
    ap.add_argument("--subdomain", required=True)
    ap.add_argument("--token-file", required=True, type=Path)
    args = ap.parse_args()

    bp = json.loads(args.blueprint.read_text())
    sub = args.subdomain.replace(".kommo.com", "").strip("/ ")
    cli = KommoClient(sub, args.token_file.read_text().strip(), read_only=True, verbose=False)
    print(f"verificando {sub} contra {bp['source']['subdomain']} @ {bp['exported_at']}\n")

    falhas = []
    pls = {p["name"]: p for p in (cli.get("leads/pipelines").get("_embedded") or {}).get("pipelines") or []}

    print("[1] funis e etapas")
    for spec in bp["pipelines"]:
        cur = pls.get(spec["name"])
        if not cur:
            falhas.append(f"funil {spec['name']!r} não existe")
            print(f"  ! funil {spec['name']!r} AUSENTE")
            continue
        ordem = [s["name"] for s in sorted(cur["_embedded"]["statuses"], key=lambda s: s["sort"])
                 if s.get("type") != 1 and s["id"] not in (142, 143)]
        querida = [s["name"] for s in sorted(spec["statuses"], key=lambda s: s["sort"])]
        marca = "ok" if ordem == querida else "DIVERGE"
        if ordem != querida:
            falhas.append(f"etapas de {spec['name']}: {ordem} != {querida}")
        print(f"  {marca:<8} {spec['name']}: {ordem}")
        for sid, want in (spec.get("system_statuses") or {}).items():
            got = next((s["name"] for s in cur["_embedded"]["statuses"] if s["id"] == int(sid)), None)
            if norm(got) != norm(want):
                falhas.append(f"{spec['name']} > {sid}: {got!r} != {want!r} (renomear na UI)")
                print(f"  DIVERGE  {spec['name']} > {sid}: {got!r} deveria ser {want!r} — RENOMEAR NA TELA")

    print("\n[2] campos")
    grupos = {g["id"]: g["name"] for g in
              (cli.get("leads/custom_fields/groups").get("_embedded") or {}).get("custom_field_groups") or []}
    idx, seen = {}, {}
    campos = cli.get_all("leads/custom_fields", "custom_fields")
    for f in sorted(campos, key=lambda f: (f.get("sort") or 0, f["id"])):
        if f.get("is_predefined"):
            continue
        gname = grupos.get(f.get("group_id"))
        if not gname:
            continue
        if f.get("code"):
            idx[f["code"]] = f
            continue
        base = f"{slug(gname)}::{slug(f['name'])}"
        n = seen.get(base, 0) + 1
        seen[base] = n
        idx[f"{base}#{n}"] = f
    faltando = [s for s in bp["fields"] if s["key"] not in idx]
    print(f"  {len(bp['fields']) - len(faltando)}/{len(bp['fields'])} presentes")
    for s in faltando:
        falhas.append(f"campo ausente: {s['name']} [{s['type']}]")
        print(f"  ! ausente: {s['name']!r} [{s['type']}]")

    print("\n[3] obrigatórios por etapa")
    por_id = {(p["id"], s["id"]): (p["name"], s["name"])
              for p in pls.values() for s in p["_embedded"]["statuses"]}
    sysnames = {p["name"]: {int(k): v for k, v in (p.get("system_statuses") or {}).items()}
                for p in bp["pipelines"]}
    total, ok = 0, 0
    for spec in bp["fields"]:
        want = spec.get("required_statuses")
        if not want:
            continue
        cur = idx.get(spec["key"])
        if not cur:
            continue
        got = set()
        for r in cur.get("required_statuses") or []:
            ref = por_id.get((r["pipeline_id"], r["status_id"]))
            if not ref:
                continue
            nome = sysnames.get(ref[0], {}).get(r["status_id"], ref[1])
            got.add((norm(ref[0]), norm(nome)))
        querido = {(norm(r["pipeline"]), norm(r["status"])) for r in want}
        total += 1
        if got == querido:
            ok += 1
        else:
            falhas.append(f"obrigatório de {spec['name']}: {sorted(got)} != {sorted(querido)}")
            print(f"  ! {spec['name']!r}: tem {sorted(got)}, blueprint pede {sorted(querido)}")
    print(f"  {ok}/{total} campos com obrigatório idêntico")

    print("\n[4] motivos de perda")
    have = {norm(r["name"]) for r in cli.get_all("leads/loss_reasons", "loss_reasons")}
    faltam = [r["name"] for r in bp["loss_reasons"] if norm(r["name"]) not in have]
    print(f"  {len(bp['loss_reasons']) - len(faltam)}/{len(bp['loss_reasons'])} presentes")
    for n in faltam:
        falhas.append(f"motivo de perda ausente: {n}")
        print(f"  ! ausente: {n!r}")

    print("\n" + ("100% — nenhuma divergência." if not falhas else f"{len(falhas)} DIVERGÊNCIA(S):"))
    for f in falhas:
        print(f"  · {f}")
    return 1 if falhas else 0


if __name__ == "__main__":
    raise SystemExit(main())
