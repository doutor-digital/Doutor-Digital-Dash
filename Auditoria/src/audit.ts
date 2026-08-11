import { diffDias, paraISO } from "./parse.js"
import { PESO } from "./types.js"
import type { Achado, Prontuario, ProntuarioAuditado, Severidade } from "./types.js"

/*
 *  Regras de auditoria de prontuário.
 *
 *  Cada regra é independente e recebe o prontuário inteiro; devolve zero ou mais
 *  achados. Todas foram derivadas de inconsistências reais encontradas na
 *  revisão manual do atendimento #2470019 e generalizadas para a base.
 */

interface Regra {
    id:          string
    severidade:  Severidade
    titulo:      string
    aplicar:     (p: Prontuario) => Achado[] | Achado | null
}

const achado = (r: Regra, detalhe: string): Achado => ({
    regra:      r.id,
    severidade: r.severidade,
    titulo:     r.titulo,
    detalhe:    detalhe
})

/*  Termos que caracterizam encerramento do tratamento na evolução  */
const TERMOS_ALTA = /\bALTA\b|RECEBE ALTA|CONCEDIDA ALTA|ALTA FISIOTERAP/i

/*  Testes que uma alta neurológica precisa nomear para ser auditável  */
const TESTES_NEURO = /LAS[ÈE]GUE|SLUMP|BRAGARD|VALSALVA|REFLEXO|PATELAR|AQUILEU|MIOT[ÓO]M|DERMAT[ÓO]M|SENSIBILIDADE|FOR[ÇC]A MUSCULAR|GRAU\s*[IV]+|TRENDELEMBURG|THOMAS|FABER|SLR/i

const REGRAS: Regra[] = [
    {
        id:         "questionario-retroativo",
        severidade: "critico",
        titulo:     "Questionário de incapacidade preenchido retroativamente",
        aplicar: (p) => {
            const q = p.questionario
            if (!q?.criadoEmISO || !p.primeiraISO) return null

            const dias = diffDias(p.primeiraISO, q.criadoEmISO)
            if (dias < 7) return null

            const naAlta = p.evolucoes.some((e) => e.dataISO === q.criadoEmISO && TERMOS_ALTA.test(e.texto))
            const sufixo = naAlta ? " — e no mesmo dia da alta, junto com a coluna \"Final\"" : ""
            return achado(REGRAS[0]!, `A coluna "Início" mede o estado do paciente na 1ª consulta (${p.primeiraConsulta}), mas o registro só foi criado em ${q.criadoEm} — ${dias} dias depois${sufixo}. O escore inicial (${q.escoreInicial}/24) é reconstrução de memória, não medição.`)
        }
    },
    {
        id:         "alta-sem-testes",
        severidade: "critico",
        titulo:     "Alta sem testes objetivos nomeados",
        aplicar: (p) => {
            const alta = p.evolucoes.find((e) => TERMOS_ALTA.test(e.texto))
            if (!alta) return null
            if (TESTES_NEURO.test(alta.texto)) return null

            const cita = /TESTE/i.test(alta.texto)
            return achado(REGRAS[1]!, cita
                ? `A evolução de ${alta.data} afirma testes negativos mas não nomeia nenhum deles. Sem o teste identificado e seu resultado, a alta não é auditável.`
                : `A evolução de ${alta.data} concede alta sem registrar nenhum teste objetivo de reavaliação.`)
        }
    },
    {
        id:         "sessao-relampago",
        severidade: "critico",
        titulo:     "Atendimento com duração implausível",
        aplicar: (p) => {
            const out: Achado[] = []
            for (const a of p.atendimentos) {
                const d = a.duracaoMin

                /*  O contador do sistema estoura para atendimentos em aberto (bug de epoch)  */
                if (d === null || d <= 0 || d > 1440 || d >= 5) continue
                out.push(achado(REGRAS[2]!, `Atendimento #${a.id} (${a.inicio}) registrado com ${d} minuto(s). Sessão de fisioterapia não ocorre nesse tempo — indica abertura/fechamento indevido ou registro sem atendimento real.`))
            }
            return out
        }
    },
    {
        id:         "atendido-sem-evolucao",
        severidade: "critico",
        titulo:     "Tratamento concluído sem evolução registrada",
        aplicar: (p) => {
            if (p.tipo !== "tratamento") return null
            if (p.evolucoes.length > 0) return null
            if (!p.atendimentos.some((a) => /ATENDIDO/i.test(a.situacao))) return null
            return achado(REGRAS[3]!, `${p.atendimentos.length} atendimento(s) marcados como ATENDIDO mas a aba Evolução está vazia. Prontuário sem registro do que foi executado.`)
        }
    },
    {
        id:         "dia-duplicado",
        severidade: "alerta",
        titulo:     "Numeração de dia duplicada na evolução",
        aplicar: (p) => {
            const vistos = new Map< number, string[] >()
            for (const e of p.evolucoes) {
                if (e.diaRotulo === null) continue
                const lista = vistos.get(e.diaRotulo) ?? []
                lista.push(e.data)
                vistos.set(e.diaRotulo, lista)
            }

            const dups = [ ...vistos.entries() ].filter(([ , datas ]) => datas.length > 1)
            if (dups.length === 0) return null

            const desc = dups.map(([ dia, datas ]) => `DIA ${dia} em ${datas.join(" e ")}`).join("; ")
            return achado(REGRAS[4]!, `${desc}. Com rótulos repetidos, o dia de protocolo correspondente nunca aparece — sessões cobradas não batem com dias de protocolo distintos aplicados.`)
        }
    },
    {
        id:         "cabecalho-x-corpo",
        severidade: "alerta",
        titulo:     "Cabeçalho da evolução diverge do protocolo descrito",
        aplicar: (p) => {
            const divs = p.evolucoes.filter((e) => e.diaRotulo !== null && e.diaCorpo !== null && e.diaRotulo !== e.diaCorpo)
            if (divs.length === 0) return null

            const desc = divs.map((e) => `${e.data} (cabeçalho DIA ${e.diaRotulo}, corpo "protocolo do DIA ${e.diaCorpo}")`).join("; ")
            return achado(REGRAS[5]!, `${divs.length} registro(s) divergentes: ${desc}.`)
        }
    },
    {
        id:         "protocolo-repetido",
        severidade: "alerta",
        titulo:     "Mesmo dia de protocolo aplicado em sessões diferentes",
        aplicar: (p) => {
            const vistos = new Map< number, string[] >()
            for (const e of p.evolucoes) {
                if (e.diaCorpo === null) continue
                const lista = vistos.get(e.diaCorpo) ?? []
                lista.push(e.data)
                vistos.set(e.diaCorpo, lista)
            }

            const dups = [ ...vistos.entries() ].filter(([ , datas ]) => datas.length > 1)
            if (dups.length === 0) return null

            const desc = dups.map(([ dia, datas ]) => `protocolo do DIA ${dia} em ${datas.join(" e ")}`).join("; ")
            return achado(REGRAS[6]!, `${desc}. O dia de protocolo seguinte não chegou a ser executado.`)
        }
    },
    {
        id:         "gap-sessoes",
        severidade: "alerta",
        titulo:     "Intervalo longo entre sessões sem justificativa",
        aplicar: (p) => {
            const datas = p.evolucoes.map((e) => e.dataISO).filter((d): d is string => d !== null)
            if (datas.length < 3) return null

            const out: Achado[] = []
            for (let i = 1; i < datas.length; i++) {
                const dias = diffDias(datas[i - 1]!, datas[i]!)
                if (dias < 14) continue

                const evo    = p.evolucoes.find((e) => e.dataISO === datas[i])
                const just   = /F[ÉE]RIAS|FALTA|VIAGEM|AFASTAD|ATESTADO|INTERNA|RETORNO AP[ÓO]S|AUS[ÊE]NCIA/i.test(evo?.texto ?? "")
                if (just) continue
                out.push(achado(REGRAS[7]!, `${dias} dias sem sessão entre ${datas[i - 1]!.split("-").reverse().join("/")} e ${datas[i]!.split("-").reverse().join("/")}, sem nota de férias, falta ou pausa. Quebra a cadência do protocolo.`))
            }
            return out
        }
    },
    {
        id:         "eva-sem-final",
        severidade: "alerta",
        titulo:     "Sessão sem EVA de encerramento",
        aplicar: (p) => {
            const comEva = p.evolucoes.filter((e) => e.evaInicial !== null || e.evaFinal !== null)

            /*  Só cobra o EVA final onde o padrão do próprio prontuário é registrá-lo  */
            if (comEva.length < 3) return null

            const faltando = comEva.filter((e) => e.evaFinal === null)
            if (faltando.length === 0) return null
            return achado(REGRAS[8]!, `${faltando.length} de ${comEva.length} sessões não fecham com EVA numérico (${faltando.map((e) => e.data).join(", ")}), quebrando o padrão do próprio prontuário.`)
        }
    },
    {
        id:         "eva-classificacao",
        severidade: "alerta",
        titulo:     "Adjetivo da dor incoerente com o valor de EVA",
        aplicar: (p) => {
            const out: Achado[] = []
            for (const e of p.evolucoes) {
                if (e.evaInicial === null) continue

                const t     = e.texto.toUpperCase()
                const leve  = /\bLEVE\b/.test(t)
                const mod   = /\bMODERAD/.test(t)
                const forte = /\bINTENS|\bFORTE|\bSEVER/.test(t)
                const v     = e.evaInicial

                if (leve  && v >= 4) out.push(achado(REGRAS[9]!, `${e.data}: dor descrita como "leve" com EVA ${v} (4–6 é moderada, 7–10 é intensa).`))
                if (mod   && v >= 7) out.push(achado(REGRAS[9]!, `${e.data}: dor descrita como "moderada" com EVA ${v} (7–10 é intensa).`))
                if (forte && v <= 3) out.push(achado(REGRAS[9]!, `${e.data}: dor descrita como intensa com EVA ${v} (0–3 é leve).`))
            }
            if (out.length === 0) return null

            /*  Uma escala verbal inconsistente invalida a comparação entre sessões  */
            return out
        }
    },
    {
        id:         "contador-divergente",
        severidade: "alerta",
        titulo:     "Contador de atendimentos divergente da evolução",
        aplicar: (p) => {
            if (p.realizados === null || p.evolucoes.length === 0) return null

            const out: Achado[] = []
            if (p.evolucoes.length !== p.realizados)
                out.push(achado(REGRAS[10]!, `O sistema informa ${p.realizados} atendimentos realizados, mas há ${p.evolucoes.length} registros de evolução.`))

            if (p.esteAtendimento !== null && p.previstos !== null && p.esteAtendimento > p.previstos)
                out.push(achado(REGRAS[10]!, `"Este atendimento: ${p.esteAtendimento}" excede o total previsto do plano (${p.previstos}).`))

            return out.length > 0 ? out : null
        }
    },
    {
        id:         "cbdf-desatualizado",
        severidade: "alerta",
        titulo:     "CBDF não revisado no encerramento",
        aplicar: (p) => {
            const alta = p.evolucoes.find((e) => TERMOS_ALTA.test(e.texto))
            if (!alta) return null
            if (p.cbdf.length === 0)
                return achado(REGRAS[11]!, "Alta concedida sem nenhuma classificação CBDF registrada.")

            /*  Alta que nega a condição mas mantém o CBDF que a afirma  */
            const negou = /NEGATIV|SEM (H[ÉE]RNIA|CI[ÁA]TICA|COMPRESS)/i.test(alta.texto)
            if (negou && p.cbdf.some((c) => /H[ÉE]RNIA|CI[ÁA]TICA/i.test(c)))
                return achado(REGRAS[11]!, `A evolução de alta declara quadro negativo, mas o CBDF segue registrado como "${p.cbdf[0]!.split(" - ")[1] ?? p.cbdf[0]}". A classificação não foi revisada no encerramento.`)

            return null
        }
    },
    {
        id:         "prognostico-ausente",
        severidade: "alerta",
        titulo:     "Prognóstico não registrado",
        aplicar: (p) => {
            if (p.evolucoes.length < 3) return null
            if (p.prognostico) return null
            return achado(REGRAS[12]!, "Tratamento em curso sem prognóstico registrado na aba correspondente.")
        }
    },
    {
        id:         "alta-sessao-curta",
        severidade: "alerta",
        titulo:     "Sessão de alta mais curta que a média do prontuário",
        aplicar: (p) => {
            const alta = p.evolucoes.at(-1)
            if (!alta?.dataISO || !TERMOS_ALTA.test(alta.texto)) return null

            /*  Cruza a data da evolução de alta com o atendimento daquele mesmo dia  */
            const sessao = p.atendimentos.find((a) => paraISO(a.inicio?.split(" ")[0] ?? null) === alta.dataISO)
            const d      = sessao?.duracaoMin ?? null
            if (d === null || d <= 0 || d > 1440 || d >= 25) return null
            return achado(REGRAS[13]!, `A sessão de alta (#${sessao!.id}, ${alta.data}) durou ${d} min. Reavaliação, aplicação do questionário de 24 itens e orientações de encerramento não cabem nesse tempo.`)
        }
    },
    {
        id:         "questionario-contradiz-evolucao",
        severidade: "alerta",
        titulo:     "Escore inicial de incapacidade contradiz a evolução",
        aplicar: (p) => {
            const q = p.questionario
            if (q?.escoreInicial === null || q?.escoreInicial === undefined) return null
            if (q.escoreInicial < 18) return null

            const iniciais = p.evolucoes.slice(0, 4).map((e) => e.evaInicial).filter((v): v is number => v !== null)
            if (iniciais.length === 0) return null

            const maxEva = Math.max(...iniciais)
            if (maxEva > 6) return null
            return achado(REGRAS[14]!, `Escore inicial ${q.escoreInicial}/24 indica incapacidade grave, mas as primeiras sessões registram EVA máximo de ${maxEva} e paciente em atividade. Sugere preenchimento em bloco, não item a item.`)
        }
    },
    {
        id:         "protocolo-estourado",
        severidade: "info",
        titulo:     "Protocolo encerrado fora do prazo previsto",
        aplicar: (p) => {
            if (!p.primeiraISO || !/(\d+)\s*MES/i.test(p.plano)) return null

            const ultima = p.evolucoes.at(-1)?.dataISO
            if (!ultima) return null

            const meses = Number.parseInt(p.plano.match(/(\d+)\s*MES/i)![1]!, 10)
            const dias  = diffDias(p.primeiraISO, ultima)
            const limite = meses * 30 + 7
            if (dias <= limite) return null
            return achado(REGRAS[15]!, `Plano de ${meses} meses iniciado em ${p.primeiraConsulta} e encerrado ${dias} dias depois (${dias - meses * 30} dias além do previsto).`)
        }
    }
]

export function auditar(p: Prontuario): ProntuarioAuditado {
    const achados: Achado[] = []

    for (const regra of REGRAS) {
        const r = regra.aplicar(p)
        if (!r) continue
        if (Array.isArray(r)) achados.push(...r)
        else achados.push(r)
    }

    const escore = achados.reduce((soma, a) => soma + PESO[a.severidade], 0)
    return { ...p, achados, escore }
}

export const catalogoRegras = REGRAS.map((r) => ({ id: r.id, severidade: r.severidade, titulo: r.titulo }))
