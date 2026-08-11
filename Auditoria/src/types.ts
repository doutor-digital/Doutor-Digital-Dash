/*
 *  Modelo de dados da auditoria de prontuários.
 *
 *  A aplicação de origem (app.doutorhernia.com.br) é CodeIgniter com renderização
 *  server-side: não existe endpoint que devolva atendimento ou evolução em JSON.
 *  Estes tipos são o contrato que o scraper produz a partir do HTML e que o
 *  dashboard consome — o mesmo arquivo é compilado para Node e para o browser.
 */

export type Severidade = "critico" | "alerta" | "info"

/*  Linha da listagem /atendimentos/listagem/{offset}  */
export interface Atendimento {
    id:              number
    paciente:        string
    inicio:          string | null
    termino:         string | null
    duracaoMin:      number | null
    fisioterapeuta:  string
    unidade:         string
    situacao:        string
}

/*  Um registro de evolução dentro da aba "Evolução"  */
export interface Evolucao {
    data:          string
    dataISO:       string | null
    profissional:  string
    protocolo:     string
    diaRotulo:     number | null
    diaCorpo:      number | null
    evaInicial:    number | null
    evaFinal:      number | null
    texto:         string
}

/*  Roland-Morris (24 itens). `criadoEm` é o campo decisivo da auditoria.  */
export interface Questionario {
    criadoEm:        string | null
    criadoEmISO:     string | null
    escoreInicial:   number | null
    escoreFinal:     number | null
    respondidos:     number
}

/*
 *  A aplicação abre a MESMA ficha em /acompanhar/{id} para todos os atendimentos
 *  de um tratamento — evolução, questionário e CBDF são do tratamento, não da
 *  sessão. Auditar por atendimento multiplicaria cada achado pelo número de
 *  sessões, então a unidade de auditoria é o tratamento; `atendimentos` guarda
 *  todas as sessões dele. Avaliações avulsas (sem tratamento) viram registro
 *  próprio, com uma única sessão.
 */
export type TipoRegistro = "tratamento" | "avaliacao"

export interface Prontuario {
    chave:             string
    tipo:              TipoRegistro
    atendimentos:      Atendimento[]
    principal:         Atendimento
    idClient:          number | null
    idTreatment:       number | null
    nomePaciente:      string
    idade:             number | null
    plano:             string
    primeiraConsulta:  string | null
    primeiraISO:       string | null
    realizados:        number | null
    previstos:         number | null
    esteAtendimento:   number | null
    anamnese:          Record< string, string >
    evolucoes:         Evolucao[]
    questionario:      Questionario | null
    cbdf:              string[]
    prognostico:       string | null
}

export interface Achado {
    regra:       string
    severidade:  Severidade
    titulo:      string
    detalhe:     string
}

export interface ProntuarioAuditado extends Prontuario {
    achados:  Achado[]
    escore:   number
}

export interface Relatorio {
    geradoEm:      string
    unidade:       string
    periodo:       string
    total:         number
    atendimentos:  number
    avaliacoes:    number
    comAchados:    number
    criticos:      number
    alertas:       number
    prontuarios:   ProntuarioAuditado[]
    porRegra:      Array< { regra: string, severidade: Severidade, titulo: string, total: number } >
    porProfissional: Array< { nome: string, atendimentos: number, criticos: number, alertas: number } >
}

/*  Peso de cada severidade no escore de risco do prontuário  */
export const PESO: Record< Severidade, number > = {
    critico: 10,
    alerta:   3,
    info:     1
}
