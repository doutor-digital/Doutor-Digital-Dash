#!/usr/bin/env python3
"""Cria os campos `monetary` que a API v4 recusa, pelo endpoint interno da UI da Kommo.

Por que existe: `POST /api/v4/leads/custom_fields` com `type=monetary` devolve
**HTTP 500** em todas as contas das clínicas — com Bearer OU com cookie, com ou sem
grupo, um a um ou em lote, BRL ou USD. Não é limite de plano nem payload errado: a
rota v4 simplesmente não cria esse tipo nessas contas. A TELA cria porque usa outra
rota, descoberta em 22/08/2026 interceptando o XHR do editor "Campos e grupos":

    POST /ajax/settings/custom_fields/        (form-urlencoded, cookie de sessão)
        action=apply_changes
        cf[add][0][element_type]=2          # 2 = lead
        cf[add][0][type_id]=23              # 23 = Moeda
        cf[add][0][name]=<nome>
        cf[add][0][code]=<code ou vazio>    # code SÓ entra na criação (PATCH v4 = 400 OnlyNull)
        cf[add][0][currency]=BRL
        cf[add][0][settings][currency]=BRL
        ...

O campo nasce SEM grupo. Quem coloca no grupo e define a ordem é:

    PATCH /ajax/v4/leads/custom_fields/groups/<group_id>   {"fields":[ids na ordem]}

Este script faz os dois passos e, no fim, acerta `sort` por `PATCH /api/v4` (esse
funciona em campo que já existe) para que a ordem bata com a matriz.

Cookie: exportar do navegador logado na conta (browser-harness:
`cdp("Network.getCookies", urls=["https://<sub>.kommo.com/"])`) e salvar em arquivo
no formato `nome=valor; nome=valor`. Precisa conter `session_id` e `csrf_token`.

Uso:
    python3 criar_monetary.py --subdomain bebedourosp \
        --blueprint blueprint.itz-2026-08-22.json \
        --token-file token_bebedouro.txt --cookie-file cookie_bebedouro.txt \
        --lock blueprint.bebedouro.lock.json          # dry-run
    ... --apply --confirm-subdomain bebedourosp

Depois de rodar, reaplique o blueprint para grudar os `required_statuses` dos
monetary (o apply é idempotente e agora acha os campos):
    python3 apply_blueprint.py ... --apply --confirm-subdomain <sub>
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

from kommo import KommoClient

TYPE_ID_MOEDA = 23
ELEMENT_TYPE_LEAD = 2
UA = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140 Safari/537.36"


class UiClient:
    """Fala com as rotas internas da UI usando o cookie da sessão do navegador."""

    def __init__(self, sub: str, cookie: str):
        self.base = f"https://{sub}.kommo.com"
        self.cookie = cookie
        m = re.search(r"csrf_token=([^;]+)", cookie)
        if not m:
            raise SystemExit("RECUSADO: o cookie não tem csrf_token — exporte de novo do navegador logado.")
        self.csrf = m.group(1)

    def _headers(self, content_type: str) -> dict[str, str]:
        return {
            "Cookie": self.cookie,
            "Content-Type": content_type,
            "X-Requested-With": "XMLHttpRequest",
            "X-CSRF-Token": self.csrf,
            "Origin": self.base,
            "Referer": f"{self.base}/leads/pipeline/",
            "Accept": "application/json, text/javascript, */*; q=0.01",
            "User-Agent": UA,
        }

    def criar_monetary(self, nome: str, code: str | None, currency: str) -> int:
        campos = [
            ("action", "apply_changes"),
            ("cf[add][0][element_type]", str(ELEMENT_TYPE_LEAD)),
            ("cf[add][0][sortable]", "true"),
            ("cf[add][0][groupable]", "true"),
            ("cf[add][0][predefined]", "false"),
            ("cf[add][0][type_id]", str(TYPE_ID_MOEDA)),
            ("cf[add][0][name]", nome),
            ("cf[add][0][code]", code or ""),
            ("cf[add][0][disabled]", ""),
            ("cf[add][0][currency]", currency),
            ("cf[add][0][required]", "false"),
            ("cf[add][0][settings][currency]", currency),
            ("cf[add][0][settings][formula]", ""),
            ("cf[add][0][pipeline_id]", "0"),
        ]
        req = urllib.request.Request(
            f"{self.base}/ajax/settings/custom_fields/",
            data=urllib.parse.urlencode(campos).encode(),
            headers=self._headers("application/x-www-form-urlencoded; charset=UTF-8"),
            method="POST",
        )
        with urllib.request.urlopen(req) as r:
            corpo = json.loads(r.read())
        ids = (corpo.get("response") or {}).get("id") or []
        if not ids:
            raise RuntimeError(f"resposta sem id: {corpo}")
        return int(ids[0])

    def ordenar_grupo(self, group_id: str, ids: list[int]) -> None:
        req = urllib.request.Request(
            f"{self.base}/ajax/v4/leads/custom_fields/groups/{group_id}",
            data=json.dumps({"fields": ids}).encode(),
            headers=self._headers("application/json"),
            method="PATCH",
        )
        urllib.request.urlopen(req).read()


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--blueprint", required=True, type=Path)
    ap.add_argument("--subdomain", required=True)
    ap.add_argument("--token-file", required=True, type=Path, help="token da API v4 (leitura + PATCH de sort)")
    ap.add_argument("--cookie-file", required=True, type=Path, help="cookie da sessão do navegador")
    ap.add_argument("--lock", required=True, type=Path, help="lock do apply_blueprint (dá os group_id)")
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--confirm-subdomain")
    args = ap.parse_args()

    sub = args.subdomain.replace(".kommo.com", "").strip("/ ")
    if sub == "attivacorpoementeitz":
        print("RECUSADO: Imperatriz é a MATRIZ, nunca destino.", file=sys.stderr)
        return 2
    if args.apply and args.confirm_subdomain != sub:
        print("RECUSADO: --apply exige --confirm-subdomain igual a --subdomain.", file=sys.stderr)
        return 2

    bp = json.loads(args.blueprint.read_text())
    lock = json.loads(args.lock.read_text())
    cli = KommoClient(sub, args.token_file.read_text().strip(), read_only=not args.apply)
    ui = UiClient(sub, args.cookie_file.read_text().strip())

    vivos = cli.get_all("leads/custom_fields", "custom_fields")
    por_nome = {f["name"]: f for f in vivos}

    faltando = [f for f in bp["fields"] if f["type"] == "monetary" and f["name"] not in por_nome]
    print(f"monetary no blueprint: {sum(1 for f in bp['fields'] if f['type']=='monetary')} — "
          f"faltando em {sub}: {len(faltando)}")
    for f in faltando:
        print(f"  + {f['name']!r} (grupo {f['group']}, code {f.get('code')})")
    if not args.apply:
        print("\nDRY-RUN — nada foi escrito. Rode com --apply --confirm-subdomain para criar.")
        return 0

    for f in faltando:
        fid = ui.criar_monetary(f["name"], f.get("code"), f.get("currency", "BRL"))
        print(f"  criado {f['name']!r} -> id {fid}")
        por_nome[f["name"]] = {"id": fid, "name": f["name"], "type": "monetary"}
        time.sleep(1.0)

    # grupo + ordem: manda a lista COMPLETA do grupo, na ordem da matriz
    print("\nreordenando os grupos na ordem da matriz")
    usados: set[int] = set()
    patches = []
    base = 500
    for chave, grupo in lock["groups"].items():
        ids = []
        for spec in [x for x in bp["fields"] if x["group"] == chave]:
            atual = por_nome.get(spec["name"])
            if not atual:
                print(f"  ! {spec['name']!r} não existe na conta — grupo {chave} fica incompleto")
                continue
            if atual["id"] in usados:
                continue
            usados.add(atual["id"])
            ids.append(atual["id"])
        ui.ordenar_grupo(grupo["id"], ids)
        # o PATCH de grupo define a pertinência, mas NÃO reescreve `sort` — e o
        # export/auditoria ordena por sort. Então grava sort explícito, sem empate.
        for i, fid in enumerate(ids):
            patches.append({"id": fid, "sort": base + i * 2})
        base += 500
        print(f"  {chave:14} {len(ids)} campos")

    tipo_por_nome = {f["name"]: f["type"] for f in bp["fields"]}
    nome_por_id = {v["id"]: k for k, v in por_nome.items()}
    for p in patches:
        if tipo_por_nome.get(nome_por_id.get(p["id"], "")) == "monetary":
            p["currency"] = "BRL"  # PATCH de monetary sem currency = 400 FieldMissing

    ok, ruins = cli.patch_bisect("leads/custom_fields", patches)
    print(f"sort gravado em {len(ok)} campos ({len(ruins)} com erro)")
    for item, err in ruins:
        print(f"  ! sort id={item['id']}: {err}")

    print("\nAGORA rode o apply_blueprint de novo para grudar os required_statuses dos monetary.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
