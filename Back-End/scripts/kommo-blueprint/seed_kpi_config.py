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
import sys
from pathlib import Path

from kommo import KommoClient

# KPI -> (source_type, como montar o config). O nome do campo é resolvido no lock.
PLANO = [
    ("total_leads", "created", None),
    ("cadastro", "custom_field_count", ("⬢ Tipo de lead", ["Cadastro"])),
    ("resgate", "custom_field_count", ("⬢ Tipo de lead", ["Resgate"])),
    # Já passou por duas fontes na Kommo e as duas dependiam de digitação: o campo
    # `✓ Agendou` (medido em 2026-08-18: VAZIO em 9 das 10 unidades, card lia 0) e a
    # etapa AGENDADO (o card que a SDR arrasta à mão). Desde 2026-08-28 vem da agenda
    # da franquia: horário de avaliação marcado PARA o período, em qualquer situação.
    # Mesmo recorte de no_show/consultas/tratamentos, então o funil inteiro passa a
    # responder a mesma pergunta — "o que aconteceu na clínica neste período".
    ("agendados", "franquia", {"metric": "agendados"}),
    ("interacoes", "custom_field_count", ("✓ Interação", ["Sim"])),
    ("no_show", "franquia", {"metric": "no_show"}),
    ("consultas", "franquia", {"metric": "consultas"}),
    ("tratamentos", "franquia", {"metric": "tratamentos"}),
]

# Os cards `profile_*` do painel do lead: KPI -> nome do campo canônico. A Imperatriz
# é a base; estes vieram do conjunto dela (2026-08-18) e só entram se o campo existir
# na conta destino (os `¤ monetary` faltam onde o plano da Kommo trava a criação).
PERFIL = [
    ("profile_origem", "⚑ Origem"),
    ("profile_tipo", "⬢ Tipo de lead"),
    ("profile_qualificacao", "★ Qualificação (Quente/Morno/Frio)"),
    ("profile_responsavel", "☻ Responsável agendamento"),
    ("profile_tipo_agendamento", "⬢ Tipo de agendamento"),
    ("profile_motivo_nao_agendamento", "⊘ Motivo do não agendamento"),
    ("profile_appointment_date", "◷ Data da Consulta"),
    ("profile_birthdate", "◷ Data de nascimento"),
    ("profile_doctor", "⚕ Fisioterapeuta"),
    ("profile_fisioterapeuta", "⚕ Fisioterapeuta"),
    ("profile_tipo_tratamento", "⚕ Tratamento indicado"),
    ("profile_tratamento_fechado", "✓ Fechou tratamento"),
    ("profile_valor_consulta", "¤ Valor da Consulta"),
    ("profile_valor_tratamento", "¤ Valor do tratamento"),
    ("profile_pausar_ia", "Pausar IA"),
]


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--lock", type=Path, help="lock do apply_blueprint (chave -> id)")
    ap.add_argument(
        "--from-account",
        metavar="SUBDOMINIO",
        help="alternativa ao --lock: lê os campos direto da conta Kommo. Útil para "
        "unidades provisionadas antes do blueprint, que não têm lock.",
    )
    ap.add_argument("--token-file", type=Path, help="token da conta, junto com --from-account")
    ap.add_argument("--unit-id", required=True, type=int)
    ap.add_argument("--clinic-id", required=True, type=int)
    ap.add_argument("--sem-perfil", action="store_true", help="não emitir os cards profile_*")
    ap.add_argument("--out", type=Path)
    args = ap.parse_args()

    if bool(args.lock) == bool(args.from_account):
        print("RECUSADO: use --lock OU --from-account (um dos dois).", file=sys.stderr)
        return 2

    etapas: dict[str, list[int]] = {}   # nome da etapa (casefold) -> ids
    if args.lock:
        lock = json.loads(args.lock.read_text())
        by_name = {f["name"]: f for f in lock["fields"].values()}
        for pl in lock.get("pipelines", {}).values():
            for nome_etapa, sid in (pl.get("statuses") or {}).items():
                etapas.setdefault(nome_etapa.strip().casefold(), []).append(sid)
    else:
        if not args.token_file:
            print("RECUSADO: --from-account exige --token-file.", file=sys.stderr)
            return 2
        cli = KommoClient(args.from_account, args.token_file.read_text().strip(), read_only=True, verbose=False)
        by_name = {
            f["name"].strip(): {"id": f["id"], "name": f["name"].strip(),
                                "enums": {e["value"]: e["id"] for e in (f.get("enums") or [])}}
            for f in cli.get_all("leads/custom_fields", "custom_fields")
        }
        for pl in (cli.get("leads/pipelines").get("_embedded") or {}).get("pipelines") or []:
            for s in pl["_embedded"]["statuses"]:
                etapas.setdefault(s["name"].strip().casefold(), []).append(s["id"])

    linhas = ["-- gerado por seed_kpi_config.py — não editar à mão", "BEGIN;"]
    for kpi, source, spec in PLANO:
        if source == "created":
            cfg: dict = {}
        elif source == "franquia":
            cfg = spec  # type: ignore[assignment]
        elif source == "kommo_stage":
            (nome_etapa,) = spec  # type: ignore[misc]
            ids = etapas.get(nome_etapa.strip().casefold())
            if not ids:
                print(f"  ! etapa {nome_etapa!r} não existe nesta conta — KPI {kpi} pulado")
                continue
            cfg = {"stageIds": ids}
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

    if not args.sem_perfil:
        for kpi, nome in PERFIL:
            campo = by_name.get(nome)
            if not campo:
                print(f"  ! campo {nome!r} não existe nesta conta — {kpi} pulado")
                continue
            cfg_json = json.dumps({"fieldId": campo["id"], "matchValues": []}, ensure_ascii=False).replace("'", "''")
            linhas.append(
                "INSERT INTO kpi_configurations "
                '("UnitId","ClinicId","KpiKey","SourceType","ConfigJson","IsCustom","DisplayType",'
                '"SortOrder","CreatedAt","UpdatedAt") '
                f"VALUES ({args.unit_id},{args.clinic_id},'{kpi}','custom_field_count','{cfg_json}',"
                "false,'number',0,now(),now()) "
                'ON CONFLICT ("UnitId","KpiKey") DO UPDATE '
                'SET "SourceType"=EXCLUDED."SourceType", "ConfigJson"=EXCLUDED."ConfigJson", '
                '"UpdatedAt"=now();'
            )
            print(f"  {kpi:32} -> {nome}")

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
