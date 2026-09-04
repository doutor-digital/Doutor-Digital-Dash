"""Fase 2 (Fluxo B do playbook) — reestrutura o funil de uma unidade ANTIGA no
padrão Imperatriz SEM criar funil novo: rename-no-lugar + migração de leads.

Por que não `apply_blueprint.apply_pipelines`: ele CRIA um pipeline com o nome do
blueprint. Numa conta com leads isso obriga a mover 100% da base e mata toda
automação da Kommo presa às etapas velhas. Renomear preserva o `status_id`, então
o lead com mais volume nem se move.

Passos (rodar em ordem, `--apply` para escrever):

    snapshot     grava lead_id -> (pipeline, status) de ANTES  (é o desfazer)
    webhook-off  remove as inscrições de webhook (não disparar a migração inteira)
    rename       renomeia pipeline + as 4 etapas âncora (name+color+sort juntos,
                 sort temporário alto para não colidir) e cria o funil TRATAMENTO
    migrate      move os leads pelo DEPARA, com patch_bisect e motivo de perda
    cleanup      apaga as etapas legadas já esvaziadas e cria EM NEGOCIAÇÃO
    sort         2ª passada de sort: posições finais do blueprint
    webhook-on   recria as inscrições salvas no snapshot

Gotchas embutidos: PATCH de status SEM `name` zera o nome; cor cinza é 400 em
etapa comum; 142/143 e a etapa de entrada não renomeiam por API (só na tela).
"""

import argparse
import json
import sys
from pathlib import Path

from kommo import KommoClient, KommoError

PIPELINE_LEGADO = "Funil de vendas"
PIPELINE_COMERCIAL = "COMERCIAL"
PIPELINE_TRATAMENTO = "TRATAMENTO"

"""  As 4 etapas legadas que VIRAM etapa canônica: o id é preservado, o lead não se move.  """
RENAMES = [
    {"legacy": "02_LEAD_SEM_RESPOSTA_FOLLOWUP",      "name": "EM QUALIFICAÇÃO",        "color": "#87f2c0", "tmp": 900, "sort": 20},
    {"legacy": "05_AGENDADO_COM_PAGAMENTO",          "name": "AGENDADO",               "color": "#fffeb2", "tmp": 910, "sort": 30},
    {"legacy": "07_NAO_FECHOU_TRATAMENTO",           "name": "EM NEGOCIAÇÃO",          "color": "#ff8f92", "tmp": 920, "sort": 50},
    {"legacy": "10_AGUARDANDO_RETORNO_POS_CONSULTA", "name": "RETORNO PÓS-TRATAMENTO", "color": "#ccc8f9", "tmp": 930, "sort": 60},
]

NOVA_ETAPA = {"name": "COMPARECEU", "color": "#deff81", "sort": 40}

TRATAMENTO_SPEC = {
    "name": PIPELINE_TRATAMENTO,
    "sort": 2,
    "is_unsorted_on": False,
    "statuses": [{"name": "EM TRATAMENTO", "sort": 10, "color": "#87f2c0"}],
}

DEPARA = {
    "01_ENTRADA_LEAD_SEQUENCIA_24H":      {"to": (PIPELINE_COMERCIAL, "EM QUALIFICAÇÃO")},
    "03_LEAD_QUENTE_QUALIFICADO":         {"to": (PIPELINE_COMERCIAL, "EM QUALIFICAÇÃO")},
    "04_AGENDADO_SEM_PAGAMENTO":          {"to": (PIPELINE_COMERCIAL, "AGENDADO")},
    "06_FALTOU_CONSULTA":                 {"to": (PIPELINE_COMERCIAL, "AGENDADO")},
    "08_EM_TRATAMENTO":                   {"to": (PIPELINE_TRATAMENTO, "EM TRATAMENTO")},
    "11_CANCELAMENTO_TRATAMENTO":         {"to": (PIPELINE_TRATAMENTO, 143), "loss": "Cancelamento de tratamento"},
    "12_ALTA_SATISFEITO":                 {"to": (PIPELINE_TRATAMENTO, 142)},
    "13_ALTA_INSATISFEITO":               {"to": (PIPELINE_TRATAMENTO, 142)},
    "14_NAO_PERTURBAR":                   {"to": (PIPELINE_COMERCIAL, 143), "loss": "Não perturbar"},
    "15_ENCAMINHADO_MAGALHAES_ADVOCACIA": {"to": (PIPELINE_COMERCIAL, 143), "loss": "Caso enviado para a franquia"},
    "16_ NAO_DEU_CONTINUIDADE":           {"to": (PIPELINE_COMERCIAL, 143), "loss": "Não deu continuidade ao atendimento"},
    "17_LEAD_MORTO":                      {"to": (PIPELINE_COMERCIAL, 143), "loss": "Lead morto"},
}

"""  142/143 já existem em todo funil; a API ignora o rename, fica pra tela.  """
SYSTEM_RENAMES = {
    PIPELINE_COMERCIAL:  {142: "GANHO / CONCLUÍDO", 143: "PERDIDO"},
    PIPELINE_TRATAMENTO: {142: "ALTA", 143: "TRATAMENTO CANCELADO"},
}


def pipelines(cli):
    return (cli.get("leads/pipelines").get("_embedded") or {}).get("pipelines") or []


def find_pipeline(cli, *names):
    alvo = {n.strip().casefold() for n in names}
    return next((p for p in pipelines(cli) if p["name"].strip().casefold() in alvo), None)


def status_by_name(pl, name):
    return next((s for s in pl["_embedded"]["statuses"] if s["name"].strip().casefold() == name.strip().casefold()), None)


# ------------------------------------------------------------------ snapshot

def step_snapshot(cli, args):
    leads = cli.get_all("leads", "leads", limit=250)
    hooks = ((cli.get("webhooks") or {}).get("_embedded") or {}).get("webhooks") or []
    snap = {
        "leads": [{"id": l["id"], "pipeline_id": l["pipeline_id"], "status_id": l["status_id"],
                   "loss_reason_id": l.get("loss_reason_id")} for l in leads],
        "pipelines": [{"id": p["id"], "name": p["name"], "is_main": p.get("is_main"),
                       "statuses": [{"id": s["id"], "name": s["name"], "sort": s["sort"],
                                     "color": s.get("color"), "type": s.get("type")}
                                    for s in p["_embedded"]["statuses"]]} for p in pipelines(cli)],
        "webhooks": [{"destination": w["destination"], "settings": w.get("settings")} for w in hooks],
    }
    Path(args.snapshot).write_text(json.dumps(snap, ensure_ascii=False, indent=1) + "\n")
    print(f"snapshot: {len(snap['leads'])} leads, {len(snap['pipelines'])} funis, "
          f"{len(snap['webhooks'])} webhooks -> {args.snapshot}")


# ------------------------------------------------------------------ webhooks

def step_webhook_off(cli, args):
    snap = json.loads(Path(args.snapshot).read_text())
    for w in snap["webhooks"]:
        print(f"  - desinscrever {w['destination']} {w['settings']}")
        if args.apply:
            # A Kommo desinscreve por DELETE com corpo; algumas contas só aceitam
            # o destino na query string.
            try:
                cli._request("DELETE", "webhooks", json_body={"destination": w["destination"]})
            except KommoError:
                cli.delete("webhooks?destination=" + w["destination"])
    print("nenhum webhook" if not snap["webhooks"] else "webhooks desligados (recriar com webhook-on)")


def step_webhook_on(cli, args):
    snap = json.loads(Path(args.snapshot).read_text())
    for w in snap["webhooks"]:
        print(f"  + reinscrever {w['destination']} {w['settings']}")
        if args.apply:
            cli.post("webhooks", {"destination": w["destination"], "settings": w["settings"]})


# -------------------------------------------------------------------- rename

def step_rename(cli, args):
    pl = find_pipeline(cli, PIPELINE_LEGADO, PIPELINE_COMERCIAL)
    if not pl:
        sys.exit(f"funil {PIPELINE_LEGADO!r} não encontrado")

    if pl["name"] != PIPELINE_COMERCIAL:
        print(f"  ~ funil {pl['name']!r} -> {PIPELINE_COMERCIAL!r}")
        if args.apply:
            cli.patch(f"leads/pipelines/{pl['id']}", {"name": PIPELINE_COMERCIAL})

    for spec in RENAMES:
        cur = status_by_name(pl, spec["legacy"]) or status_by_name(pl, spec["name"])
        if not cur:
            print(f"  ! etapa {spec['legacy']!r} não encontrada — pulando")
            continue
        print(f"  ~ etapa {cur['name']!r} (id {cur['id']}) -> {spec['name']!r} sort tmp {spec['tmp']}")
        if args.apply:
            # PATCH sem `name` zera o nome; cor vai junto pra não herdar a legada.
            cli.patch(f"leads/pipelines/{pl['id']}/statuses/{cur['id']}",
                      {"name": spec["name"], "color": spec["color"], "sort": spec["tmp"]})

    trat = find_pipeline(cli, PIPELINE_TRATAMENTO)
    if trat:
        print(f"  = funil {PIPELINE_TRATAMENTO!r} já existe (id {trat['id']})")
    else:
        print(f"  + funil {PIPELINE_TRATAMENTO!r}")
        if args.apply:
            cli.post("leads/pipelines", [{
                "name": TRATAMENTO_SPEC["name"],
                "sort": TRATAMENTO_SPEC["sort"],
                "is_main": False,
                "is_unsorted_on": TRATAMENTO_SPEC["is_unsorted_on"],
                "_embedded": {"statuses": TRATAMENTO_SPEC["statuses"]},
            }])


# ------------------------------------------------------------------- migrate

def resolve_targets(cli, lock):
    com = find_pipeline(cli, PIPELINE_COMERCIAL, PIPELINE_LEGADO)
    trat = find_pipeline(cli, PIPELINE_TRATAMENTO)
    pls = {PIPELINE_COMERCIAL: com, PIPELINE_TRATAMENTO: trat}
    out = {}
    for legacy, rule in DEPARA.items():
        pname, target = rule["to"]
        pl = pls.get(pname)
        if not pl:
            sys.exit(f"funil {pname!r} não existe — rode o passo rename antes")
        sid = target if isinstance(target, int) else (status_by_name(pl, target) or {}).get("id")
        if sid is None:
            sys.exit(f"etapa {target!r} não existe em {pname!r}")
        entry = {"pipeline_id": pl["id"], "status_id": sid}
        if rule.get("loss"):
            lid = lock["loss_reasons"].get(rule["loss"])
            if lid is None:
                sys.exit(f"motivo de perda {rule['loss']!r} não está no lock")
            entry["loss_reason_id"] = lid
        if rule.get("set"):
            entry["fields"] = []
            for key, value in rule["set"].items():
                f = lock["fields"].get(key)
                entry["fields"].append({"field_id": f["id"], "values": [{"enum_id": f["enums"][value]}]})
        out[legacy] = entry
    return out, com


def step_migrate(cli, args):
    lock = json.loads(Path(args.lock).read_text())
    targets, com = resolve_targets(cli, lock)
    legacy_ids = {s["name"]: s["id"] for s in com["_embedded"]["statuses"]}

    leads = cli.get_all("leads", "leads", limit=250)
    by_status = {}
    for l in leads:
        by_status.setdefault((l["pipeline_id"], l["status_id"]), []).append(l["id"])

    patches, resumo = [], []
    for legacy, tgt in targets.items():
        sid = legacy_ids.get(legacy)
        if sid is None:
            print(f"  = etapa {legacy!r} já não existe — nada a mover")
            continue
        ids = by_status.get((com["id"], sid), [])
        resumo.append((legacy, len(ids)))
        for lid in ids:
            p = {"id": lid, "pipeline_id": tgt["pipeline_id"], "status_id": tgt["status_id"]}
            if "loss_reason_id" in tgt:
                p["loss_reason_id"] = tgt["loss_reason_id"]
            if "fields" in tgt:
                p["custom_fields_values"] = tgt["fields"]
            patches.append(p)

    for legacy, n in resumo:
        print(f"  → {n:>5} lead(s) de {legacy}")
    print(f"  total a mover: {len(patches)}")
    if args.apply and patches:
        ok, bad = cli.patch_bisect("leads", patches, chunk=50)
        print(f"  aplicados {len(ok)}, erro {len(bad)}")
        for item, err in bad[:10]:
            print(f"    ! lead {item['id']}: {err[:200]}")


# ------------------------------------------------------------------- cleanup

def step_cleanup(cli, args):
    com = find_pipeline(cli, PIPELINE_COMERCIAL, PIPELINE_LEGADO)
    leads = cli.get_all("leads", "leads", limit=250)
    ocupadas = {(l["pipeline_id"], l["status_id"]) for l in leads}
    canonicas = {r["name"] for r in RENAMES} | {NOVA_ETAPA["name"]}

    for s in sorted(com["_embedded"]["statuses"], key=lambda s: s["sort"]):
        if s["id"] in (142, 143) or s.get("type") == 1 or s["name"] in canonicas:
            continue
        n = sum(1 for l in leads if l["pipeline_id"] == com["id"] and l["status_id"] == s["id"])
        if n:
            print(f"  ! etapa {s['name']!r} ainda tem {n} lead(s) — NÃO apagada")
            continue
        print(f"  - apagar etapa {s['name']!r} (id {s['id']})")
        if args.apply:
            try:
                cli.delete(f"leads/pipelines/{com['id']}/statuses/{s['id']}")
            except KommoError as exc:
                print(f"    ! falhou: {exc}")

    if status_by_name(com, NOVA_ETAPA["name"]):
        print(f"  = etapa {NOVA_ETAPA['name']!r} já existe")
    else:
        print(f"  + etapa {NOVA_ETAPA['name']!r}")
        if args.apply:
            cli.post(f"leads/pipelines/{com['id']}/statuses", [NOVA_ETAPA])


# ---------------------------------------------------------------------- sort

def step_sort(cli, args):
    com = find_pipeline(cli, PIPELINE_COMERCIAL)
    for spec in RENAMES:
        cur = status_by_name(com, spec["name"])
        if not cur or cur["sort"] == spec["sort"]:
            continue
        print(f"  ~ {spec['name']!r} sort {cur['sort']} -> {spec['sort']}")
        if args.apply:
            cli.patch(f"leads/pipelines/{com['id']}/statuses/{cur['id']}",
                      {"name": spec["name"], "color": spec["color"], "sort": spec["sort"]})

    if not com.get("is_main"):
        print(f"  ~ {PIPELINE_COMERCIAL!r} vira o funil principal")
        if args.apply:
            cli.patch(f"leads/pipelines/{com['id']}", {"is_main": True})

    print("\n142/143 — a API ignora o rename, fazer NA TELA:")
    for pname, mapa in SYSTEM_RENAMES.items():
        pl = find_pipeline(cli, pname)
        if not pl:
            continue
        for sid, want in mapa.items():
            got = next((s["name"] for s in pl["_embedded"]["statuses"] if s["id"] == sid), None)
            marca = "ok" if (got or "").strip().casefold() == want.casefold() else "RENOMEAR"
            print(f"  · {pname} > {sid}: {got!r} -> {want!r} [{marca}]")


STEPS = {
    "snapshot": step_snapshot, "webhook-off": step_webhook_off, "rename": step_rename,
    "migrate": step_migrate, "cleanup": step_cleanup, "sort": step_sort, "webhook-on": step_webhook_on,
}


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--subdomain", required=True)
    ap.add_argument("--token-file", required=True, type=Path)
    ap.add_argument("--lock", required=True, type=Path)
    ap.add_argument("--snapshot", required=True, type=Path)
    ap.add_argument("--step", required=True, choices=sorted(STEPS))
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--confirm-subdomain")
    args = ap.parse_args()

    sub = args.subdomain.replace(".kommo.com", "").strip("/ ")
    if args.apply and args.confirm_subdomain != sub:
        print("RECUSADO: --apply exige --confirm-subdomain igual a --subdomain.", file=sys.stderr)
        return 2

    cli = KommoClient(sub, args.token_file.read_text().strip(), read_only=not args.apply, verbose=False)
    print(f"[{args.step}] {sub} — modo {'APLICAR' if args.apply else 'DRY-RUN'}")
    STEPS[args.step](cli, args)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
