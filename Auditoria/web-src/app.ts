import type { Achado, ProntuarioAuditado, Relatorio, Severidade } from "../src/types.js"

/*
 *  Dashboard da auditoria. Consome web/relatorio.js (gravado pelo scraper como
 *  window.__RELATORIO__) para funcionar aberto direto do disco, sem servidor.
 *
 *  Gráficos são SVG montados à mão: barras horizontais para magnitude por regra
 *  e uma linha única para o EVA ao longo das sessões. Ambos com camada de hover.
 */

declare global {
    interface Window { __RELATORIO__?: Relatorio }
}

const rel = window.__RELATORIO__
const $   = <T extends HTMLElement>(id: string): T => document.getElementById(id) as T

const GLIFO: Record< Severidade, string > = { critico: "◆", alerta: "▲", info: "●" }
const NOME:  Record< Severidade, string > = { critico: "crítico", alerta: "alerta", info: "info" }

const esc = (s: string): string =>
    s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;")

const contar = (achados: Achado[], s: Severidade): number => achados.filter((a) => a.severidade === s).length

/*  ── camada de hover, compartilhada pelos dois gráficos ─────────────────── */

const dica = $<HTMLDivElement>("dica")

const mostrarDica = (ev: MouseEvent, html: string): void => {
    dica.innerHTML = html
    dica.classList.add("visivel")

    const r = dica.getBoundingClientRect()
    const x = Math.min(ev.clientX + 14, window.innerWidth  - r.width  - 8)
    const y = Math.min(ev.clientY + 14, window.innerHeight - r.height - 8)
    dica.style.left = `${x}px`
    dica.style.top  = `${y}px`
}

const esconderDica = (): void => dica.classList.remove("visivel")

const svgNS = "http://www.w3.org/2000/svg"

const el = (tag: string, attrs: Record< string, string | number >): SVGElement => {
    const n = document.createElementNS(svgNS, tag)
    for (const [ k, v ] of Object.entries(attrs)) n.setAttribute(k, String(v))
    return n
}

/*  ── barras horizontais: achados por regra ──────────────────────────────── */

function graficoRegras(alvo: HTMLElement, dados: Relatorio["porRegra"]): void {
    alvo.textContent = ""
    if (dados.length === 0) {
        alvo.innerHTML = "<div class=\"vazio\">Nenhum achado.</div>"
        return
    }

    const alturaLinha = 30
    const rotuloW     = 340
    const padDir      = 56
    const largura     = Math.max(alvo.clientWidth || 900, 620)
    const altura      = dados.length * alturaLinha + 8
    const plotW       = largura - rotuloW - padDir
    const maximo      = Math.max(...dados.map((d) => d.total))

    const svg = el("svg", { class: "grafico", viewBox: `0 0 ${largura} ${altura}`, width: "100%", height: altura, role: "img" })

    dados.forEach((d, i) => {
        const y = i * alturaLinha + 4
        const w = Math.max((d.total / maximo) * plotW, 3)

        const rot = el("text", { x: rotuloW - 12, y: y + 15, "text-anchor": "end", class: "tick" })
        rot.textContent = d.titulo.length > 50 ? `${d.titulo.slice(0, 49)}…` : d.titulo
        svg.appendChild(rot)

        /*  4px de raio nas pontas de dado, ancoradas na linha de base  */
        const barra = el("rect", {
            x: rotuloW, y: y + 4, width: w, height: 16, rx: 4,
            fill: `var(--${d.severidade})`, class: "marca"
        })
        barra.addEventListener("mousemove", (ev) => mostrarDica(ev as MouseEvent,
            `<div class="dv">${esc(d.titulo)}</div><div class="dt">${GLIFO[d.severidade]} ${NOME[d.severidade]} · ${d.total} ficha(s)</div>`))
        barra.addEventListener("mouseleave", esconderDica)
        svg.appendChild(barra)

        const num = el("text", { x: rotuloW + w + 8, y: y + 16, class: "rotulo-direto" })
        num.textContent = `${GLIFO[d.severidade]} ${d.total}`
        svg.appendChild(num)
    })

    svg.appendChild(el("line", { x1: rotuloW, y1: 0, x2: rotuloW, y2: altura, class: "eixo" }))
    alvo.appendChild(svg)
}

/*  ── linha: EVA ao longo das sessões de um prontuário ───────────────────── */

function graficoEva(alvo: HTMLElement, p: ProntuarioAuditado): void {
    const pontos = p.evolucoes
        .map((e, i) => ({ i, data: e.data, dia: e.diaRotulo, ini: e.evaInicial, fim: e.evaFinal }))
        .filter((x) => x.ini !== null || x.fim !== null)

    if (pontos.length < 2) {
        alvo.innerHTML = "<div class=\"vazio\">Sem EVA suficiente registrado para traçar a curva.</div>"
        return
    }

    const largura = Math.max(alvo.clientWidth || 700, 520)
    const altura  = 190
    const padE    = 34, padD = 14, padT = 14, padB = 30
    const plotW   = largura - padE - padD
    const plotH   = altura  - padT - padB

    const x = (i: number): number => padE + (pontos.length === 1 ? plotW / 2 : (i / (pontos.length - 1)) * plotW)
    const y = (v: number): number => padT + plotH - (v / 10) * plotH

    const svg = el("svg", { class: "grafico", viewBox: `0 0 ${largura} ${altura}`, width: "100%", height: altura, role: "img" })

    for (const v of [ 0, 2, 4, 6, 8, 10 ]) {
        svg.appendChild(el("line", { x1: padE, y1: y(v), x2: largura - padD, y2: y(v), class: "grade" }))
        const t = el("text", { x: padE - 8, y: y(v) + 4, "text-anchor": "end", class: "tick" })
        t.textContent = String(v)
        svg.appendChild(t)
    }

    /*  Uma série só: sem legenda, o título nomeia o que está plotado  */
    const usados = pontos.map((pt, k) => ({ k, v: pt.ini ?? pt.fim ?? 0, pt }))
    const linha  = usados.map((u) => `${u.k === 0 ? "M" : "L"}${x(u.k)},${y(u.v)}`).join(" ")
    svg.appendChild(el("path", { d: linha, fill: "none", stroke: "var(--series-1)", "stroke-width": 2, "stroke-linejoin": "round" }))

    for (const u of usados) {
        /*  Anel de 2px na cor da superfície separa marcas sobrepostas  */
        svg.appendChild(el("circle", { cx: x(u.k), cy: y(u.v), r: 5, fill: "var(--series-1)", stroke: "var(--surface)", "stroke-width": 2 }))

        const alvoHit = el("circle", { cx: x(u.k), cy: y(u.v), r: 14, fill: "transparent", class: "marca" })
        alvoHit.addEventListener("mousemove", (ev) => mostrarDica(ev as MouseEvent,
            `<div class="dv">${esc(u.pt.data)}${u.pt.dia !== null ? ` · DIA ${u.pt.dia}` : ""}</div>` +
            `<div class="dt">EVA inicial ${u.pt.ini ?? "—"} · final ${u.pt.fim ?? "—"}</div>`))
        alvoHit.addEventListener("mouseleave", esconderDica)
        svg.appendChild(alvoHit)
    }

    svg.appendChild(el("line", { x1: padE, y1: y(0), x2: largura - padD, y2: y(0), class: "eixo" }))

    const primeira = el("text", { x: padE, y: altura - 10, class: "tick" })
    primeira.textContent = pontos[0]!.data
    svg.appendChild(primeira)

    const ultima = el("text", { x: largura - padD, y: altura - 10, "text-anchor": "end", class: "tick" })
    ultima.textContent = pontos.at(-1)!.data
    svg.appendChild(ultima)

    alvo.textContent = ""
    alvo.appendChild(svg)
}

/*  ── estado e renderização ──────────────────────────────────────────────── */

let selecionada: string | null = null

const filtrados = (): ProntuarioAuditado[] => {
    if (!rel) return []

    const termo = $<HTMLInputElement>("busca").value.trim().toLowerCase()
    const sev   = $<HTMLSelectElement>("f-severidade").value
    const prof  = $<HTMLSelectElement>("f-profissional").value
    const regra = $<HTMLSelectElement>("f-regra").value

    return rel.prontuarios.filter((p) => {
        if (prof && p.principal.fisioterapeuta !== prof) return false
        if (regra && !p.achados.some((a) => a.regra === regra)) return false

        if (sev === "limpo" && p.achados.length > 0) return false
        if (sev === "critico" && contar(p.achados, "critico") === 0) return false
        if (sev === "alerta"  && contar(p.achados, "alerta")  === 0) return false

        if (termo) {
            const alvo = `${p.nomePaciente} ${p.principal.fisioterapeuta} ${p.atendimentos.map((a) => a.id).join(" ")}`.toLowerCase()
            if (!alvo.includes(termo)) return false
        }
        return true
    })
}

function renderLista(): void {
    const lista = $<HTMLDivElement>("lista")
    const itens = filtrados()

    $<HTMLSpanElement>("contagem").textContent = `${itens.length} de ${rel!.prontuarios.length} fichas`

    if (itens.length === 0) {
        lista.innerHTML = "<div class=\"vazio\">Nada corresponde ao filtro.</div>"
        return
    }

    lista.innerHTML = itens.map((p) => {
        const c   = contar(p.achados, "critico")
        const a   = contar(p.achados, "alerta")
        const sel = p.chave === selecionada

        const selos = [
            c > 0 ? `<span class="selo critico"><span class="glifo">${GLIFO.critico}</span>${c}</span>` : "",
            a > 0 ? `<span class="selo alerta"><span class="glifo">${GLIFO.alerta}</span>${a}</span>`  : "",
            c === 0 && a === 0 ? `<span class="selo ok"><span class="glifo">${GLIFO.info}</span>ok</span>` : ""
        ].join("")

        const tipo = p.tipo === "avaliacao" ? "avaliação" : `${p.evolucoes.length} sessões`
        return `<div class="item" role="option" tabindex="0" aria-selected="${sel}" data-chave="${esc(p.chave)}">
            <div class="nome">${esc(p.nomePaciente)}</div>
            <div class="sub2">#${p.principal.id} · ${tipo} · ${esc(p.principal.fisioterapeuta)}</div>
            <div class="selos">${selos}</div>
        </div>`
    }).join("")

    for (const node of lista.querySelectorAll< HTMLDivElement >(".item")) {
        const abrir = (): void => {
            selecionada = node.dataset["chave"] ?? null
            renderLista()
            renderDetalhe()
        }
        node.addEventListener("click", abrir)
        node.addEventListener("keydown", (ev) => {
            if ((ev as KeyboardEvent).key === "Enter" || (ev as KeyboardEvent).key === " ") {
                ev.preventDefault()
                abrir()
            }
        })
    }
}

function renderDetalhe(): void {
    const box = $<HTMLDivElement>("detalhe")
    const p   = rel!.prontuarios.find((x) => x.chave === selecionada)

    if (!p) {
        box.innerHTML = "<div class=\"vazio\">Selecione uma ficha à esquerda.</div>"
        return
    }

    const q      = p.questionario
    const campo  = (k: string, v: string): string => `<div class="campo"><div class="k">${esc(k)}</div><div class="v">${esc(v)}</div></div>`
    const evaIni = p.evolucoes.find((e) => e.evaInicial !== null)?.evaInicial

    /*  Datas que a auditoria citou: destacam a sessão correspondente na timeline  */
    const marcadas = new Set(p.achados.flatMap((a) => a.detalhe.match(/\d{2}\/\d{2}\/\d{4}/g) ?? []))

    box.innerHTML = `
      <div class="cabeca">
        <div>
          <h2>${esc(p.nomePaciente)}</h2>
          <div class="sub">${p.idade !== null ? `${p.idade} anos · ` : ""}${esc(p.plano || "sem plano registrado")}</div>
        </div>
        <div class="selos">
          <span class="selo critico"><span class="glifo">${GLIFO.critico}</span>${contar(p.achados, "critico")} crítico</span>
          <span class="selo alerta"><span class="glifo">${GLIFO.alerta}</span>${contar(p.achados, "alerta")} alerta</span>
        </div>
      </div>

      <div class="dados">
        ${campo("Atendimentos", `${p.atendimentos.length} nesta janela`)}
        ${campo("Evoluções", String(p.evolucoes.length))}
        ${campo("1ª consulta", p.primeiraConsulta ?? "—")}
        ${campo("Contador", p.realizados !== null ? `${p.realizados} de ${p.previstos ?? "?"}` : "—")}
        ${campo("Prognóstico", p.prognostico ? p.prognostico.replace(/^Progn[óo]stico\s*/i, "") : "—")}
        ${campo("Roland-Morris", q ? `${q.escoreInicial ?? "—"} → ${q.escoreFinal ?? "—"}` : "não aplicado")}
        ${campo("Questionário criado", q?.criadoEm ?? "—")}
        ${campo("Fisioterapeuta", p.principal.fisioterapeuta)}
      </div>

      ${p.cbdf.length > 0 ? `<div class="sub" style="margin-top:12px"><strong>CBDF:</strong> ${esc(p.cbdf[0]!.split(" - ").slice(0, 2).join(" — "))}</div>` : ""}

      <h2 style="margin-top:20px">Achados (${p.achados.length}) · escore ${p.escore}</h2>
      <div class="sub">Crítico pesa 10, alerta 3, info 1.</div>
      ${p.achados.length === 0
          ? "<div class=\"vazio\">Nenhuma regra disparou nesta ficha.</div>"
          : p.achados.map((a) => `<div class="achado ${a.severidade}">
              <div class="t"><span style="color:var(--${a.severidade})">${GLIFO[a.severidade]}</span>${esc(a.titulo)}</div>
              <div class="d">${esc(a.detalhe)}</div>
            </div>`).join("")}

      ${p.evolucoes.length > 1 ? `
        <h2 style="margin-top:22px">EVA por sessão registrada</h2>
        <div class="sub">Escala visual analógica de dor no início de cada sessão${evaIni !== undefined ? ` — abriu em ${evaIni}` : ""}. Passe o cursor para ver a sessão.</div>
        <div id="grafico-eva"></div>` : ""}

      ${p.evolucoes.length > 0 ? `
        <h2 style="margin-top:22px">Evolução (${p.evolucoes.length} registros)</h2>
        <div class="sub">Em ordem cronológica. Sessões citadas por algum achado aparecem destacadas.</div>
        <div class="evo">${p.evolucoes.map((e) => `
          <div class="sessao${marcadas.has(e.data) ? " marcada" : ""}">
            <div class="linha1">
              <span class="data">${esc(e.data)}</span>
              <span class="dia">${e.diaRotulo !== null ? `DIA ${e.diaRotulo}` : "sem rótulo de dia"}${e.diaCorpo !== null && e.diaCorpo !== e.diaRotulo ? ` · corpo cita DIA ${e.diaCorpo}` : ""}</span>
              <span class="eva">EVA ${e.evaInicial ?? "—"} → ${e.evaFinal ?? "—"}</span>
            </div>
            <div class="texto">${esc(e.texto)}</div>
          </div>`).join("")}</div>` : ""}
    `

    const cx = document.getElementById("grafico-eva")
    if (cx) graficoEva(cx, p)
}

function renderTopo(): void {
    if (!rel) return

    $<HTMLDivElement>("meta").textContent =
        `${rel.unidade} · ${rel.periodo} · gerado em ${new Date(rel.geradoEm).toLocaleString("pt-BR")}`

    const tiles: Array< { rotulo: string, valor: string, nota: string, cls?: string } > = [
        { rotulo: "Fichas auditadas", valor: String(rel.total),      nota: `${rel.atendimentos} atendimentos · ${rel.avaliacoes} avaliações` },
        { rotulo: "Com achados",      valor: String(rel.comAchados),  nota: `${Math.round((rel.comAchados / Math.max(rel.total, 1)) * 100)}% das fichas` },
        { rotulo: "Críticos",         valor: String(rel.criticos),    nota: "invalidam a auditabilidade", cls: "critico" },
        { rotulo: "Alertas",          valor: String(rel.alertas),     nota: "inconsistências de registro", cls: "alerta" }
    ]

    $<HTMLElement>("tiles").innerHTML = tiles.map((t) => `
      <div class="tile ${t.cls ?? ""}">
        <div class="rotulo">${esc(t.rotulo)}</div>
        <div class="valor">${esc(t.valor)}</div>
        <div class="nota">${esc(t.nota)}</div>
      </div>`).join("")

    const profs = [ ...new Set(rel.prontuarios.map((p) => p.principal.fisioterapeuta).filter(Boolean)) ].sort()
    $<HTMLSelectElement>("f-profissional").insertAdjacentHTML("beforeend",
        profs.map((n) => `<option value="${esc(n)}">${esc(n)}</option>`).join(""))

    $<HTMLSelectElement>("f-regra").insertAdjacentHTML("beforeend",
        rel.porRegra.map((r) => `<option value="${esc(r.regra)}">${esc(r.titulo)} (${r.total})</option>`).join(""))

    $<HTMLTableElement>("tabela-prof").innerHTML = `
      <thead><tr><th>Fisioterapeuta</th><th style="text-align:right">Atendimentos</th><th style="text-align:right">Críticos</th><th style="text-align:right">Alertas</th></tr></thead>
      <tbody>${rel.porProfissional.map((x) => `<tr>
        <td>${esc(x.nome)}</td>
        <td class="num">${x.atendimentos}</td>
        <td class="num">${x.criticos > 0 ? `<span style="color:var(--critico)">${GLIFO.critico}</span> ` : ""}${x.criticos}</td>
        <td class="num">${x.alertas > 0 ? `<span style="color:var(--alerta)">${GLIFO.alerta}</span> ` : ""}${x.alertas}</td>
      </tr>`).join("")}</tbody>`

    graficoRegras($<HTMLDivElement>("grafico-regras"), rel.porRegra)
}

function iniciar(): void {
    if (!rel) {
        document.body.innerHTML = "<div class=\"wrap\"><div class=\"vazio\">Relatório não encontrado. Rode <code>npm run scrape</code> primeiro.</div></div>"
        return
    }

    renderTopo()
    selecionada = rel.prontuarios[0]?.chave ?? null
    renderLista()
    renderDetalhe()

    for (const id of [ "busca", "f-severidade", "f-profissional", "f-regra" ])
        $<HTMLElement>(id).addEventListener("input", renderLista)

    $<HTMLButtonElement>("tema").addEventListener("click", () => {
        const atual = document.documentElement.getAttribute("data-theme")
        const escuro = atual === "dark" || (atual === null && window.matchMedia("(prefers-color-scheme: dark)").matches)
        document.documentElement.setAttribute("data-theme", escuro ? "light" : "dark")
        graficoRegras($<HTMLDivElement>("grafico-regras"), rel.porRegra)
        renderDetalhe()
    })

    let redimensiona: number | undefined
    window.addEventListener("resize", () => {
        window.clearTimeout(redimensiona)
        redimensiona = window.setTimeout(() => {
            graficoRegras($<HTMLDivElement>("grafico-regras"), rel.porRegra)
            renderDetalhe()
        }, 150)
    })
}

iniciar()
