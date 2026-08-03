#!/usr/bin/env python3
"""Importa leads de um CSV de outro CRM para a Kommo, usando o de-para de um mapping JSON.

Idempotente por telefone: cada lead criado é registrado num arquivo de estado
(JSONL). Reexecutar pula o que já subiu. Isso importa porque **a Kommo não deixa
apagar lead por API** (DELETE = 405) — um import errado só se desfaz na mão, pela
interface. Por isso o fluxo é: --dry-run -> --piloto -> import completo.

Cria lead + contato juntos (POST /leads/complex) e grava:
  * pipeline/status conforme o de-para (perdido já com loss_reason no mesmo POST);
  * created_at histórico (data no fim do nome; fallback: última mensagem) — sem
    isso a base inteira entra com a data de hoje e estoura o KPI do dia;
  * custom fields resolvidos pelo lock (nunca id chumbado).

Uso:
    python3 import_leads_csv.py --csv "contatos.csv" --mapping mapping.boa-vista.json \\
        --lock blueprint.boa-vista.lock.json --subdomain boavistarrdoutorhernia \\
        --token-file token.txt --state import.boa-vista.jsonl            # dry-run
    ... --piloto            # 1 lead de cada etapa, para conferir no cartão
    ... --apply --confirm-subdomain boavistarrdoutorhernia
"""

from __future__ import annotations

import argparse
import csv
import json
import re
import sys
from collections import Counter, defaultdict
from datetime import datetime, timedelta, timezone
from pathlib import Path

from apply_blueprint import PROTECTED, norm
from kommo import KommoClient, KommoError, chunked

BATCH = 50
NAME_DATE = re.compile(r"(\d{1,2})/(\d{1,2})/(\d{2,4})\s*$")


def parse_dt(value: str, tz: timezone) -> int | None:
    """'12/12/2025 08:22' ou '12/12/2025' -> unix seconds, no fuso da conta."""
    value = (value or "").strip()
    if not value:
        return None
    for fmt in ("%d/%m/%Y %H:%M", "%d/%m/%Y %H:%M:%S", "%d/%m/%Y"):
        try:
            return int(datetime.strptime(value, fmt).replace(tzinfo=tz).timestamp())
        except ValueError:
            continue
    return None


def created_at_from_name(name: str, tz: timezone) -> int | None:
    """O CRM antigo carimbava a data do cadastro no fim do nome ('Fulano 19/1/25')."""
    m = NAME_DATE.search(name or "")
    if not m:
        return None
    d, mo, y = (int(g) for g in m.groups())
    if len(m.group(3)) == 2:
        y += 2000
    if not (1 <= d <= 31 and 1 <= mo <= 12 and 2015 <= y <= 2035):
        return None
    try:
        return int(datetime(y, mo, d, 12, 0, tzinfo=tz).timestamp())
    except ValueError:
        return None


def only_digits(s: str) -> str:
    return re.sub(r"\D", "", s or "")


class Importer:
    def __init__(self, cli: KommoClient, mapping: dict, lock: dict, tz: timezone, *, apply: bool):
        self.cli = cli
        self.map = mapping
        self.lock = lock
        self.tz = tz
        self.apply = apply
        self.field_by_name = {f["name"]: f for f in lock["fields"].values()}
        self.problems: list[str] = []

    # ------------------------------------------------------------- helpers

    def field(self, name: str) -> dict | None:
        f = self.field_by_name.get(name)
        if f is None:
            self.problems.append(f"campo {name!r} não existe no lock")
        return f

    def cf_text(self, name: str, value) -> dict | None:
        f = self.field(name)
        if not f or value in (None, ""):
            return None
        return {"field_id": f["id"], "values": [{"value": str(value)}]}

    def cf_date(self, name: str, unix: int | None) -> dict | None:
        f = self.field(name)
        if not f or not unix:
            return None
        return {"field_id": f["id"], "values": [{"value": unix}]}

    def cf_enum(self, name: str, option: str) -> dict | None:
        f = self.field(name)
        if not f:
            return None
        enum_id = (f.get("enums") or {}).get(option)
        if enum_id is None:
            self.problems.append(f"opção {option!r} não existe no campo {name!r}")
            return None
        return {"field_id": f["id"], "values": [{"enum_id": enum_id}]}

    def ensure_extra_field(self) -> None:
        """Cria o campo que só existe em Boa Vista (recência do CRM antigo)."""
        spec = self.map.get("campo_extra_boa_vista")
        if not spec or spec["key"] in self.lock["fields"]:
            return
        if not self.apply:
            print(f"  (dry-run) criaria o campo {spec['name']!r}")
            return
        resp = self.cli.post(
            "leads/custom_fields",
            [
                {
                    "name": spec["name"],
                    "type": spec["type"],
                    "group_id": self.lock["groups"][spec["group"]]["id"],
                }
            ],
        )
        cur = (resp.get("_embedded") or {}).get("custom_fields", [])[0]
        self.lock["fields"][spec["key"]] = {
            "id": cur["id"], "name": spec["name"], "type": spec["type"], "group": spec["group"],
        }
        self.field_by_name[spec["name"]] = self.lock["fields"][spec["key"]]
        print(f"  campo {spec['name']!r} criado (id {cur['id']})")

    # -------------------------------------------------------------- payload

    def build(self, row: dict) -> dict | None:
        c = self.map["csv"]
        etapa = (row[c["coluna_etapa"]] or "").strip()
        rule = self.map["etapas"].get(etapa)
        if not rule:
            self.problems.append(f"etapa sem de-para: {etapa!r}")
            return None

        pipe = self.lock["pipelines"].get(rule["pipeline"])
        status_id = pipe["statuses"].get(norm(rule["status"]))
        if status_id is None:
            # Ganho/perdido ainda com o nome padrão da Kommo na conta destino.
            alias = {"PERDIDO": 143, "TRATAMENTO CANCELADO": 143, "ALTA": 142, "GANHO / CONCLUÍDO": 142}
            status_id = alias.get(rule["status"].upper())
        if status_id is None:
            self.problems.append(f"etapa {rule['status']!r} não encontrada em {rule['pipeline']!r}")
            return None

        name = (row[c["coluna_nome"]] or "").strip() or "LEAD IMPORTADO"
        phone = (row[c["coluna_telefone"]] or "").strip()
        bloqueado = (row[c["coluna_bloqueado"]] or "").strip().casefold() == "sim"

        obs = (row[c["coluna_observacoes"]] or "").strip()
        if bloqueado:
            obs = (self.map["bloqueado"]["prefixo_observacao"] + obs).strip()

        origem_raw = (row[c["coluna_tags"]] or "").split("|")[0].strip()
        origem = self.map["origem"].get(origem_raw, self.map["origem"]["_default"])

        created = created_at_from_name(name, self.tz) or parse_dt(row[c["coluna_ultima_mensagem"]], self.tz)

        cfs = [
            self.cf_text("✎ Observações", obs),
            self.cf_enum("⚑ Origem", origem),
            self.cf_text("⌂ Campanha", (row[c["coluna_anuncio"]] or "").strip()),
            self.cf_date("◷ Data da Consulta", parse_dt(row[c["coluna_data_consulta"]], self.tz)),
            self.cf_date("◷ Data de agendamento", parse_dt(row[c["coluna_data_agendamento"]], self.tz)),
            self.cf_date("◷ Data de criação lead", created),
        ]
        extra = self.map.get("campo_extra_boa_vista")
        if extra:
            cfs.append(self.cf_date(extra["name"], parse_dt(row[c["coluna_ultima_mensagem"]], self.tz)))
        for fname, option in (rule.get("campos") or {}).items():
            cfs.append(self.cf_enum(fname, option))

        lead: dict = {
            "name": name,
            "pipeline_id": pipe["id"],
            "status_id": status_id,
            "custom_fields_values": [x for x in cfs if x],
        }
        if created:
            lead["created_at"] = created

        motivo = rule.get("motivo_perda")
        if bloqueado and status_id == 143:
            motivo = self.map["bloqueado"]["motivo_perda_override"]
        if status_id == 143 and motivo:
            # loss_reason_id SÓ é aceito junto do status 143, no mesmo request.
            rid = self.lock["loss_reasons"].get(motivo)
            if rid:
                lead["loss_reason_id"] = rid
            else:
                self.problems.append(f"motivo de perda {motivo!r} não existe no lock")

        if phone:
            lead["_embedded"] = {
                "contacts": [
                    {
                        "first_name": name,
                        "custom_fields_values": [
                            {"field_code": "PHONE", "values": [{"value": phone, "enum_code": "MOB"}]}
                        ],
                    }
                ]
            }
        return lead


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--csv", required=True, type=Path)
    ap.add_argument("--mapping", required=True, type=Path)
    ap.add_argument("--lock", required=True, type=Path)
    ap.add_argument("--subdomain", required=True)
    ap.add_argument("--token-file", required=True, type=Path)
    ap.add_argument("--state", required=True, type=Path, help="JSONL de telefones já importados")
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--confirm-subdomain")
    ap.add_argument("--piloto", action="store_true", help="1 lead de cada etapa de origem")
    ap.add_argument("--limit", type=int)
    args = ap.parse_args()

    sub = args.subdomain.replace(".kommo.com", "").strip("/ ")
    if sub in PROTECTED:
        print(f"RECUSADO: {sub!r} é conta protegida.", file=sys.stderr)
        return 2
    if args.apply and args.confirm_subdomain != sub:
        print("RECUSADO: --apply exige --confirm-subdomain igual a --subdomain.", file=sys.stderr)
        return 2

    mapping = json.loads(args.mapping.read_text())
    lock = json.loads(args.lock.read_text())
    cli = KommoClient(sub, args.token_file.read_text().strip(), read_only=not args.apply)

    acc = cli.get("account", **{"with": "datetime_settings"})
    # datetime_settings vem dentro de _embedded, não na raiz.
    dts = (acc.get("_embedded") or {}).get("datetime_settings") or {}
    off = dts.get("timezone_offset")
    if not off:
        print("ERRO: não consegui ler o fuso da conta; sem isso as datas entram deslocadas.", file=sys.stderr)
        return 2
    sign = -1 if off.startswith("-") else 1
    tz = timezone(sign * timedelta(hours=int(off[1:3]), minutes=int(off[4:6])))
    print(f"destino: {acc['name']} | fuso {dts.get('timezone')} ({off}) "
          f"| modo {'APLICAR' if args.apply else 'DRY-RUN'}")

    done = set()
    if args.state.exists():
        for line in args.state.read_text().splitlines():
            if line.strip():
                done.add(json.loads(line)["telefone"])
    print(f"já importados no estado: {len(done)}")

    rows = list(csv.DictReader(args.csv.open(encoding=mapping["csv"]["encoding"])))
    imp = Importer(cli, mapping, lock, tz, apply=args.apply)
    imp.ensure_extra_field()

    pending, skipped, seen_stage = [], 0, set()
    for row in rows:
        phone = only_digits(row[mapping["csv"]["coluna_telefone"]])
        if phone in done:
            skipped += 1
            continue
        etapa = (row[mapping["csv"]["coluna_etapa"]] or "").strip()
        if args.piloto:
            if etapa in seen_stage:
                continue
            seen_stage.add(etapa)
        lead = imp.build(row)
        if lead:
            pending.append((phone, etapa, lead))
        if args.limit and len(pending) >= args.limit:
            break

    dist = Counter()
    for _, etapa, lead in pending:
        rule = mapping["etapas"][etapa]
        dist[f"{rule['pipeline']} · {rule['status']}"] += 1
    print(f"\na importar: {len(pending)} | pulados (já no estado): {skipped}")
    for k, v in dist.most_common():
        print(f"  {v:6}  {k}")

    sem_created = sum(1 for _, _, l in pending if "created_at" not in l)
    print(f"\nsem created_at histórico (entram com a data de hoje): {sem_created}")

    if imp.problems:
        counts = Counter(imp.problems)
        print("\nPROBLEMAS:")
        for p, n in counts.most_common(15):
            print(f"  {n:5}x {p}")

    if not args.apply:
        if pending:
            print("\nexemplo de payload:")
            print(json.dumps(pending[0][2], ensure_ascii=False, indent=2)[:1400])
        print("\nDRY-RUN — nada foi criado.")
        return 0

    created_total, failed = 0, 0
    with args.state.open("a") as state:
        for batch in chunked(pending, BATCH):
            payload = [l for _, _, l in batch]
            try:
                resp = cli.post("leads/complex", payload)
            except KommoError as exc:
                print(f"  ! lote de {len(payload)} falhou ({exc.status}); tentando um a um")
                resp = []
                for phone, etapa, lead in batch:
                    try:
                        r = cli.post("leads/complex", [lead])
                        resp.extend(r if isinstance(r, list) else [r])
                    except KommoError as e2:
                        failed += 1
                        print(f"    x {lead['name']!r}: {e2}")
                        resp.append(None)
            for (phone, etapa, lead), out in zip(batch, resp if isinstance(resp, list) else []):
                if not out:
                    continue
                state.write(json.dumps({"telefone": phone, "lead_id": out.get("id"),
                                        "contact_id": out.get("contact_id"), "etapa": etapa},
                                       ensure_ascii=False) + "\n")
                created_total += 1
            state.flush()
            print(f"  {created_total}/{len(pending)} criados")

    args.lock.write_text(json.dumps(lock, ensure_ascii=False, indent=2) + "\n")
    print(f"\ncriados: {created_total} | falhas: {failed} | estado em {args.state}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
