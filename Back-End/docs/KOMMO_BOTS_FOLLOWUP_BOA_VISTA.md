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
