import { parseHTML } from "linkedom"
import type { Atendimento, Evolucao, Prontuario, Questionario, TipoRegistro } from "./types.js"

/*
 *  Extração do HTML server-side da aplicação de origem.
 *
 *  Tudo aqui trabalha sobre o HTML cru, não sobre um DOM vivo: o estado dos
 *  radios vem do atributo `checked` (SSR), não da propriedade `.checked`.
 */

const texto = (el: Element | null | undefined): string => {
    return (el?.textContent ?? "").replace(/\s+/g, " ").trim()
}

/*  "11/08/2026" ou "11/08/26" -> "2026-08-11"  */
export const paraISO = (br: string | null | undefined): string | null => {
    if (!br) return null
    const m = br.trim().match(/^(\d{2})\/(\d{2})\/(\d{2,4})/)
    if (!m) return null
    const [ , d, mes, a ] = m
    const ano = a!.length === 2 ? `20${a}` : a
    return `${ano}-${mes}-${d}`
}

export const diffDias = (isoA: string, isoB: string): number => {
    const a = Date.parse(`${isoA}T00:00:00Z`)
    const b = Date.parse(`${isoB}T00:00:00Z`)
    return Math.round((b - a) / 86400000)
}

/*  Converte o HTML rico do Quill em texto legível, preservando quebras de parágrafo  */
const htmlParaTexto = (html: string): string => {
    return html
        .replace(/<\s*br\s*\/?\s*>/gi, "\n")
        .replace(/<\/\s*(p|div|li|h\d)\s*>/gi, "\n")
        .replace(/<[^>]+>/g, "")
        .replace(/&nbsp;/gi, " ")
        .replace(/&amp;/gi, "&")
        .replace(/&lt;/gi, "<")
        .replace(/&gt;/gi, ">")
        .replace(/[ \t]+/g, " ")
        .replace(/\n{3,}/g, "\n\n")
        .trim()
}

export function parseListagem(html: string): { atendimentos: Atendimento[], total: number } {
    const { document } = parseHTML(html)
    const atendimentos: Atendimento[] = []

    for (const tr of document.querySelectorAll("table tbody tr")) {
        const tds = [ ...tr.querySelectorAll("td") ]
        if (tds.length < 9) continue

        const idTxt = texto(tds[0]).replace(/^#/, "")
        const id    = Number.parseInt(idTxt, 10)
        if (!Number.isFinite(id)) continue

        const durTxt = texto(tds[4]).match(/(\d+)/)
        atendimentos.push({
            id:             id,
            paciente:       texto(tds[1]),
            inicio:         texto(tds[2]) || null,
            termino:        texto(tds[3]) || null,
            duracaoMin:     durTxt ? Number.parseInt(durTxt[1]!, 10) : null,
            fisioterapeuta: texto(tds[5]),
            unidade:        texto(tds[6]),
            situacao:       texto(tds[8])
        })
    }

    const mTotal = html.match(/Total de\s*(\d+)\s*registros/i)
    return { atendimentos, total: mTotal ? Number.parseInt(mTotal[1]!, 10) : atendimentos.length }
}

/*
 *  Lê o rótulo "DIA N" do cabeçalho da evolução e, separadamente, o "PROTOCOLO DO
 *  DIA N" citado no corpo. Divergência entre os dois é achado de auditoria — por
 *  isso são campos distintos e não um só normalizado.
 */
const extrairDias = (txt: string): { rotulo: number | null, corpo: number | null } => {
    const linhas = txt.split("\n").map((l) => l.trim()).filter(Boolean)

    let rotulo: number | null = null
    for (const l of linhas.slice(0, 4)) {
        const m = l.match(/^DIA\s+(\d+)\s*$/i)
        if (m) {
            rotulo = Number.parseInt(m[1]!, 10)
            break
        }
    }

    let corpo: number | null = null
    const mc = txt.match(/PROTOCOLO\s+(?:B\s+)?(?:DO\s+|DESTE\s+)?DIA\s+(\d+)/i)
    if (mc) corpo = Number.parseInt(mc[1]!, 10)

    return { rotulo, corpo }
}

/*
 *  EVA inicial e final. O padrão de escrita da equipe é "EVA <n>" no trecho de
 *  abertura e "TERMINA SESSÃO ... EVA <n>" no fecho; quando o fecho não traz
 *  número, evaFinal fica null (é isso que a regra `eva-sem-final` procura).
 */
const extrairEva = (txt: string): { inicial: number | null, final: number | null } => {
    const t     = txt.toUpperCase()
    const todos = [ ...t.matchAll(/EVA\s*:?\s*(\d{1,2})/g) ]
        .map((m) => ({ valor: Number.parseInt(m[1]!, 10), pos: m.index ?? 0 }))
        .filter((x) => x.valor >= 0 && x.valor <= 10)

    if (todos.length === 0) return { inicial: null, final: null }

    /*
     *  O fecho é o PRIMEIRO marcador de encerramento, não o último: registros que
     *  trazem "APÓS AJUSTES, EVA 0" e depois "AO TÉRMINO, SEM QUEIXAS" teriam o
     *  EVA final descartado se a busca partisse do marcador mais tardio.
     */
    const marcas = [ "TERMINA", "FINALIZA", "AO TÉRMINO", "APÓS AJUSTES", "APÓS FLEXO" ]
        .map((m) => t.indexOf(m))
        .filter((i) => i >= 0)

    const iFecho = marcas.length > 0 ? Math.min(...marcas) : -1
    const fecho  = iFecho >= 0 ? todos.filter((x) => x.pos > iFecho) : []
    const abre   = iFecho >= 0 ? todos.filter((x) => x.pos < iFecho) : todos

    return {
        inicial: abre[0]?.valor  ?? null,
        final:   fecho.at(-1)?.valor ?? null
    }
}

function parseEvolucoes(document: Document): Evolucao[] {
    const out: Evolucao[] = []

    for (const li of document.querySelectorAll("#evolution li.timeline-item")) {
        const data = texto(li.querySelector("strong"))
        const prof = texto(li.querySelector("span.float-end")).replace(/\s+/g, " ").trim()

        /*
         *  O corpo NÃO pode ser lido via li.querySelector("p"): o editor grava
         *  <P> aninhado dentro do <p> do template, e todo parser HTML fecha o
         *  parágrafo externo ao encontrar o interno — o <p> externo chega vazio.
         *  Lê-se o innerHTML do <li> descontando a data e o nome do profissional.
         */
        const bruto = li.innerHTML
            .replace(/<strong>[\s\S]*?<\/strong>/i, "")
            .replace(/<span[^>]*float-end[^>]*>[\s\S]*?<\/span>/i, "")
        const txt = htmlParaTexto(bruto)
        if (!data && !txt) continue

        const linhas    = txt.split("\n").map((l) => l.trim()).filter(Boolean)
        const protocolo = linhas.find((l) => /^PROTOCOLO\s+\w/i.test(l) && !/DO\s+DIA/i.test(l)) ?? ""
        const dias      = extrairDias(txt)
        const eva       = extrairEva(txt)

        out.push({
            data:         data,
            dataISO:      paraISO(data),
            profissional: prof,
            protocolo:    protocolo,
            diaRotulo:    dias.rotulo,
            diaCorpo:     dias.corpo,
            evaInicial:   eva.inicial,
            evaFinal:     eva.final,
            texto:        txt
        })
    }

    /*  A timeline vem do mais recente para o mais antigo; a auditoria lê cronológico  */
    return out.reverse()
}

function parseQuestionario(html: string, document: Document): Questionario | null {
    const pane = document.querySelector("#incapacidade")
    if (!pane) return null

    const mCriado = texto(pane).match(/Criado em:\s*([\d/]+\s*[\d:]*)/i)
    const criado  = mCriado ? mCriado[1]!.trim() : null

    /*  Escore Roland-Morris = quantidade de "S" (Sim). Radios marcados via atributo.  */
    const bloco   = html.slice(html.indexOf("id=\"incapacidade\""))
    const fim     = bloco.indexOf("id=\"lombar\"")
    const escopo  = fim > 0 ? bloco.slice(0, fim) : bloco

    const marcados = [ ...escopo.matchAll(/name="(f_)?question_(\d+)"[^>]*value="([SN])"[^>]*\schecked/gi) ]
    if (marcados.length === 0) return null

    let inicial = 0, final = 0, respInicial = 0, respFinal = 0
    for (const m of marcados) {
        const ehFinal = Boolean(m[1])
        const sim     = m[3]!.toUpperCase() === "S"
        if (ehFinal) {
            respFinal++
            if (sim) final++
        }
        else {
            respInicial++
            if (sim) inicial++
        }
    }

    return {
        criadoEm:      criado,
        criadoEmISO:   paraISO(criado),
        escoreInicial: respInicial > 0 ? inicial : null,
        escoreFinal:   respFinal   > 0 ? final   : null,
        respondidos:   respInicial + respFinal
    }
}

function parseAnamnese(document: Document): Record< string, string > {
    const out:  Record< string, string > = {}
    const form = document.querySelector("#formAnamnese")
    if (!form) return out

    for (const el of form.querySelectorAll("input, textarea")) {
        const nome = el.getAttribute("name")
        const tipo = el.getAttribute("type")
        if (!nome || tipo === "hidden" || tipo === "submit") continue

        const valor = el.tagName === "TEXTAREA" ? texto(el) : (el.getAttribute("value") ?? "")
        if (valor.trim()) out[nome] = valor.trim()
    }

    const prof = form.querySelector("select[name=id_profession] option[selected]")
    if (prof) out["id_profession"] = texto(prof)

    return out
}

export function parseProntuario(html: string, atendimento: Atendimento): Prontuario {
    const { document } = parseHTML(html)
    const corpo        = texto(document.body)

    /*
     *  Avaliação avulsa ("Iniciando Avaliação") não tem aba de evolução nem
     *  tratamento vinculado. Sem essa distinção toda avaliação seria acusada de
     *  atendimento concluído sem evolução.
     */
    const tipo: TipoRegistro = document.querySelector("#evolution-tab") ? "tratamento" : "avaliacao"

    const num = (re: RegExp): number | null => {
        const m = corpo.match(re)
        return m ? Number.parseInt(m[1]!, 10) : null
    }

    const mPrimeira = corpo.match(/Primeira consulta em:\s*([\d/]+)/i)
    const mRealiz   = corpo.match(/Atendimentos realizados:\s*(\d+)\s*de\s*(\d+)/i)
    const mNome     = document.querySelector("h3 a")
    const planos    = [ ...document.querySelectorAll("span.badge.bg-primary") ].map(texto).filter(Boolean)

    const hidden = (nome: string): number | null => {
        const el = document.querySelector(`input[name="${nome}"]`)
        const v  = el?.getAttribute("value")
        return v ? Number.parseInt(v, 10) : null
    }

    const cbdf = [ ...document.querySelectorAll("#CBDF option[selected]") ]
        .map(texto)
        .filter((t) => t && !/^N\/A/i.test(t))

    const prognostico = texto(document.querySelector("#prognosis option[selected]")) || null

    const idTreatment = hidden("id_treatment")

    return {
        chave:            idTreatment !== null ? `t${idTreatment}` : `a${atendimento.id}`,
        tipo:             tipo,
        atendimentos:     [ atendimento ],
        principal:        atendimento,
        idClient:         hidden("id_client"),
        idTreatment:      idTreatment,
        nomePaciente:     texto(mNome) || atendimento.paciente,
        idade:            num(/Idade:\s*(\d+)\s*anos/i),
        plano:            planos.join(" · "),
        primeiraConsulta: mPrimeira ? mPrimeira[1]! : null,
        primeiraISO:      paraISO(mPrimeira?.[1]),
        realizados:       mRealiz ? Number.parseInt(mRealiz[1]!, 10) : null,
        previstos:        mRealiz ? Number.parseInt(mRealiz[2]!, 10) : null,
        esteAtendimento:  num(/Este atendimento:\s*(\d+)/i),
        anamnese:         parseAnamnese(document),
        evolucoes:        parseEvolucoes(document),
        questionario:     parseQuestionario(html, document),
        cbdf:             cbdf,
        prognostico:      prognostico
    }
}
