#!/usr/bin/env python3
"""Move todos os leads de um funil para outro, casando as etapas pelo NOME.

Preserva o motivo de perda: `loss_reason_id` só é aceito no MESMO PATCH que leva
`status_id=143` — em request separado a Kommo devolve 400 "only for lost lead".

Etapas de sistema (142 ganho / 143 perdido) casam por id, já que o nome delas
varia por funil e não é editável por API.

Uso:
    python3 move_pipeline.py --subdomain <conta> --token-file token.txt \\
        --from "COMERCIAL (antigo) - APAGAR" --to "COMERCIAL"        # dry-run
    ... --apply --confirm-subdomain <conta>
"""

from __future__ import annotations

import argparse
import json
import sys
from collections import Counter
from pathlib import Path

from apply_blueprint import PROTECTED, norm
from kommo import KommoClient

BATCH = 50


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--subdomain", required=True)
    ap.add_argument("--token-file", required=True, type=Path)
    ap.add_argument("--from", dest="src", required=True, help="nome do funil de origem")
    ap.add_argument("--to", dest="dst", required=True, help="nome do funil de destino")
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--confirm-subdomain")
    args = ap.parse_args()

    sub = args.subdomain.replace(".kommo.com", "").strip("/ ")
    if sub in PROTECTED:
        print(f"RECUSADO: {sub!r} é conta protegida.", file=sys.stderr)
        return 2
    if args.apply and args.confirm_subdomain != sub:
        print("RECUSADO: --apply exige --confirm-subdomain igual a --subdomain.", file=sys.stderr)
        return 2

    cli = KommoClient(sub, args.token_file.read_text().strip(), read_only=not args.apply)
    pipelines = cli.get("leads/pipelines")["_embedded"]["pipelines"]
    src = next((p for p in pipelines if norm(p["name"]) == norm(args.src)), None)
    dst = next((p for p in pipelines if norm(p["name"]) == norm(args.dst)), None)
    if not src or not dst:
        print("funil de origem ou destino não encontrado", file=sys.stderr)
        return 1

    src_name = {s["id"]: s["name"] for s in src["_embedded"]["statuses"]}
    dst_by_name = {norm(s["name"]): s["id"] for s in dst["_embedded"]["statuses"]}

    def target(status_id: int) -> int | None:
        if status_id in (142, 143):
            return status_id  # sistema: casa por id, o nome muda de funil para funil
        return dst_by_name.get(norm(src_name.get(status_id, "")))

    leads = [l for l in cli.get_all("leads", "leads") if l["pipeline_id"] == src["id"]]
    print(f"{src['name']!r} ({src['id']}) -> {dst['name']!r} ({dst['id']}): {len(leads)} leads")

    patches, unmapped = [], Counter()
    dist = Counter()
    for l in leads:
        tgt = target(l["status_id"])
        if tgt is None:
            unmapped[src_name.get(l["status_id"], l["status_id"])] += 1
            continue
        p = {"id": l["id"], "pipeline_id": dst["id"], "status_id": tgt}
        if tgt == 143 and l.get("loss_reason_id"):
            p["loss_reason_id"] = l["loss_reason_id"]
        patches.append(p)
        dist[f"{src_name.get(l['status_id'], l['status_id'])} -> {tgt}"] += 1

    for k, v in dist.most_common():
        print(f"  {v:6}  {k}")
    if unmapped:
        print("\nSEM ETAPA EQUIVALENTE NO DESTINO (ficam onde estão):")
        for k, v in unmapped.most_common():
            print(f"  {v:6}  {k}")

    if not args.apply:
        print(f"\nDRY-RUN — {len(patches)} leads seriam movidos.")
        return 0

    ok, bad = cli.patch_bisect("leads", patches, chunk=BATCH)
    print(f"\nmovidos: {len(ok)} | falhas: {len(bad)}")
    for item, err in bad[:10]:
        print(f"  x lead {item['id']}: {err[:200]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
