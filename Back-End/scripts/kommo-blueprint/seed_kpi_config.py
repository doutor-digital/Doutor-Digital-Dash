#!/usr/bin/env python3
"""Gera o SQL que aponta os KPIs do dashboard para os campos da unidade na Kommo.

Lê o `blueprint.<unidade>.lock.json` (chave -> id real da conta) e emite os upserts
de `kpi_configurations`. Nada de id chumbado: se a conta for reprovisionada, roda de
novo a partir do lock novo.

KPIs de fonte `franquia` (no-show, consultas, tratamentos) são incluídos de propósito
mesmo sem o token: o back devolve a nota `sem_autorizacao_franquia` e o card mostra
"Sem autorização da franquia" em vez de 0. Quando a franquia liberar o token, o mesmo
mapeamento passa a trazer número sem precisar reconfigurar nada.

Uso:
    python3 seed_kpi_config.py --lock blueprint.boa-vista.lock.json \
        --unit-id 26 --clinic-id 8033 --out seed_kpi_boa_vista.sql
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

# KPI -> (source_type, como montar o config). O nome do campo é resolvido no lock.
PLANO = [
    ("total_leads", "created", None),
    ("cadastro", "custom_field_count", ("⬢ Tipo de lead", ["Cadastro"])),
    ("resgate", "custom_field_count", ("⬢ Tipo de lead", ["Resgate"])),
    ("agendados", "custom_field_count", ("✓ Agendou", ["Sim"])),
    ("interacoes", "custom_field_count", ("✓ Interação", ["Sim"])),
    ("no_show", "franquia", {"metric": "no_show"}),
    ("consultas", "franquia", {"metric": "consultas"}),
    ("tratamentos", "franquia", {"metric": "tratamentos"}),
]


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--lock", required=True, type=Path)
    ap.add_argument("--unit-id", required=True, type=int)
    ap.add_argument("--clinic-id", required=True, type=int)
    ap.add_argument("--out", type=Path)
    args = ap.parse_args()

    lock = json.loads(args.lock.read_text())
    by_name = {f["name"]: f for f in lock["fields"].values()}

    linhas = ["-- gerado por seed_kpi_config.py — não editar à mão", "BEGIN;"]
    for kpi, source, spec in PLANO:
        if source == "created":
            cfg: dict = {}
        elif source == "franquia":
            cfg = spec  # type: ignore[assignment]
        else:
            nome, valores = spec  # type: ignore[misc]
            campo = by_name.get(nome)
            if not campo:
                print(f"  ! campo {nome!r} não está no lock — KPI {kpi} pulado")
                continue
            faltando = [v for v in valores if v not in (campo.get("enums") or {})]
            if faltando:
                print(f"  ! opções {faltando} não existem em {nome!r} — KPI {kpi} pulado")
                continue
            cfg = {"fieldId": campo["id"], "matchValues": valores}

        cfg_json = json.dumps(cfg, ensure_ascii=False).replace("'", "''")
        linhas.append(
            "INSERT INTO kpi_configurations "
            '("UnitId","ClinicId","KpiKey","SourceType","ConfigJson","IsCustom","DisplayType",'
            '"SortOrder","CreatedAt","UpdatedAt") '
            f"VALUES ({args.unit_id},{args.clinic_id},'{kpi}','{source}','{cfg_json}',"
            "false,'number',0,now(),now()) "
            'ON CONFLICT ("UnitId","KpiKey") DO UPDATE '
            'SET "SourceType"=EXCLUDED."SourceType", "ConfigJson"=EXCLUDED."ConfigJson", '
            '"UpdatedAt"=now();'
        )
        print(f"  {kpi:12} -> {source:20} {cfg_json}")

    linhas.append("COMMIT;")
    sql = "\n".join(linhas) + "\n"
    if args.out:
        args.out.write_text(sql)
        print(f"\nSQL escrito em {args.out}")
    else:
        print("\n" + sql)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
