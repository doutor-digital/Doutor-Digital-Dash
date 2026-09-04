"""Fase 3 (B3 do playbook) — backfill legado → canônico, idempotente.

A fonte é um BACKUP (dump de `GET /leads` com `custom_fields_values` tirado antes
de mexer), não a conta viva: em Canaã os campos legados foram apagados no meio da
replicação e o backup virou a única cópia. Funciona igual quando o legado ainda
existe — o dump de hoje serve de fonte.

Idempotente por construção: só escreve onde o canônico está VAZIO. Rodar de novo
converge para 0 gravações; é assim que se sabe que terminou.

Enum é resolvido por NOME (o enum_id muda de conta para conta, e um PATCH com
`enums` reatribui os ids). Nome que não existe no canônico entra em ENUM_ALIAS ou
é reportado como não migrado — nunca é gravado às cegas.

    python3 fase3_backfill.py --subdomain <sub> --token-file token.txt \
        --lock blueprint.<sub>.lock.json --backup backup_campos_antigos_<sub>.json
    ... --apply --confirm-subdomain <sub>
"""

import argparse
import json
import sys
from pathlib import Path

from kommo import KommoClient

"""  legado (id do campo no backup) -> chave do campo canônico no lock.  """
DEPARA = {
    2425056: "comercial::origem#1",
    2425058: "comercial::valor-do-tratamento#1",
    2425060: "comercial::motivo-do-nao-agendamento#1",
    2425062: "comercial::motivo-de-nao-fechamento-do-tratamento#1",
    2425066: "comercial::cidade#1",
    2425068: "comercial::bairro#1",
    2425070: "comercial::sexo#1",
    2425072: "comercial::profissao#1",
    2425074: "comercial::data-de-nascimento#1",
    2425076: "comercial::data-da-consulta#1",
    2425078: "comercial::data-do-tratamento-iniciado#1",
    2425084: "comercial::qualificacao-quente-morno-frio#1",
    2426686: "comercial::tratamento-indicado#1",
    2426774: "comercial::estado#1",
}

"""  Nomes de opção que a unidade escrevia diferente do canônico da Imperatriz.  """
ENUM_ALIAS = {
    "comercial::origem#1": {
        "WhatsApp anúncio": "Meta-WhatsApp",
        "Faxada": "Fachada",
    },
}

ENUM_TYPES = {"select", "multiselect", "radiobutton"}


def preenchido(valores) -> bool:
    return any(v.get("value") not in (None, "", 0, "0") for v in (valores or []))


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--subdomain", required=True)
    ap.add_argument("--token-file", required=True, type=Path)
    ap.add_argument("--lock", required=True, type=Path)
    ap.add_argument("--backup", required=True, type=Path, help="dump de leads com custom_fields_values")
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--confirm-subdomain")
    ap.add_argument(
        "--depara",
        type=Path,
        help="JSON {legacy_field_id: chave_canonica_no_lock} da unidade. "
        "Sem isto usa o mapa embutido (Canaã).",
    )
    ap.add_argument(
        "--enum-alias",
        type=Path,
        help="JSON {chave_canonica: {nome_legado: nome_canonico}} de opções que a unidade "
        "escrevia diferente. Sem isto usa o mapa embutido.",
    )
    args = ap.parse_args()

    """  Mapas por unidade: o embutido é só o default histórico da Canaã.  """
    DEPARA = globals()["DEPARA"]
    ENUM_ALIAS = globals()["ENUM_ALIAS"]
    if args.depara:
        DEPARA = {int(k): v for k, v in json.loads(args.depara.read_text()).items()}
    if args.enum_alias:
        ENUM_ALIAS = json.loads(args.enum_alias.read_text())

    sub = args.subdomain.replace(".kommo.com", "").strip("/ ")
    if args.apply and args.confirm_subdomain != sub:
        print("RECUSADO: --apply exige --confirm-subdomain igual a --subdomain.", file=sys.stderr)
        return 2

    lock = json.loads(args.lock.read_text())["fields"]
    cli = KommoClient(sub, args.token_file.read_text().strip(), read_only=not args.apply, verbose=False)
    print(f"backfill em {sub} — modo {'APLICAR' if args.apply else 'DRY-RUN'}")

    """  O canônico pode não existir (monetary travado pelo plano): reporta e segue.  """
    destino, ausentes = {}, []
    for legacy_id, key in DEPARA.items():
        alvo = lock.get(key)
        if alvo is None:
            ausentes.append(key)
            continue
        destino[legacy_id] = alvo

    atual = {l["id"]: l for l in cli.get_all("leads", "leads", limit=250)}
    """  Aceita o dump cru (lista) ou o envelopado {"taken_at":..., "leads":[...]}.  """
    backup = json.loads(args.backup.read_text())
    if isinstance(backup, dict):
        backup = backup.get("leads") or []

    patches, escritos, pulados, sem_enum = [], {}, {}, {}
    for old in backup:
        lead = atual.get(old["id"])
        if lead is None:
            continue
        tem = {cf["field_id"]: cf.get("values") for cf in (lead.get("custom_fields_values") or [])}
        novos = []
        for cf in (old.get("custom_fields_values") or []):
            alvo = destino.get(cf["field_id"])
            if alvo is None or not preenchido(cf.get("values")):
                continue
            if preenchido(tem.get(alvo["id"])):
                pulados[alvo["name"]] = pulados.get(alvo["name"], 0) + 1
                continue
            if alvo["type"] in ENUM_TYPES:
                enums = alvo.get("enums") or {}
                alias = ENUM_ALIAS.get(DEPARA[cf["field_id"]], {})
                ids = []
                for v in cf["values"]:
                    nome = alias.get(v["value"], v["value"])
                    eid = enums.get(nome)
                    if eid is None:
                        sem_enum.setdefault(alvo["name"], set()).add(v["value"])
                        continue
                    ids.append({"enum_id": eid})
                if not ids:
                    continue
                novos.append({"field_id": alvo["id"], "values": ids})
            else:
                novos.append({"field_id": alvo["id"],
                              "values": [{"value": v["value"]} for v in cf["values"]]})
            escritos[alvo["name"]] = escritos.get(alvo["name"], 0) + 1
        if novos:
            patches.append({"id": lead["id"], "custom_fields_values": novos})

    print(f"\n  leads a tocar: {len(patches)}")
    for nome, n in sorted(escritos.items(), key=lambda kv: -kv[1]):
        print(f"    + {n:>5}  {nome}")
    for nome, n in sorted(pulados.items(), key=lambda kv: -kv[1]):
        print(f"    = {n:>5}  {nome} (canônico já preenchido)")
    for key in ausentes:
        print(f"    ! canônico ausente no lock: {key} — backfill PENDENTE")
    for nome, vals in sem_enum.items():
        print(f"    ! opção sem correspondente em {nome}: {sorted(vals)}")

    if args.apply and patches:
        ok, bad = cli.patch_bisect("leads", patches, chunk=50)
        print(f"\n  aplicados {len(ok)}, erro {len(bad)}")
        for item, err in bad[:10]:
            print(f"    ! lead {item['id']}: {err[:200]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
