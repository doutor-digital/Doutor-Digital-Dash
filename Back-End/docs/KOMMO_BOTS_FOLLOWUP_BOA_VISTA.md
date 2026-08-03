# Automação de atendimento — Doutor Hérnia Boa Vista

Documentação da régua de bots implantada na Kommo de Boa Vista: o que cada bot faz,
quando dispara, por que foi desenhado assim e como reproduzir em outra unidade.

Conta: `boavistarrdoutorhernia` · funil **COMERCIAL** (`14213383`).

---

## 1. O desenho em uma frase

Cada etapa do funil tem **uma mensagem de entrada** (dispara quando o lead chega
ali) e uma **régua de silêncio** (dispara quando o paciente para de responder).
São dois relógios diferentes, e é por isso que não se atropelam: um conta *entrada
em etapa*, o outro conta *tempo desde a última mensagem do paciente*.

```
            ┌─ entrada na etapa ──► 1 mensagem imediata
  ETAPA ────┤
            └─ silêncio do paciente ──► 2h → 1d → 3d → 7d → 15d
```

## 2. Mapa das automações (11 ativas)

### Mensagem de entrada de etapa

| Etapa | Quando | Bot | Mensagem rápida |
|---|---|---|---|
| Etapa de leads de entrada | lead criado | ENT 01 · Boas-vindas | 01 \| Boas-vindas |
| EM QUALIFICAÇÃO | movido ou criado | QLF 01 · Método & autoridade | 16 \| Autoridade + método |
| COMPARECEU | movido ou criado | NEG 01 · Cirurgia × tratamento | 27 \| Anchoring cirurgia vs tratamento |
| EM NEGOCIAÇÃO | movido ou criado | NEG 02 · Caso real de paciente | 29 \| Storytelling caso real |

### Régua de silêncio (tempo desde a última mensagem do paciente)

| Tempo | Bot | Mensagem | Vale nas etapas |
|---|---|---|---|
| 2h | FUP 01 | 13 \| Follow-up "vou pensar" | EM QUALIFICAÇÃO, AGENDADO |
| 1 dia | FUP 02 | 17 \| Prova social — depoimento | EM QUALIFICAÇÃO, AGENDADO |
| 3 dias | FUP 03 | 21 \| Custo de adiar | EM QUALIFICAÇÃO, AGENDADO |
| 1 dia | NEG 03 | 12 \| Objeção preço | COMPARECEU, EM NEGOCIAÇÃO |
| 3 dias | NEG 04 | 22 \| Ligação de 5 minutos | COMPARECEU, EM NEGOCIAÇÃO |
| 7 dias | FUP 04 | 14 \| Reativação de lead frio | as quatro etapas ativas |
| 15 dias | FUP 05 | 30 \| Break-up | as quatro etapas ativas |

O escopo por etapa é o que evita a colisão: quem está no começo do funil recebe a
régua de *silêncio comercial*; quem já compareceu recebe a régua de *decisão*
(preço, ligação), que fala de outra coisa.

### Bots criados sem gatilho (uso manual)

| Bot | Por quê |
|---|---|
| FUP · No-show | Falta não é evento da Kommo — quem sabe que o paciente não veio é o CRM da franquia, integração ainda sem token |
| ENT 02 · Fora do horário | Depende de configurar o horário comercial (`work_time`) na conta |
| QLF 02 · Perguntas de qualificação | Deixado manual para a SDR usar quando o lead responde |
| QLF 03 · Convite para agendar | Idem — disparar automaticamente antes de qualificar queima o convite |

## 2-A. Os cinco fluxos ramificados

Cada etapa tem um **fluxo**, não uma mensagem solta: envia → espera → lê a resposta →
ramifica → grava campo → avisa a SDR. Total: 34 blocos, 83 ações.

| Etapa | Fluxo | Blocos | Campos que grava |
|---|---|---|---|
| Entrada | ENT 01 · Boas-vindas | 6 | Tipo de lead, Data do primeiro contato, Interação |
| EM QUALIFICAÇÃO | QLF 01 · Qualificação | 10 | Status da conversa, Data da qualificação, Interação, Queixa, Plano de saúde, Qualificação Q/M/F, Intenção, Nº de tentativas |
| COMPARECEU | NEG 01 · Pós-consulta | 6 | Status da conversa, Intenção, Qualificação |
| EM NEGOCIAÇÃO | NEG 02 · Negociação | 8 | Status da conversa, Intenção, Motivo de não fechamento |
| Régua 15 dias | FUP 05 · Break-up | 4 | Status da conversa, Motivo do não agendamento, Interação |

O ganho: a queixa do paciente entra no campo certo, o termômetro Quente/Morno/Frio é
preenchido pela própria resposta, e o motivo de não fechamento nasce classificado a
partir do que o paciente escreveu — é isso que alimenta o card de motivos do dashboard
sem depender de digitação da SDR.

Blocos usados: `send_message`, `wait` (por tempo e por resposta), `conditions` + `goto`,
`action → set_custom_fields`, `send_internal`. Detalhe bloco a bloco no PDF
`Manual_Bots_Doutor_Hernia_Boa_Vista.pdf`.

**Gotchas do fluxo:** bloco `finish` não pode ter entrada no `text` (400); cada bloco
precisa de `step` ÚNICO — steps repetidos fazem um ramo inteiro sumir em silêncio.

## 2-B. Mensagem agendada pela SDR

A Kommo não tem "agendar mensagem". O que existe é o evento `121 relative_date` (relativo a
um campo de data), e a configuração dele **não é alcançável por API**: testei três formatos
e a Kommo gravou `relative_date: null` nos três, sem erro. Precisa de um exemplo criado na
interface para ler o JSON e replicar.

A saída foi outra, e é melhor: **`POST /api/v2/salesbot/run`** dispara qualquer bot em
qualquer lead, sob comando, com o token público. Com isso o agendamento é por lead e quem
decide é a pessoa, não uma regra de funil.

**Como a SDR usa:** abre o cartão, preenche `◷ Enviar mensagem em` (data e hora) e escolhe
em `⬢ Mensagem a enviar` uma das 30 mensagens rápidas. Só isso.

**O que roda por trás** (`docs/n8n/kommo-mensagens-agendadas-boa-vista.json`, a cada 5 min):

1. Busca leads com `Enviar mensagem em <= agora` (filtro por campo de data na API v4).
2. Descarta quem não escolheu mensagem ou já tem `Mensagem enviada em` — essa é a trava
   contra reenvio.
3. Resolve o bot da mensagem escolhida (mapa de 30 entradas embutido no fluxo).
4. `salesbot/run` no lead.
5. Carimba `◷ Mensagem enviada em` e limpa os dois campos de agendamento.

Campos criados em Boa Vista: `496166` (enviar em), `496168` (qual mensagem, 30 opções),
`496170` (enviado em). Toda mensagem rápida tem um bot correspondente — 20 criados para
isso, 10 reaproveitados dos que já existiam.

Opção sem bot correspondente não fica em silêncio: o fluxo emite um item de erro em vez de
engolir o disparo.

**Pré-requisito para ligar:** variável `KOMMO_TOKEN_BOA_VISTA` no n8n. E vale um teste
supervisionado com um número da equipe antes de liberar — o `salesbot/run` não foi executado
contra lead real, justamente para não mandar mensagem a paciente sem você ver.

## 3. Por que cada escolha

**Um bot por mensagem, e não um bot com vários passos.** A Kommo permite blocos de
espera dentro do bot, mas isso amarra a régua inteira em um objeto só: mudar o
intervalo do passo 3 exige mexer no fluxo todo, e desligar um passo isolado é
inviável. Com um bot por mensagem, o tempo mora no gatilho — trocar "3 dias" por
"5 dias" é editar um número.

**O relógio conta a partir da mensagem do paciente, não da nossa.** O evento é
`last_incoming_message`. Quem responde sai da régua automaticamente, sem precisar
de tag, lista de exclusão ou intervenção da SDR.

**Nada dispara para a base importada.** Os 4.476 leads migrados do CRM antigo
entraram por API, sem conversa. O gatilho depende de existir mensagem recebida,
então nenhum deles é alcançado — inclusive os 243 que pediram para não ser
incomodados.

**Nenhuma mensagem automática tem placeholder.** Oito das trinta mensagens rápidas
ainda têm `[preencher]`, `[HORÁRIO]`, `[colar link do Google]`. Todas ficaram fora
da automação de propósito: paciente recebendo `[preencher endereço completo]` é
pior do que não receber nada.

## 4. Riscos conhecidos

**O passo de 2h pode cortar conversa humana.** O gatilho não sabe se a SDR está
atendendo naquele momento — ele só vê que o paciente não escreve há 2h. Se
incomodar, suba para 4–6h: é editar o `delay` da ação `37254856`.

**O save do funil é dono de todas as ações.** Salvar a tela "Automatize" pela
interface aplica um diff sobre o conjunto inteiro de ações daquele funil. Ações
criadas por fora (endpoint de triggers) que não estiverem no payload são
**apagadas**. Por isso as 11 automações vivem no mesmo lugar — inclusive as de
tempo.

## 5. Como foi feito (reprodutível em outra unidade)

Salesbot e automação de funil **não existem na API v4 pública**. São API privada da
interface, autenticada por **cookie de sessão** (`session_id`) + header
`X-Requested-With: XMLHttpRequest`. As mensagens rápidas, essas sim, estão na v4.

```
# 1. mensagens rápidas (API pública, Bearer token)
GET  /api/v4/chats/templates?limit=250

# 2. criar bot
POST /ajax/v2/salesbot/       {"salesbot":[{ "id":0, "name":…, "positions":"<json>", "text":"<json>" }]}
     positions/text são STRINGS de JSON (json dentro de json);
     a mensagem em si é só params.template_id

# 3. apagar bot
DELETE /api/v1/salesbot/{id}          → {"deleted":true}
     ⚠️ mandar is_deleted:true no POST do ajax NÃO apaga: cria outro bot

# 4. catálogo de eventos, handlers e delays
GET  /ajax/settings/pipeline/leads/{pipeline_id}     (estado atual: statuses + actions)
GET  /ajax/v4/triggers/bots                          (condition_variants, handlers)

# 5. salvar automações (form-urlencoded, diff do conjunto TODO)
POST /ajax/settings/pipeline/leads/{pipeline_id}/save?pipeline_stats=Y&skip_filter=Y
     statuses[i][...]   → cópia fiel do estado atual, senão etapa some
     actions[j][...]    → settings[bot_id], execution_condition[id|name], delay, statuses[]
```

Eventos úteis: `1` lead_added · `15` lead_appeared_in_status · `141`
last_incoming_message (o `delay` em segundos é o intervalo) · `119` talk_created ·
`121` relative_date (caminho para lembrete de véspera de consulta, quando o campo
`◷ Data da Consulta` estiver sendo preenchido).

## 6. Próximos passos

1. **Preencher os 8 templates com placeholder** — destrava lembrete de véspera,
   confirmação no dia, endereço e pedido de avaliação no Google.
2. **Lembrete de consulta** com evento `121 relative_date` sobre `◷ Data da Consulta`
   (véspera e 2h antes). Depende do item 1 e do campo ser preenchido no agendamento.
3. **No-show automático** quando a integração com o CRM da franquia for liberada.
4. **Horário comercial** (`work_time`) para ligar o ENT 02 fora do expediente.
