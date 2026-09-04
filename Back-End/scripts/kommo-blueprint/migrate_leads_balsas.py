#!/usr/bin/env python3
"""Migra os leads de Balsas do funil legado para o canônico (COMERCIAL/TRATAMENTO).

De-para aprovado pelo dono em 2026-08-18 (gotcha #10 do playbook: etapa de
fechamento/no-show nunca se decide sozinho).

Segurança:
  * dry-run por padrão — só escreve com --apply;
  * grava um arquivo de ROLLBACK (lead id -> pipeline/status/motivo de origem)
    ANTES de qualquer escrita;
  * --limit N faz canário (migra só N leads da primeira etapa pendente);
  * order[id]=asc na paginação e patch_bisect no 400 (via kommo.py).

Uso:
    python3 migrate_leads_balsas.py --token-file token_balsas.txt            # dry-run
    python3 migrate_leads_balsas.py --token-file token_balsas.txt --apply --limit 5
    python3 migrate_leads_balsas.py --token-file token_balsas.txt --apply
"""

from __future__ import annotations

import argparse
import json
import time
from datetime import datetime, timezone
from pathlib import Path

from kommo import KommoClient

SUBDOMAIN = "doutorherniabalsas"
LEGADO = 13843399
COMERCIAL = 14304411
TRATAMENTO = 14304415

# etapa legada -> (pipeline destino, status destino, motivo de perda | None)
DE_PARA: dict[int, tuple[int, int, str | None]] = {
    106820083: (COMERCIAL,  110469423, None),   # Incoming leads            -> Incoming leads
    106820283: (COMERCIAL,  110469427, None),   # 01_ENTRADA_SEQUENCIA_24H  -> EM QUALIFICAÇÃO
    106820287: (COMERCIAL,  110469427, None),   # 02_SEM_RESPOSTA_FOLLOWUP  -> EM QUALIFICAÇÃO
    106820291: (COMERCIAL,  110469427, None),   # 03_LEAD_QUENTE_QUALIFICADO-> EM QUALIFICAÇÃO
    106820295: (COMERCIAL,  110469431, None),   # 04_AGENDADO_SEM_PAGAMENTO -> AGENDADO
    106820299: (COMERCIAL,  110469431, None),   # 05_AGENDADO_COM_PAGAMENTO -> AGENDADO
    106820579: (COMERCIAL,  110469431, None),   # 06_FALTOU_CONSULTA        -> AGENDADO      [aprovado]
    106820587: (COMERCIAL,  110469439, None),   # 07_NAO_FECHOU_TRATAMENTO  -> EM NEGOCIAÇÃO [aprovado]
    106820595: (TRATAMENTO, 110469451, None),   # 08_EM_TRATAMENTO          -> EM TRATAMENTO
    106820599: (COMERCIAL,  110469439, None),   # 09_AGUARDANDO_RETORNO     -> EM NEGOCIAÇÃO
    106820603: (TRATAMENTO, 143, "Cancelamento de tratamento"),          # 10_CANCELAMENTO
    106820607: (TRATAMENTO, 142, None),                                   # 11_ALTA_SATISFEITO   -> ALTA
    106820611: (TRATAMENTO, 142, None),                                   # 12_ALTA_INSATISFEITO -> ALTA [aprovado]
    106820615: (COMERCIAL,  143, "Não perturbar"),                        # 13_NAO_PERTURBAR
    106820619: (COMERCIAL,  143, "Caso enviado para a franquia"),         # 14_ENCAMINHADO_MAGALHAES
    106820623: (COMERCIAL,  143, "Não deu continuidade ao atendimento"),  # 15_NAO_DEU_CONTINUIDADE
    106820627: (COMERCIAL,  143, "Mora em outra cidade"),                 # 16_MORA_FORA
    107121223: (COMERCIAL,  143, "Lead morto"),                           # 17_LEAD_MORTO
    142:       (COMERCIAL,  142, None),                                   # 18_FECHOU_TRATAMENTO -> GANHO
    143:       (COMERCIAL,  143, None),                                   # 19_TRATAMENTO_PERDIDO-> PERDIDO
}

PATCH_CHUNK = 50


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--token-file", required=True, type=Path)
    ap.add_argument("--apply", action="store_true", help="sem isto, só imprime o plano")
    ap.add_argument("--limit", type=int, help="canário: migra no máximo N leads no total")
    ap.add_argument("--rollback-out", type=Path, default=Path("rollback.balsas-leads.json"))
    args = ap.parse_args()

    cli = KommoClient(SUBDOMAIN, args.token_file.read_text().strip(), read_only=not args.apply)

    motivos = {r["name"].strip(): r["id"] for r in cli.get_all("leads/loss_reasons", "loss_reasons")}
    faltando = {m for _, _, m in DE_PARA.values() if m} - set(motivos)
    if faltando:
        print(f"RECUSADO: motivo(s) de perda inexistente(s) na conta: {sorted(faltando)}")
        return 2

    print(f"lendo leads do funil legado {LEGADO}...")
    # get_all já pagina com order[id]=asc
    leads = [l for l in cli.get_all("leads", "leads") if l["pipeline_id"] == LEGADO]
    print(f"  {len(leads)} leads no funil legado")

    # ROLLBACK primeiro: de onde cada lead saiu. Sem isto não se escreve nada.
    rollback = [
        {
            "id": l["id"],
            "pipeline_id": l["pipeline_id"],
            "status_id": l["status_id"],
            "loss_reason_id": l.get("loss_reason_id"),
        }
        for l in leads
    ]

    patches: list[dict] = []
    resumo: dict[str, int] = {}
    sem_mapa: list[int] = []
    for l in leads:
        alvo = DE_PARA.get(l["status_id"])
        if alvo is None:
            sem_mapa.append(l["status_id"])
            continue
        pid, sid, motivo = alvo
        item = {"id": l["id"], "pipeline_id": pid, "status_id": sid}
        if motivo:
            item["loss_reason_id"] = motivos[motivo]
        patches.append(item)
        chave = f"{l['status_id']} -> {pid}/{sid}" + (f" ({motivo})" if motivo else "")
        resumo[chave] = resumo.get(chave, 0) + 1

    if sem_mapa:
        print(f"RECUSADO: {len(sem_mapa)} lead(s) em etapa sem de-para: {sorted(set(sem_mapa))}")
        return 2

    print(f"\nPLANO ({len(patches)} leads):")
    for chave, n in sorted(resumo.items(), key=lambda x: -x[1]):
        print(f"  {n:>5}  {chave}")

    if args.limit:
        patches = patches[: args.limit]
        print(f"\nCANÁRIO: limitando a {len(patches)} lead(s)")

    if not args.apply:
        print("\nDRY-RUN — nada escrito. Use --apply para valer.")
        return 0

    args.rollback_out.write_text(
        json.dumps(
            {"taken_at": datetime.now(timezone.utc).isoformat(), "subdomain": SUBDOMAIN, "leads": rollback},
            ensure_ascii=False,
        )
        + "\n"
    )
    print(f"\nrollback gravado em {args.rollback_out} ({len(rollback)} leads)")

    ok_total = 0
    ruins: list[tuple[dict, str]] = []
    for i in range(0, len(patches), PATCH_CHUNK):
        lote = patches[i : i + PATCH_CHUNK]
        ok, bad = cli.patch_bisect("leads", lote, chunk=PATCH_CHUNK)
        ok_total += len(ok)
        ruins.extend(bad)
        print(f"  {ok_total}/{len(patches)} migrados", end="\r", flush=True)
        time.sleep(0.25)

    print(f"\n{ok_total} migrados, {len(ruins)} com erro")
    for item, err in ruins[:10]:
        print(f"  ! lead {item['id']}: {err}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
