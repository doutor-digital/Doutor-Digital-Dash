# Filtros salvos da Kommo — Doutor Hérnia Boa Vista

A API v4 da Kommo **não expõe filtros salvos** (`leads/filters`, `filters`,
`custom_filters`, `account/filters` → todos 404). Filtro salvo é objeto de
interface: só dá para criar clicando. Este documento é a receita de cada um,
com a contagem esperada da base migrada (4.476 leads, jul/2026).

Como criar: seção **Leads** → abre o painel de filtro → monta os critérios →
**Salvar** com o nome indicado. Filtro salvo fica ao lado de "Leads ativos".

> "Leads ativos" é filtro de sistema e exclui ganho/perdido — por isso ele mostra
> só 462 dos 4.476. Não é defeito, é o certo para a tela de trabalho do SDR.

## Operação do dia a dia

| Nome | Critérios | Esperado |
|---|---|---|
| **Base completa** | Etapa: marcar TODAS, inclusive ganho e perdido dos 2 funis | 4.476 |
| **Trabalhando agora** | Funil COMERCIAL · Etapas: EM QUALIFICAÇÃO, AGENDADO, COMPARECEU, EM NEGOCIAÇÃO | 336 |
| **Agendados** | Funil COMERCIAL · Etapa: AGENDADO | 64 |
| **Compareceu, não fechou** | Funil COMERCIAL · Etapa: EM NEGOCIAÇÃO | 121 |
| **Pacientes em tratamento** | Funil TRATAMENTO · Etapa: EM TRATAMENTO | 126 |
| **Altas** | Funil TRATAMENTO · Etapa de ganho (ALTA) | 26 |

## Campanhas de resgate (a base morta é aqui)

| Nome | Critérios | Esperado |
|---|---|---|
| **Resgate · não deu continuidade** | Etapa: perdido (COMERCIAL) · Motivo de perda: Não deu continuidade ao atendimento | 2.083 |
| **Resgate · não interagiu** | Etapa: perdido (COMERCIAL) · Motivo de perda: Não interagiu | 1.517 |
| **Resgate · plano de saúde** | Etapa: perdido · Motivo de perda: Plano de saúde | 74 |
| **Resgate · no-show e desmarcou** | Campo `⬢ Tipo de lead` = Resgate | 17 |
| **Já agendou alguma vez** | Campo `✓ Agendou` = Sim | 234 |
| **⛔ NÃO DISPARAR** | Motivo de perda: Não perturbar | 243 |

O filtro **⛔ NÃO DISPARAR** existe para ser *subtraído* de qualquer disparo: são
os leads que pediram para não ser incomodados (inclui os 42 marcados como
bloqueados no CRM antigo). Conferir antes de toda campanha.

## Origem / mídia

| Nome | Critérios | Esperado |
|---|---|---|
| **Origem · Meta Facebook** | Campo `⚑ Origem` = Meta-Facebook | 2.221 |
| **Origem · Meta Instagram** | Campo `⚑ Origem` = Meta-Instagram | 1.409 |
| **Origem · Google** | Campo `⚑ Origem` = Google | 190 |
| **Origem · Indicação** | Campo `⚑ Origem` = Indicação | 39 |
| **Sem origem identificada** | Campo `⚑ Origem` = Sem origem | 607 |

Os 607 sem origem são o custo da migração: no CRM antigo a origem vinha no
primeiro token da coluna `Tags`, e nesses registros ela estava vazia ou era só
histórico de disparo em massa.

## Higiene da base

| Nome | Critérios | Esperado |
|---|---|---|
| **Importados sem data de criação** | Data de criação = hoje (dia do import) | 7 |
| **Recência: sem contato há 6+ meses** | Campo `▷ Última interação (importado)` até a data de 6 meses atrás | varia |

`▷ Última interação (importado)` só existe em Boa Vista — é a data da última
mensagem no CRM antigo, preservada para priorizar resgate por recência.

## Depende de renomear ganho/perdido na UI

Enquanto o COMERCIAL tiver `143 = "Consulta cancelada – perdido"` (herdado do
template), qualquer filtro por etapa de perda continua funcionando na Kommo, mas
o **dashboard** classifica esses 3.980 leads como tratamento cancelado, porque
resolve etapa por nome e testa "CANCELAD" antes de "PERDIDO". Renomear para
`PERDIDO` / `GANHO / CONCLUÍDO` (COMERCIAL) e `TRATAMENTO CANCELADO` / `ALTA`
(TRATAMENTO) antes de plugar a unidade.
