"""Cliente mínimo da API v4 da Kommo, com os gotchas da conta do cliente embutidos.

Gotchas conhecidos (aprendidos na migração da unidade 15 / Imperatriz):
  * paginar SEMPRE com order[id]=asc — ordenar por updated_at pula registro
    quando a conta está sendo mutada em massa;
  * campo do tipo `monetary` exige `currency` no POST, senão 400 FieldMissing;
  * select/multiselect gravam por enum_id, nunca pelo texto;
  * status 142/143 (ganho/perdido) e a etapa de entrada são de sistema: não dá
    para renomear nem apagar por API depois que o pipeline existe;
  * PATCH em lote morre inteiro se um único item for inválido — por isso o
    `patch_bisect`.

Sem dependência além de `requests`.
"""

from __future__ import annotations

import json
import time
from typing import Any, Iterable

import requests

RATE_LIMIT_SLEEP = 0.16  # Kommo tolera ~7 req/s; 0.16s dá margem.


class KommoError(RuntimeError):
    def __init__(self, method: str, url: str, status: int, body: str):
        super().__init__(f"{method} {url} -> HTTP {status}: {body[:600]}")
        self.method = method
        self.url = url
        self.status = status
        self.body = body


class KommoClient:
    def __init__(self, subdomain: str, token: str, *, read_only: bool = False, verbose: bool = True):
        self.subdomain = subdomain.replace(".kommo.com", "").strip("/ ")
        self.base = f"https://{self.subdomain}.kommo.com/api/v4"
        self.token = token.strip()
        self.read_only = read_only
        self.verbose = verbose
        self.session = requests.Session()
        self.session.headers.update(
            {
                "Authorization": f"Bearer {self.token}",
                "Content-Type": "application/json",
                # O CRM web da franquia quebra sem Accept-Language; a Kommo não
                # exige, mas mandamos por consistência com o resto do projeto.
                "Accept-Language": "pt-BR,pt;q=0.9",
            }
        )

    # ------------------------------------------------------------------ core

    def _request(self, method: str, path: str, *, params: dict | None = None, json_body: Any = None) -> Any:
        if method != "GET" and self.read_only:
            raise PermissionError(
                f"cliente em modo somente-leitura: {method} {path} bloqueado "
                f"(subdomínio {self.subdomain})"
            )
        url = path if path.startswith("http") else f"{self.base}/{path.lstrip('/')}"
        for attempt in range(5):
            resp = self.session.request(method, url, params=params, data=json.dumps(json_body) if json_body is not None else None, timeout=60)
            time.sleep(RATE_LIMIT_SLEEP)
            if resp.status_code == 429 or resp.status_code >= 500:
                wait = 2 ** attempt
                if self.verbose:
                    print(f"    ! HTTP {resp.status_code} em {method} {url} — retry em {wait}s")
                time.sleep(wait)
                continue
            if resp.status_code == 204 or not resp.content:
                return None
            if resp.status_code >= 400:
                raise KommoError(method, url, resp.status_code, resp.text)
            try:
                return resp.json()
            except ValueError:
                return None
        raise KommoError(method, url, resp.status_code, resp.text)

    def get(self, path: str, **params) -> Any:
        return self._request("GET", path, params=params or None)

    def post(self, path: str, body: Any) -> Any:
        return self._request("POST", path, json_body=body)

    def patch(self, path: str, body: Any) -> Any:
        return self._request("PATCH", path, json_body=body)

    def delete(self, path: str) -> Any:
        return self._request("DELETE", path)

    # ------------------------------------------------------------ paginação

    def get_all(self, path: str, embedded_key: str, *, limit: int = 250, **params) -> list[dict]:
        """Pagina um endpoint de coleção. Ordena por id asc (ver docstring do módulo)."""
        out: list[dict] = []
        page = 1
        while True:
            data = self.get(path, page=page, limit=limit, **{"order[id]": "asc"}, **params)
            if not data:
                break
            items = (data.get("_embedded") or {}).get(embedded_key) or []
            out.extend(items)
            if len(items) < limit or not (data.get("_links") or {}).get("next"):
                break
            page += 1
        return out

    # -------------------------------------------------------------- helpers

    def patch_bisect(self, path: str, items: list[dict], *, chunk: int = 50) -> tuple[list[dict], list[tuple[dict, str]]]:
        """PATCH em lote tolerante a item ruim.

        A Kommo derruba o lote inteiro se um item for inválido ("Lead not found",
        enum inexistente…). Aqui o lote é bissecionado até isolar os culpados.
        Retorna (aplicados, [(item, erro)]).
        """
        ok: list[dict] = []
        bad: list[tuple[dict, str]] = []

        def flush(batch: list[dict]) -> None:
            if not batch:
                return
            try:
                self.patch(path, batch)
                ok.extend(batch)
            except KommoError as exc:
                if len(batch) == 1:
                    bad.append((batch[0], str(exc)))
                    return
                mid = len(batch) // 2
                flush(batch[:mid])
                flush(batch[mid:])

        for i in range(0, len(items), chunk):
            flush(items[i : i + chunk])
        return ok, bad


def chunked(seq: Iterable, size: int) -> Iterable[list]:
    buf: list = []
    for item in seq:
        buf.append(item)
        if len(buf) >= size:
            yield buf
            buf = []
    if buf:
        yield buf
