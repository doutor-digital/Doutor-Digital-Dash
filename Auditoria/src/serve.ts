import { createServer } from "node:http"
import { readFile } from "node:fs/promises"
import { resolve, extname, normalize, dirname } from "node:path"
import { fileURLToPath } from "node:url"

/*
 *  Servidor estático do dashboard.
 *
 *  O Chrome bloqueia ES modules carregados de file:// (CORS de origem opaca), e
 *  o dashboard é módulo — então servir por HTTP é o caminho, não conveniência.
 */

const RAIZ = resolve(dirname(fileURLToPath(import.meta.url)), "../web")
const PORTA = Number.parseInt(process.env["PORTA"] ?? "4123", 10)

const TIPOS: Record< string, string > = {
    ".html": "text/html; charset=utf-8",
    ".js":   "text/javascript; charset=utf-8",
    ".css":  "text/css; charset=utf-8",
    ".json": "application/json; charset=utf-8",
    ".svg":  "image/svg+xml"
}

createServer(async (req, res) => {
    const caminho = (req.url ?? "/").split("?")[0] ?? "/"
    const rel     = normalize(caminho === "/" ? "/index.html" : caminho)

    /*  normalize() já resolve "..", mas o prefixo precisa ser conferido  */
    const arquivo = resolve(RAIZ, `.${rel}`)
    if (!arquivo.startsWith(RAIZ)) {
        res.writeHead(403).end("403")
        return
    }

    try {
        const corpo = await readFile(arquivo)
        res.writeHead(200, {
            "Content-Type":  TIPOS[extname(arquivo)] ?? "application/octet-stream",
            "Cache-Control": "no-store"
        })
        res.end(corpo)
    }
    catch {
        res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" })
        res.end("404 — rode `npm run scrape` para gerar o relatório.")
    }
}).listen(PORTA, () => {
    console.log(`Dashboard em http://localhost:${PORTA}`)
})
