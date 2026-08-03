#!/usr/bin/env python3
"""Exporta a ESTRUTURA de uma conta Kommo para um blueprint JSON versionável.

Somente leitura — o cliente é instanciado com read_only=True, então qualquer
POST/PATCH/DELETE acidental levanta exceção antes de sair da máquina.

Exporta: pipelines + etapas, grupos de campos, custom fields de lead (com enums
e required_statuses) e motivos de perda. NÃO exporta dados de lead, contatos ou
tags (na conta de origem as tags são lixo de importação em massa).

A identidade de cada campo no blueprint é a chave `key`:
  * o `code` do campo, quando existe (ASAAS_*, IA_*…);
  * senão `<grupo>::<nome-slug>#<n>` — determinístico, e o `#n` desempata os
    campos separadores de layout, que repetem nome ("**!", "##!").

Uso:
    python3 export_account.py --subdomain attivacorpoementeitz \
        --token-file /caminho/token.txt --out blueprint.clinica-v1.json
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import unicodedata
from datetime import datetime, timezone
from pathlib import Path

from kommo import KommoClient

# Grupos que a Kommo cria sozinha e não aceita campo novo por API.
SYSTEM_GROUPS = {"statistic", "default"}


def slug(text: str) -> str:
    norm = unicodedata.normalize("NFKD", text or "")
    norm = "".join(c for c in norm if not unicodedata.combining(c))
    norm = re.sub(r"[^A-Za-z0-9]+", "-", norm).strip("-").lower()
    return norm or "campo"


def export(sub: str, token: str, *, skip_pipelines: list[str]) -> dict:
    cli = KommoClient(sub, token, read_only=True)
    account = cli.get("account")
    print(f"conta: {account['name']} (id {account['id']}, {account['country']}/{account['currency']})")

    # ---------------------------------------------------------- pipelines
    raw_pipelines = (cli.get("leads/pipelines").get("_embedded") or {}).get("pipelines") or []
    skip_norm = {s.strip().casefold() for s in skip_pipelines}
    pipelines = []
    # (pipeline_id, status_id) -> (nome do pipeline, nome da etapa).
    # A chave TEM de ser o par: 142/143 repetem o id em todos os pipelines, e
    # indexar só por status_id fazia o obrigatório de COMERCIAL/GANHO ser
    # exportado como se fosse de TRATAMENTO/ALTA.
    status_index: dict[tuple[int, int], tuple[str, str]] = {}
    for pl in sorted(raw_pipelines, key=lambda p: p["sort"]):
        if pl["name"].strip().casefold() in skip_norm:
            print(f"  - pipeline ignorado: {pl['name']!r}")
            continue
        statuses, system = [], {}
        for st in sorted(pl["_embedded"]["statuses"], key=lambda s: s["sort"]):
            status_index[(pl["id"], st["id"])] = (pl["name"], st["name"])
            if st["id"] in (142, 143):
                # Ganho/perdido: só o NOME viaja (a API não renomeia depois).
                system[str(st["id"])] = st["name"]
                continue
            if st["type"] == 1:
                # "Incoming leads" é criada pela própria Kommo com o pipeline.
                continue
            statuses.append({"name": st["name"], "sort": st["sort"], "color": st.get("color")})
        pipelines.append(
            {
                "name": pl["name"],
                "sort": pl["sort"],
                "is_main": pl["is_main"],
                # obrigatório no POST de pipeline (400 FieldMissing sem ele)
                "is_unsorted_on": pl.get("is_unsorted_on", True),
                "statuses": statuses,
                "system_statuses": system,
            }
        )
        print(f"  + pipeline {pl['name']!r}: {len(statuses)} etapas + 142/143")

    # ------------------------------------------------------------- grupos
    raw_groups = (cli.get("leads/custom_fields/groups").get("_embedded") or {}).get("custom_field_groups") or []
    group_name_by_id = {g["id"]: g["name"] for g in raw_groups}
    groups = [
        {"key": slug(g["name"]), "name": g["name"], "sort": g.get("sort", 0)}
        for g in sorted(raw_groups, key=lambda g: g.get("sort", 0))
        if g["id"] not in SYSTEM_GROUPS
    ]
    print(f"grupos exportados: {[g['name'] for g in groups]}")

    # ------------------------------------------------------------- campos
    raw_fields = cli.get_all("leads/custom_fields", "custom_fields")
    fields, seen_keys, skipped = [], {}, []
    for f in sorted(raw_fields, key=lambda f: (f.get("sort") or 0, f["id"])):
        if f.get("is_predefined"):
            continue  # utm_*, gclid… a Kommo já cria em toda conta
        gid = f.get("group_id")
        if gid in SYSTEM_GROUPS or gid not in group_name_by_id:
            skipped.append((f["name"], gid))
            continue
        gkey = slug(group_name_by_id[gid])
        if f.get("code"):
            key = f["code"]
        else:
            base = f"{gkey}::{slug(f['name'])}"
            n = seen_keys.get(base, 0) + 1
            seen_keys[base] = n
            key = f"{base}#{n}"
        entry = {
            "key": key,
            "name": f["name"],
            "type": f["type"],
            "group": gkey,
            "sort": f.get("sort"),
        }
        if f.get("code"):
            entry["code"] = f["code"]
        if f.get("is_api_only"):
            entry["is_api_only"] = True
        if f["type"] == "monetary":
            # obrigatório no POST, senão 400 FieldMissing
            entry["currency"] = f.get("currency") or account["currency"]
        if f.get("enums"):
            entry["enums"] = [
                {"value": e["value"], "sort": e.get("sort", i)}
                for i, e in enumerate(sorted(f["enums"], key=lambda e: e.get("sort", 0)))
            ]
        if f.get("required_statuses"):
            req = []
            for rs in f["required_statuses"]:
                ref = status_index.get((rs["pipeline_id"], rs["status_id"]))
                if not ref:
                    continue
                if ref[0].strip().casefold() in skip_norm:
                    continue
                req.append({"pipeline": ref[0], "status": ref[1]})
            if req:
                entry["required_statuses"] = req
        fields.append(entry)
    print(f"campos exportados: {len(fields)} (ignorados por grupo de sistema: {len(skipped)})")
    for name, gid in skipped:
        print(f"  - fora do blueprint: {name!r} (grupo {gid})")

    # ------------------------------------------------- motivos de perda
    raw_reasons = cli.get_all("leads/loss_reasons", "loss_reasons")
    reasons, dups = [], []
    seen = set()
    for r in sorted(raw_reasons, key=lambda r: r.get("sort", 0)):
        norm = r["name"].strip().casefold()
        if norm in seen:
            dups.append(r["name"])
            continue
        seen.add(norm)
        reasons.append({"name": r["name"].strip(), "sort": r.get("sort", 0)})
    print(f"motivos de perda: {len(reasons)} (duplicados descartados: {dups})")

    return {
        "blueprint_version": 1,
        "exported_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "source": {
            "subdomain": cli.subdomain,
            "account_id": account["id"],
            "currency": account["currency"],
        },
        "pipelines": pipelines,
        "field_groups": groups,
        "fields": fields,
        "loss_reasons": reasons,
        "notes": [
            "Tags NÃO são exportadas: na conta de origem são lixo de disparo em massa.",
            "Campos de contato NÃO são exportados (sobras da Cloudia).",
            "Os nomes de 142/143 vêm em system_statuses e precisam ser aplicados NA UI "
            "da conta destino — a API aceita o PATCH e ignora silenciosamente.",
        ],
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--subdomain", required=True, help="conta de ORIGEM (só leitura)")
    ap.add_argument("--token-file", required=True, type=Path)
    ap.add_argument("--out", required=True, type=Path)
    ap.add_argument(
        "--skip-pipeline",
        action="append",
        default=[],
        help="nome de pipeline a não exportar (ex.: 'NÂO USAR'); pode repetir",
    )
    args = ap.parse_args()

    token = args.token_file.read_text().strip()
    if not token:
        print("token vazio", file=sys.stderr)
        return 1

    bp = export(args.subdomain, token, skip_pipelines=args.skip_pipeline)
    args.out.write_text(json.dumps(bp, ensure_ascii=False, indent=2) + "\n")
    print(f"\nblueprint escrito em {args.out} ({args.out.stat().st_size} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
