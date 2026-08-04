# Troca das automações — Doutor Hérnia Imperatriz

Conta `attivacorpoementeitz` (36459431) · levantado em 04/08/2026 · **nada foi alterado ainda**

Estado atual salvo em `backup-2026-08-04/` (pipelines e os 50 bots). É o que permite desfazer.

---

## 1. O que existe hoje

**43 automações ativas**: 31 no COMERCIAL, 12 no TRATAMENTO. Elas se apoiam em 33 bots,
dos quais 22 mandam mensagem e 11 só gravam campo.

A estrutura está certa — e isso é importante dizer, porque contraria o que eu tinha assumido
lendo a experiência de Boa Vista. **O gatilho por data (`relative_date`) está configurado e
funcionando**: 7 automações no AGENDADO disparam antes da data em `2440909 ◷ Data de agendamento`,
5 no COMPARECEU disparam depois dela, e no TRATAMENTO três se apoiam em `2440965 ◷ Próxima sessão`
e uma em `2440973 ◷ Última sessão marcada`.

O problema não é a automação. É que `2440909` só tem valor nos 67 leads da migração — nenhum lead
novo recebe data. **Onze automações estão montadas, corretas e nunca disparam.**

### Os 11 bots que não mandam mensagem

Gravam campo, carimbam data, movem etapa. **Nenhum deles muda.**

| Bot | O que faz |
|---|---|
| 99477 | `[RASTREAMENTO]` origem e data de entrada |
| 99487 | `[COMPARECEU]` |
| 99489 | `[VENDA GANHA]` data de fechamento |
| 99493 | `[TRATAMENTO]` paciente perdido |
| 99541 | `[AGENDADO]` verifica comparecimento |
| 99609 | `[COMPARECEU]` fechou tratamento |
| 99629 | `[EM TRATAMENTO]` data de início |
| 99631 | `[EM TRATAMENTO]` reset próxima sessão |

Fora deles, duas automações de conversão da Meta (`handle_conversions`: `lead` em EM QUALIFICAÇÃO,
`purchase` em COMPARECEU), uma de tag `IA-SOFIA` na entrada e quatro que marcam `2443055 Pausar IA`.
Também não mudam.

---

## 2. A régua de silêncio precisa encolher

Nove automações hoje disparam por `last_incoming_message_date` — tempo desde a última fala do
paciente. Esse é exatamente o relógio que a Sofia opera no `agente-dt`, e o
`follow-up-worker.ts` diz que só pode haver um:

> *"O Salesbot é o CANAL, nunca um segundo motor de follow-up. Dois relógios mirando o mesmo lead
> disparam sem se ver, e mensagem duplicada no WhatsApp não tem desfazer."*

A escada da Sofia vai de 5 minutos a 20 horas. Tudo que o Kommo dispara **dentro** dessa faixa é
mensagem em duplicidade. O que sobra é o que fica fora da janela de 24h.

| Hoje | Bot | Mensagem atual | Proposta |
|---|---|---|---|
| 2h | 99517 | E01_REFORCO_2H | **desligar** — Sofia cobre (degrau 2h) |
| 4h | 99503 | E01_REFORCO_4H | **desligar** — Sofia cobre (degrau 6h) |
| 4h | 99525 | E03_CONVITE_CONSULTA | **desligar** — Sofia cobre |
| 24h | 99507 | E01_IMPACTO_24H | **desligar** — Sofia cobre (degrau 20h) |
| 24h | 99527 | E03_AUTORIDADE | **desligar** — Sofia cobre |
| 48h | 99509 | E02_POS_LIGACAO_48H | `ITZ_C08_TERMOMETRO_72H_AUTO` |
| 72h | 99511 | E02_TERMOMETRO_72H | **desligar** — toque a mais, sem ângulo novo |
| 96h | 99513 | E02_RETOMADA_96H | `ITZ_C05_METODO_AUTORIDADE_AUTO` |
| 120h | 99515 | E02_PORTA_ABERTA_120H | `ITZ_C09_PORTA_ABERTA_120H_AUTO` |

**De nove toques para três.** Os seis desligados não são perda: cinco viram degrau da Sofia, com
texto escrito a partir da conversa real em vez de mensagem fixa.

---

## 3. AGENDADO — lembretes por data

Todos apoiados em `2440909 ◷ Data de agendamento`. Só voltam a disparar quando esse campo passar
a ser preenchido no agendamento.

| Quando | Bot | Mensagem atual | Proposta |
|---|---|---|---|
| 3 dias antes | 99529 | E04_OFERTA_PREPAGAMENTO | **falta template novo** — ver seção 6 |
| 2 dias antes | 99531 | E04_DADOS_PIX | **falta template novo** — ver seção 6 |
| 1 dia antes (sem pagamento) | 99533 | E04_CONFIRMA_VESPERA | `ITZ_C11_LEMBRETE_VESPERA_SDR` |
| 1 dia antes (pago) | 99537 | E05_CONFIRMA_VESPERA | `ITZ_C11_LEMBRETE_VESPERA_SDR` |
| 2h antes (sem pagamento) | 99535 | E04_CONFIRMA_DIA | `ITZ_C12_CONFIRMACAO_DIA_AUTO` |
| 2h antes (pago) | 99539 | E05_CONFIRMA_DIA | `ITZ_C12_CONFIRMACAO_DIA_AUTO` |
| 1h antes | 99541 | *(utilitário)* | não muda |

Hoje há dois caminhos, pago e não pago, com textos diferentes. Na proposta os dois usam a mesma
mensagem — a diferença de pagamento é assunto da Sofia dentro da conversa, não de dois lembretes
de véspera concorrentes. Se você preferir manter separado, é uma linha a mais no catálogo.

**Acrescentar:** `ITZ_C13_PREPARO_CONSULTA_AUTO` (o que levar) uma hora depois de entrar em
AGENDADO. É a única mensagem dessa faixa que não depende de data e por isso já pode rodar hoje.

---

## 4. COMPARECEU — régua de decisão

Apoiada em `2440909`, contando *depois* da consulta. Mesma dependência de campo.

| Quando | Bot | Mensagem atual | Proposta |
|---|---|---|---|
| 2h depois | 99595 | E07_POS_CONSULTA_D1 | `ITZ_C17_POS_CONSULTA_D1_AUTO` |
| 24h depois | 99597 | E07_OBJ_VALOR | `ITZ_C19_OBJECAO_VALOR_AUTO` |
| 72h depois | 99599 | E07_OBJ_FAMILIA_MEDICO | `ITZ_C20_OBJECAO_FAMILIA_MEDICO_AUTO` |
| 96h depois | 99601 | E07_RESGATE_D3 | `ITZ_C22_RETOMADA_NEGOCIACAO_D3_AUTO` |
| 99h depois | 99603 | E07_RESGATE_D7 | `ITZ_C23_ENCERRAMENTO_D7_AUTO` |

O 99h chama-se "168h" no nome do bot mas está configurado em 356400 segundos, que é 99 horas.
Ou o nome está errado, ou o valor. **Proposta: 168h (7 dias)**, coerente com o nome e com o
espaçamento dos anteriores.

---

## 5. TRATAMENTO

| Quando | Bot | Mensagem atual | Proposta |
|---|---|---|---|
| 1 dia antes da sessão | 99611 | E08_SESSAO_VESPERA | `ITZ_T01_SESSAO_VESPERA_AUTO` |
| 2h antes da sessão | 99613 | E08_SESSAO_DIA | `ITZ_T02_SESSAO_DIA_AUTO` |
| 2h após última sessão | 99615 | E08_FALTOU_SESSAO | `ITZ_T03_FALTA_SESSAO_AUTO` |
| entrada em EM TRATAMENTO | 99617 | E08_INCENTIVO_MEIO | `ITZ_T04_INCENTIVO_MEIO_PLANO_AUTO` |
| mudança de etapa | 99619 | E10_REVERSAO | `ITZ_T06_CANCELAMENTO_ACOLHIMENTO_AUTO` |

Estas quatro por data se apoiam em `2440965 ◷ Próxima sessão` e `2440973 ◷ Última sessão marcada`,
que **são preenchidos** — diferente do funil comercial. É a parte da régua que funciona hoje.

**Acrescentar quando fizer sentido:** `ITZ_T08` (retorno antes de encerrar) em TRATAMENTO
CANCELADO, `ITZ_R01` e `ITZ_R02` (parabéns e NPS) em ALTA, e os quatro do financeiro
(`ITZ_F01`–`F04`) quando o webhook do Asaas estiver ligado.

---

## 6. Dois templates que faltam no catálogo novo

O catálogo antigo tem duas mensagens sem equivalente entre as 43, e as duas são de dinheiro:

| Faltando | Para quê | Categoria |
|---|---|---|
| `ITZ_C28_OFERTA_PREPAGAMENTO` | consulta a R$ 350, R$ 150 garantindo no PIX — 3 dias antes | Marketing |
| `ITZ_C29_DADOS_PIX` | chave e favorecido, depois que o paciente aceita | Utility |

Faz diferença real: a própria Sofia tem um degrau inteiro dedicado ao antecipado, com a razão
escrita no código — *"R$ 150 garantido contra R$ 350 que talvez não venha, e quem paga antes
falta menos"*. Preciso da chave PIX e do nome do favorecido para escrever a segunda.

---

## 7. Ordem de execução

1. **Aprovar os templates na Meta** (seção 6 do PDF) — nada abaixo funciona sem isso.
2. Criar `ITZ_C28` e `ITZ_C29`.
3. Padronizar o preenchimento de `2440909` com **data e hora** no agendamento. Sem isso,
   as seções 3 e 4 continuam montadas e paradas.
4. Criar os bots novos, um por mensagem.
5. Trocar o `bot_id` de cada automação, uma a uma.
6. Desligar os seis toques da seção 2.
7. Acrescentar os quatro degraus fora da janela em `follow-up-presets.ts` do `agente-dt`.

Os passos 4 a 6 são um único `POST` no funil, e é o passo perigoso: salvar a tela de automações
aplica um diff sobre **todas** as ações daquele funil, e o que não estiver no payload é apagado.
Por isso o backup da pasta `backup-2026-08-04/` é pré-requisito, não zelo.

---

## 8. Resumo da mudança

| | Hoje | Proposta |
|---|---|---|
| Automações COMERCIAL | 31 | 26 |
| Automações TRATAMENTO | 12 | 12 |
| Toques de silêncio no Kommo | 9 | 3 |
| Degraus de silêncio na Sofia | 5 (dentro de 24h) | 5 + 4 fora da janela |
| Bots utilitários | 11 | 11, intocados |
| Mensagens sem substituto | — | 2 (pré-pagamento e PIX) |
