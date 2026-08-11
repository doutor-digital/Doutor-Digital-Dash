import { writeFile, mkdir } from "node:fs/promises"
import { resolve, dirname } from "node:path"
import { fileURLToPath } from "node:url"
import { Client, SessaoExpiradaError } from "./client.js"
import { parseListagem, parseProntuario } from "./parse.js"
import { auditar, catalogoRegras } from "./audit.js"
import type { Atendimento, Prontuario, ProntuarioAuditado, Relatorio, Severidade } from "./types.js"

/*
 *  Varre a listagem de atendimentos de uma unidade, abre cada acompanhamento,
 *  extrai o prontuário e roda as regras de auditoria.
 *
 *      DH_SESSION=<cookie> node dist/scrape.js
 *      DH_SESSION=<cookie> DH_PERIODO="01/07/2026 - 31/08/2026" node dist/scrape.js
 */

const RAIZ     = resolve(dirname(fileURLToPath(import.meta.url)), "..")
const UNIDADES: Record< string, string > = {
    "333": "AÇAILÂNDIA - MA",
    "93":  "ARAGUAÍNA - TO",
    "132": "BALSAS - MA",
    "166": "CANAÃ DOS CARAJÁS - PA",
    "133": "IMPERATRIZ - MA",
    "114": "MARABÁ - PA",
    "131": "PARAUAPEBAS - PA",
    "320": "PORTO NACIONAL - TO"
}

const periodoPadrao = (): string => {
    const hoje    = new Date()
    const ano     = hoje.getFullYear()
    const mes     = String(hoje.getMonth() + 1).padStart(2, "0")
    const ultimo  = new Date(ano, hoje.getMonth() + 1, 0).getDate()
    return `01/${mes}/${ano} - ${ultimo}/${mes}/${ano}`
}

async function main(): Promise< void > {
    const sessao = process.env["DH_SESSION"]
    if (!sessao) {
        console.error("Faltou DH_SESSION. Leia o cookie do Chrome logado:\n\n  browser-harness <<'PY'\n  cs = cdp(\"Network.getCookies\", urls=[\"https://app.doutorhernia.com.br\"])[\"cookies\"]\n  print(next(c[\"value\"] for c in cs if c[\"name\"] == \"sessions\"))\n  PY\n")
        process.exit(1)
    }

    const idCompany = process.env["DH_UNIDADE"] ?? "133"
    const periodo   = process.env["DH_PERIODO"] ?? periodoPadrao()
    const unidade   = UNIDADES[idCompany] ?? idCompany
    const client    = new Client({ sessao })

    console.log(`Unidade: ${unidade}  ·  Período: ${periodo}`)
    await client.filtrar(idCompany, periodo)

    /*  Página 1 também informa o total, que define quantos offsets buscar  */
    const primeira = parseListagem(await client.get("/atendimentos/listagem/0"))
    const total    = primeira.total
    const porPag   = Math.max(primeira.atendimentos.length, 1)

    const lista: Atendimento[] = [ ...primeira.atendimentos ]
    for (let offset = porPag; offset < total; offset += porPag) {
        const pag = parseListagem(await client.get(`/atendimentos/listagem/${offset}`))
        lista.push(...pag.atendimentos)
        console.log(`  listagem ${lista.length}/${total}`)
    }

    /*  A listagem é global se o filtro falhar; conferir a unidade evita auditar a base inteira  */
    const alvo = lista.filter((a) => a.unidade.trim() === unidade)
    if (alvo.length !== lista.length)
        console.warn(`  atenção: ${lista.length - alvo.length} linha(s) de outra unidade descartadas`)

    /*
     *  Cada atendimento do mesmo tratamento abre a MESMA ficha. Agrupa-se por
     *  tratamento e mantém-se a leitura mais recente como canônica; sem isso um
     *  achado do prontuário seria contado uma vez por sessão.
     */
    const porChave = new Map< string, Prontuario >()
    for (const [ i, at ] of alvo.entries()) {
        try {
            const html = await client.get(`/atendimentos/acompanhar/${at.id}`)
            const p    = parseProntuario(html, at)
            const ja   = porChave.get(p.chave)

            if (!ja) porChave.set(p.chave, p)
            else {
                ja.atendimentos.push(at)

                /*  A listagem vem do mais recente para o mais antigo  */
                if (ja.evolucoes.length < p.evolucoes.length) {
                    p.atendimentos = ja.atendimentos
                    p.principal    = ja.principal
                    porChave.set(p.chave, p)
                }
            }
        }
        catch (e) {
            if (e instanceof SessaoExpiradaError) throw e
            console.warn(`  falha em #${at.id}: ${(e as Error).message}`)
        }
        if ((i + 1) % 10 === 0 || i + 1 === alvo.length)
            console.log(`  fichas ${i + 1}/${alvo.length}`)
    }

    const auditados: ProntuarioAuditado[] = []
    for (const p of porChave.values()) {
        p.atendimentos.sort((a, b) => b.id - a.id)
        auditados.push(auditar(p))
    }

    auditados.sort((a, b) => b.escore - a.escore || b.principal.id - a.principal.id)

    const contaPorRegra = new Map< string, number >()
    for (const p of auditados)
        for (const a of p.achados)
            contaPorRegra.set(a.regra, (contaPorRegra.get(a.regra) ?? 0) + 1)

    const porProfissional = new Map< string, { atendimentos: number, criticos: number, alertas: number } >()
    for (const p of auditados) {
        const nome = p.principal.fisioterapeuta || "—"
        const acc  = porProfissional.get(nome) ?? { atendimentos: 0, criticos: 0, alertas: 0 }
        acc.atendimentos += p.atendimentos.length
        acc.criticos += p.achados.filter((a) => a.severidade === "critico").length
        acc.alertas  += p.achados.filter((a) => a.severidade === "alerta").length
        porProfissional.set(nome, acc)
    }

    const conta = (s: Severidade): number =>
        auditados.reduce((n, p) => n + p.achados.filter((a) => a.severidade === s).length, 0)

    const relatorio: Relatorio = {
        geradoEm:    new Date().toISOString(),
        unidade:     unidade,
        periodo:     periodo,
        total:        auditados.length,
        atendimentos: alvo.length,
        avaliacoes:   auditados.filter((p) => p.tipo === "avaliacao").length,
        comAchados:   auditados.filter((p) => p.achados.length > 0).length,
        criticos:    conta("critico"),
        alertas:     conta("alerta"),
        prontuarios: auditados,
        porRegra:    catalogoRegras
            .map((r) => ({ regra: r.id, severidade: r.severidade, titulo: r.titulo, total: contaPorRegra.get(r.id) ?? 0 }))
            .filter((r) => r.total > 0)
            .sort((a, b) => b.total - a.total),
        porProfissional: [ ...porProfissional.entries() ]
            .map(([ nome, v ]) => ({ nome, ...v }))
            .sort((a, b) => (b.criticos * 10 + b.alertas) - (a.criticos * 10 + a.alertas))
    }

    await mkdir(resolve(RAIZ, "data"), { recursive: true })
    await writeFile(resolve(RAIZ, "data/relatorio.json"), JSON.stringify(relatorio, null, 2), "utf8")

    /*  Cópia como script para o dashboard abrir direto do disco, sem servidor  */
    await writeFile(resolve(RAIZ, "web/relatorio.js"), `window.__RELATORIO__ = ${JSON.stringify(relatorio)}\n`, "utf8")

    console.log(`\n${relatorio.atendimentos} atendimentos → ${relatorio.total} fichas (${relatorio.avaliacoes} avaliações)`)
    console.log(`${relatorio.comAchados} com achados · ${relatorio.criticos} críticos · ${relatorio.alertas} alertas`)
    console.log("Abra web/index.html no navegador.")
}

main().catch((e) => {
    console.error(e instanceof SessaoExpiradaError ? e.message : e)
    process.exit(1)
})
