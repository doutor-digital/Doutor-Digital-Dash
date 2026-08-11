/*
 *  Cliente HTTP da aplicação de origem.
 *
 *  Autenticação é por cookie de sessão do CodeIgniter (`sessions`, HttpOnly).
 *  O valor vem da env DH_SESSION — leia-o do Chrome já logado com:
 *
 *      browser-harness <<'PY'
 *      cs = cdp("Network.getCookies", urls=["https://app.doutorhernia.com.br"])["cookies"]
 *      print(next(c["value"] for c in cs if c["name"] == "sessions"))
 *      PY
 */

export const BASE = "https://app.doutorhernia.com.br"

export class SessaoExpiradaError extends Error {
    constructor() {
        super("Sessão expirada ou inválida — atualize DH_SESSION com o cookie do Chrome logado.")
        this.name = "SessaoExpiradaError"
    }
}

export interface ClientOpts {
    sessao:        string
    /*  Intervalo mínimo entre requisições, para não martelar o servidor de produção  */
    intervaloMs?:  number
    tentativas?:   number
}

export class Client {
    private readonly sessao:      string
    private readonly intervaloMs: number
    private readonly tentativas:  number
    private ultimaEm = 0

    constructor(opts: ClientOpts) {
        this.sessao      = opts.sessao
        this.intervaloMs = opts.intervaloMs ?? 350
        this.tentativas  = opts.tentativas  ?? 3
    }

    private async aguardarVez(): Promise< void > {
        const agora   = Date.now()
        const espera  = this.ultimaEm + this.intervaloMs - agora
        if (espera > 0)
            await new Promise((r) => setTimeout(r, espera))
        this.ultimaEm = Date.now()
    }

    private async requisitar(url: string, init: RequestInit): Promise< string > {
        let ultimoErro: unknown = null
        for (let i = 0; i < this.tentativas; i++) {
            await this.aguardarVez()
            try {
                const r = await fetch(url, {
                    ...init,
                    redirect: "follow",
                    headers: {
                        ...(init.headers ?? {}),
                        "Cookie":     `sessions=${this.sessao}`,
                        "User-Agent": "auditoria-prontuarios/1.0 (interno)"
                    }
                })
                const texto = await r.text()

                /*  O CI devolve 200 com a tela de login quando a sessão morre  */
                if (texto.includes("name=\"password\"") && texto.includes("form-signin"))
                    throw new SessaoExpiradaError()

                if (r.status >= 500) {
                    ultimoErro = new Error(`HTTP ${r.status} em ${url}`)
                    continue
                }
                return texto
            }
            catch (e) {
                if (e instanceof SessaoExpiradaError) throw e
                ultimoErro = e

                /*  backoff simples: 400ms, 800ms, 1600ms  */
                await new Promise((r) => setTimeout(r, 400 * (2 ** i)))
            }
        }
        throw ultimoErro instanceof Error ? ultimoErro : new Error(`Falha em ${url}`)
    }

    async get(caminho: string): Promise< string > {
        return this.requisitar(`${BASE}${caminho}`, { method: "GET" })
    }

    async post(caminho: string, campos: Record< string, string >): Promise< string > {
        return this.requisitar(`${BASE}${caminho}`, {
            method:  "POST",
            body:    new URLSearchParams(campos).toString(),
            headers: { "Content-Type": "application/x-www-form-urlencoded" }
        })
    }

    /*
     *  Aplica o filtro da listagem. O filtro é guardado na sessão do servidor e
     *  vale para as chamadas seguintes de /atendimentos/listagem/{offset}.
     *
     *  Atenção: `created` NÃO pode ir vazio. Com string vazia o controller ignora
     *  o filtro inteiro e devolve todas as unidades — foi o que mascarou o
     *  primeiro teste. O formato aceito é "DD/MM/AAAA - DD/MM/AAAA".
     */
    async filtrar(idCompany: string, periodo: string, idStaff = "0"): Promise< void > {
        await this.post("/atendimentos/filtrar", {
            id_company:          idCompany,
            id_staff:            idStaff,
            created:             periodo,
            keyword_attendance:  ""
        })
    }
}
