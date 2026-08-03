#!/usr/bin/env python3
"""Aplica um blueprint de estrutura em uma conta Kommo DESTINO. Idempotente.

Segurança:
  * dry-run por padrão — só escreve com --apply;
  * contas de produção já existentes ficam numa lista de bloqueio (PROTECTED);
    a de Imperatriz está lá, ela é a MATRIZ e nunca deve ser escrita por aqui;
  * exige --confirm-subdomain igual a --subdomain para escrever.

Ordem de aplicação (há dependência real entre as etapas):
  1. pipelines + etapas       2. grupos de campos
  3. campos (+ enums)         4. required_statuses (depende de 1 e 3)
  5. motivos de perda         6. pipeline principal (is_main)
  7. lock file  key -> id     (fonte de verdade para n8n, IA e dashboard)

Uso:
    python3 apply_blueprint.py --blueprint blueprint.clinica-v1.json \
        --subdomain boavistarrdoutorhernia --token-file token.txt \
        --lock blueprint.boa-vista.lock.json            # dry-run
    ... --apply --confirm-subdomain boavistarrdoutorhernia
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import unicodedata
from pathlib import Path

from kommo import KommoClient, KommoError, chunked

# Contas que este script NUNCA pode escrever. Imperatriz é a matriz do blueprint.
PROTECTED = {
    "attivacorpoementeitz",      # Doutor Hérnia Imperatriz (unit 15) — MATRIZ
    "araguainadoutorhernia",
    "institutotraumakommon",
    "doutorherniaacailandia",
    "doutorherniabalsas",
    "doutorherniaporto",
    "doutorherniacanaa",
    "hmtecnologiakommon",
    "parauapebasdoutorhernia",
    "marabadoutorhernia",
    "drherniaserra",
    "magalhaesadv2025",
}

FIELD_CREATE_CHUNK = 50


def slug(text: str) -> str:
    norm = unicodedata.normalize("NFKD", text or "")
    norm = "".join(c for c in norm if not unicodedata.combining(c))
    norm = re.sub(r"[^A-Za-z0-9]+", "-", norm).strip("-").lower()
    return norm or "campo"


def norm(text: str) -> str:
    return (text or "").strip().casefold()


class Applier:
    def __init__(self, cli: KommoClient, bp: dict, *, apply: bool):
        self.cli = cli
        self.bp = bp
        self.apply = apply
        self.lock: dict = {"pipelines": {}, "groups": {}, "fields": {}, "loss_reasons": {}}
        self.plan: list[str] = []
        self.warnings: list[str] = []
        # (campo, pipeline, etapa) que só dá para tornar obrigatório pela UI (142/143)
        self.manual_required: list[tuple[str, str, str]] = []

    def say(self, action: str, what: str) -> None:
        prefix = {"create": "  + criar", "update": "  ~ ajustar", "keep": "  = já existe", "skip": "  - pular"}[action]
        line = f"{prefix} {what}"
        self.plan.append(line)
        print(line)

    # --------------------------------------------------------- 1. pipelines

    def apply_pipelines(self) -> None:
        print("\n[1/6] pipelines e etapas")
        existing = (self.cli.get("leads/pipelines").get("_embedded") or {}).get("pipelines") or []
        by_name = {norm(p["name"]): p for p in existing}

        for spec in self.bp["pipelines"]:
            cur = by_name.get(norm(spec["name"]))
            if cur is None:
                self.say("create", f"pipeline {spec['name']!r} com {len(spec['statuses'])} etapas")
                if self.apply:
                    body = [
                        {
                            "name": spec["name"],
                            "sort": spec["sort"],
                            "is_main": False,  # definido no passo 6, depois de tudo pronto
                            "is_unsorted_on": spec.get("is_unsorted_on", True),
                            "_embedded": {
                                "statuses": [
                                    {"name": s["name"], "sort": s["sort"], "color": s.get("color")}
                                    for s in spec["statuses"]
                                ]
                            },
                        }
                    ]
                    resp = self.cli.post("leads/pipelines", body)
                    cur = (resp.get("_embedded") or {}).get("pipelines", [])[0]
                else:
                    cur = None
            else:
                self.say("keep", f"pipeline {spec['name']!r} (id {cur['id']})")
                self._reconcile_statuses(cur, spec)

            if cur:
                statuses = {norm(s["name"]): s["id"] for s in cur["_embedded"]["statuses"]}
                self.lock["pipelines"][spec["name"]] = {"id": cur["id"], "statuses": statuses}
                self._check_system_status_names(cur, spec)

    def _reconcile_statuses(self, cur: dict, spec: dict) -> None:
        have = {norm(s["name"]) for s in cur["_embedded"]["statuses"]}
        missing = [s for s in spec["statuses"] if norm(s["name"]) not in have]
        for s in missing:
            self.say("create", f"  etapa {s['name']!r} em {spec['name']!r}")
            if self.apply:
                self.cli.post(
                    f"leads/pipelines/{cur['id']}/statuses",
                    [{"name": s["name"], "sort": s["sort"], "color": s.get("color")}],
                )
        if missing and self.apply:
            fresh = self.cli.get(f"leads/pipelines/{cur['id']}")
            cur["_embedded"] = fresh["_embedded"]

    def _check_system_status_names(self, cur: dict, spec: dict) -> None:
        """142/143 não podem ser renomeados por API — só reporta a divergência."""
        actual = {str(s["id"]): s["name"] for s in cur["_embedded"]["statuses"] if s["id"] in (142, 143)}
        for sid, want in (spec.get("system_statuses") or {}).items():
            got = actual.get(sid)
            if got and norm(got) != norm(want):
                self.warnings.append(
                    f"pipeline {spec['name']!r}: status {sid} está {got!r}, o blueprint pede {want!r} — "
                    f"renomear MANUALMENTE na UI da Kommo (a API ignora)."
                )

    # ------------------------------------------------------------ 2. grupos

    def apply_groups(self) -> dict[str, str]:
        print("\n[2/6] grupos de campos")
        existing = (self.cli.get("leads/custom_fields/groups").get("_embedded") or {}).get("custom_field_groups") or []
        by_name = {norm(g["name"]): g for g in existing}
        mapping: dict[str, str] = {}
        for spec in self.bp["field_groups"]:
            cur = by_name.get(norm(spec["name"]))
            if cur:
                self.say("keep", f"grupo {spec['name']!r} ({cur['id']})")
            else:
                self.say("create", f"grupo {spec['name']!r}")
                if self.apply:
                    resp = self.cli.post(
                        "leads/custom_fields/groups", [{"name": spec["name"], "sort": spec["sort"]}]
                    )
                    cur = (resp.get("_embedded") or {}).get("custom_field_groups", [])[0]
            if cur:
                mapping[spec["key"]] = cur["id"]
                self.lock["groups"][spec["key"]] = {"id": cur["id"], "name": spec["name"]}
        return mapping

    # ------------------------------------------------------------ 3. campos

    def _index_existing_fields(self, group_name_by_id: dict[str, str]) -> dict[str, dict]:
        """Indexa os campos do destino pela mesma chave usada no export."""
        raw = self.cli.get_all("leads/custom_fields", "custom_fields")
        idx, seen = {}, {}
        for f in sorted(raw, key=lambda f: (f.get("sort") or 0, f["id"])):
            if f.get("is_predefined"):
                continue
            gname = group_name_by_id.get(f.get("group_id"))
            if not gname:
                continue
            if f.get("code"):
                idx[f["code"]] = f
                continue
            base = f"{slug(gname)}::{slug(f['name'])}"
            n = seen.get(base, 0) + 1
            seen[base] = n
            idx[f"{base}#{n}"] = f
        return idx

    def apply_fields(self, group_ids: dict[str, str]) -> None:
        print("\n[3/6] campos")
        group_name_by_key = {g["key"]: g["name"] for g in self.bp["field_groups"]}
        group_name_by_id = {gid: group_name_by_key[k] for k, gid in group_ids.items()}
        existing = self._index_existing_fields(group_name_by_id) if group_ids else {}

        to_create = []
        for spec in self.bp["fields"]:
            cur = existing.get(spec["key"])
            if cur:
                self.say("keep", f"campo {spec['name']!r} ({spec['key']} -> {cur['id']})")
                self.lock["fields"][spec["key"]] = self._field_lock(spec, cur)
                continue
            gid = group_ids.get(spec["group"])
            if gid is None and self.apply:
                self.warnings.append(f"campo {spec['name']!r}: grupo {spec['group']} inexistente")
                continue
            self.say("create", f"campo {spec['name']!r} [{spec['type']}] em {spec['group']}")
            body = {"name": spec["name"], "type": spec["type"], "sort": spec.get("sort") or 0}
            if gid:
                body["group_id"] = gid
            if spec.get("code"):
                body["code"] = spec["code"]
            if spec.get("is_api_only"):
                body["is_api_only"] = True
            if spec["type"] == "monetary":
                body["currency"] = spec.get("currency", "BRL")  # sem isso: 400 FieldMissing
            if spec.get("enums"):
                body["enums"] = [{"value": e["value"], "sort": e["sort"]} for e in spec["enums"]]
            to_create.append((spec, body))

        if not self.apply or not to_create:
            return

        for batch in chunked(to_create, FIELD_CREATE_CHUNK):
            payload = [b for _, b in batch]
            try:
                resp = self.cli.post("leads/custom_fields", payload)
                created = (resp.get("_embedded") or {}).get("custom_fields") or []
            except KommoError as exc:
                print(f"    ! lote de {len(payload)} falhou, tentando um a um: {exc}")
                created = []
                for spec, body in batch:
                    try:
                        r = self.cli.post("leads/custom_fields", [body])
                        created.extend((r.get("_embedded") or {}).get("custom_fields") or [])
                    except KommoError as e2:
                        self.warnings.append(f"campo {spec['name']!r} NÃO criado: {e2}")
                        created.append(None)
            for (spec, _), cur in zip(batch, created):
                if cur:
                    self.lock["fields"][spec["key"]] = self._field_lock(spec, cur)

    def _field_lock(self, spec: dict, cur: dict) -> dict:
        entry = {"id": cur["id"], "name": spec["name"], "type": spec["type"], "group": spec["group"]}
        if spec.get("code"):
            entry["code"] = spec["code"]
        if cur.get("enums"):
            entry["enums"] = {e["value"]: e["id"] for e in cur["enums"]}
        return entry

    # ------------------------------------------------- 4. required_statuses

    def apply_required_statuses(self) -> None:
        print("\n[4/6] campos obrigatórios por etapa")
        # 142/143 mantêm o id em toda conta, mas o NOME na conta destino ainda é o
        # padrão da Kommo ("Closed - won") até alguém renomear na UI. Então etapa de
        # sistema é resolvida pelo id que veio em system_statuses, não pelo nome.
        system_by_pipeline = {
            p["name"]: {norm(name): int(sid) for sid, name in (p.get("system_statuses") or {}).items()}
            for p in self.bp["pipelines"]
        }
        patches = []
        for spec in self.bp["fields"]:
            req = spec.get("required_statuses")
            if not req:
                continue
            locked = self.lock["fields"].get(spec["key"])
            if not locked:
                continue
            resolved = []
            for r in req:
                pl = self.lock["pipelines"].get(r["pipeline"])
                if not pl:
                    continue
                sid = pl["statuses"].get(norm(r["status"]))
                if sid is None:
                    sid = system_by_pipeline.get(r["pipeline"], {}).get(norm(r["status"]))
                if sid is None:
                    self.warnings.append(
                        f"required_statuses de {spec['name']!r}: etapa {r['status']!r} não encontrada"
                    )
                    continue
                if sid in (142, 143):
                    # Verificado na conta de Boa Vista (jul/2026): a API aceita o PATCH,
                    # responde 200 e devolve required_statuses=[] — vale para ganho (142)
                    # E para perda (143). Só dá para marcar obrigatório nessas etapas pela
                    # UI da Kommo (Configurações do funil > etapa > campos obrigatórios).
                    self.manual_required.append((spec["name"], r["pipeline"], r["status"]))
                    continue
                resolved.append({"pipeline_id": pl["id"], "status_id": sid})
            if resolved:
                self.say("update", f"{spec['name']!r} obrigatório em {len(resolved)} etapa(s)")
                patch = {"id": locked["id"], "required_statuses": resolved}
                if spec["type"] == "monetary":
                    # PATCH de campo monetary sem currency = 400 FieldMissing, mesmo
                    # quando o PATCH não mexe no valor.
                    patch["currency"] = spec.get("currency", "BRL")
                patches.append(patch)
        if self.apply and patches:
            ok, bad = self.cli.patch_bisect("leads/custom_fields", patches)
            print(f"    {len(ok)} aplicados, {len(bad)} com erro")
            for item, err in bad:
                self.warnings.append(f"required_statuses id={item['id']}: {err}")

    # ----------------------------------------------------- 5. loss reasons

    def apply_loss_reasons(self) -> None:
        print("\n[5/6] motivos de perda")
        existing = self.cli.get_all("leads/loss_reasons", "loss_reasons")
        have = {norm(r["name"]): r for r in existing}
        missing = [r for r in self.bp["loss_reasons"] if norm(r["name"]) not in have]
        for r in self.bp["loss_reasons"]:
            cur = have.get(norm(r["name"]))
            if cur:
                self.lock["loss_reasons"][r["name"]] = cur["id"]
        if not missing:
            self.say("keep", f"{len(self.bp['loss_reasons'])} motivos já presentes")
            return
        self.say("create", f"{len(missing)} motivos de perda")
        if self.apply:
            # sort do motivo de perda tem de ficar entre 1 e 100000 (a origem exporta 0).
            resp = self.cli.post(
                "leads/loss_reasons",
                [{"name": r["name"], "sort": max(1, r.get("sort") or i + 1)} for i, r in enumerate(missing)],
            )
            for cur in (resp.get("_embedded") or {}).get("loss_reasons") or []:
                self.lock["loss_reasons"][cur["name"]] = cur["id"]

    # ------------------------------------------------------- 6. is_main

    def apply_main_pipeline(self, retire: str | None) -> None:
        print("\n[6/6] pipeline principal")
        main_spec = next((p for p in self.bp["pipelines"] if p.get("is_main")), None)
        if not main_spec:
            return
        locked = self.lock["pipelines"].get(main_spec["name"])
        self.say("update", f"{main_spec['name']!r} vira o funil principal (novos leads entram nele)")
        if self.apply and locked:
            self.cli.patch(f"leads/pipelines/{locked['id']}", {"is_main": True})

        if not retire:
            return
        pipelines = (self.cli.get("leads/pipelines").get("_embedded") or {}).get("pipelines") or []
        target = next((p for p in pipelines if norm(p["name"]) == norm(retire)), None)
        if not target:
            return
        self.say("update", f"aposentar pipeline {target['name']!r} (renomear p/ 'NÃO USAR' e tentar apagar)")
        if self.apply:
            try:
                self.cli.patch(f"leads/pipelines/{target['id']}", {"name": "NÃO USAR", "is_main": False})
                self.cli.delete(f"leads/pipelines/{target['id']}")
                print("    apagado")
            except KommoError as exc:
                self.warnings.append(
                    f"pipeline {retire!r} renomeado mas não apagado ({exc.status}); "
                    f"a Kommo só apaga pipeline sem nenhum lead, inclusive na lixeira."
                )


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--blueprint", required=True, type=Path)
    ap.add_argument("--subdomain", required=True, help="conta DESTINO")
    ap.add_argument("--token-file", required=True, type=Path)
    ap.add_argument("--lock", required=True, type=Path, help="saída: mapa key -> id da conta destino")
    ap.add_argument("--apply", action="store_true", help="sem isto, só imprime o plano")
    ap.add_argument("--confirm-subdomain", help="repita o subdomínio para confirmar a escrita")
    ap.add_argument("--retire-default-pipeline", help="nome do funil padrão a aposentar no fim")
    args = ap.parse_args()

    sub = args.subdomain.replace(".kommo.com", "").strip("/ ")
    if sub in PROTECTED:
        print(f"RECUSADO: {sub!r} está na lista de contas protegidas.", file=sys.stderr)
        return 2
    if args.apply and args.confirm_subdomain != sub:
        print("RECUSADO: --apply exige --confirm-subdomain igual a --subdomain.", file=sys.stderr)
        return 2

    bp = json.loads(args.blueprint.read_text())
    token = args.token_file.read_text().strip()
    cli = KommoClient(sub, token, read_only=not args.apply)
    account = cli.get("account")
    print(f"destino: {account['name']} (id {account['id']}) — modo {'APLICAR' if args.apply else 'DRY-RUN'}")
    print(f"origem do blueprint: {bp['source']['subdomain']} @ {bp['exported_at']}")

    app = Applier(cli, bp, apply=args.apply)
    app.apply_pipelines()
    group_ids = app.apply_groups()
    app.apply_fields(group_ids)
    app.apply_required_statuses()
    app.apply_loss_reasons()
    app.apply_main_pipeline(args.retire_default_pipeline)

    if args.apply:
        app.lock["target"] = {"subdomain": sub, "account_id": account["id"]}
        args.lock.write_text(json.dumps(app.lock, ensure_ascii=False, indent=2) + "\n")
        print(f"\nlock escrito em {args.lock}")

    if app.manual_required:
        print("\nOBRIGATÓRIOS QUE PRECISAM SER MARCADOS NA UI (a API ignora em 142/143):")
        for name, pipeline, status in app.manual_required:
            print(f"  · {pipeline} > {status}: {name}")

    if app.warnings:
        print("\nAVISOS (ação manual pode ser necessária):")
        for w in app.warnings:
            print(f"  ! {w}")
    print(f"\n{len(app.plan)} ações no plano.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
