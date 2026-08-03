# Rastreio de campanhas — Click-to-WhatsApp → Kommo

De qual anúncio veio cada paciente, gravado no cartão da Kommo, **sem tocar na
integração que hoje entrega as mensagens**.

Unidade piloto: Boa Vista (`boavistarrdoutorhernia`).
Fluxo: `docs/n8n/rastreio-campanhas-whatsapp-kommo.json`.

---

## 1. A arquitetura, e por que ela não quebra a Kommo

O ponto que decide tudo: **a Meta permite vários apps inscritos no mesmo WABA**
(`POST /{WABA_ID}/subscribed_apps`), e todos os inscritos recebem o mesmo evento.

Isso significa que **não desviamos, não substituímos e não encadeamos** o webhook da
Kommo. Inscrevemos um app nosso ao lado dela; a Meta passa a entregar o evento nos dois
endereços, em paralelo e de forma independente. Se o nosso n8n cair, a Kommo continua
recebendo mensagem normalmente — e vice-versa.

```
                        ┌─────────────► Kommo  ──► conversa, funil, bots   (INALTERADO)
  clique no anúncio     │
   → WhatsApp ──► Meta ─┤
                        └─────────────► n8n ──► lê o `referral` ──► grava atribuição
                                                 (via API oficial da Kommo)
```

A camada de rastreio é **somente leitura** sobre o evento e **escrita apenas em campos de
atribuição** do lead. Ela nunca responde ao paciente, nunca move etapa e nunca cria
conversa.

## 2. De onde vem o dado

Quando o primeiro contato nasce de um anúncio Click-to-WhatsApp, a mensagem inicial traz
um objeto `referral`:

| Campo do `referral` | Significado | Campo na Kommo |
|---|---|---|
| `source_id` | id do anúncio | `⌂ Anúncio (ad)` · `⌂ Campanha` |
| `ctwa_clid` | identificador do clique | `⌂ ctwa_clid` |
| `headline` | título do anúncio | `⌂ Título do anúncio` |
| `source_url` | URL de origem | `⌂ URL de origem do clique` |
| `timestamp` da mensagem | primeiro contato | `◷ Data do primeiro contato` |
| `from` | telefone do lead | chave de busca na Kommo |

O `referral` só aparece na **primeira** mensagem da conversa. Mensagem seguinte não
repete o dado — por isso o fluxo precisa capturar na hora, e por isso a deduplicação é
por `message_id`, não por telefone.

## 3. Campos criados na Kommo (Boa Vista)

| id | campo | tipo |
|---|---|---|
| 498572 | `⌂ Anúncio (ad)` | texto |
| 498574 | `⌂ Título do anúncio` | texto |
| 498576 | `⌂ ctwa_clid` | texto |
| 498578 | `⌂ URL de origem do clique` | url |

Reaproveitados, já existentes: `⌂ Campanha` (397126), `⌂ Conjunto de anúncio` (397128),
`⚑ Origem` (397124), `◷ Data do primeiro contato` (397114) e os `utm_*` nativos da Kommo.

## 4. Os nós do fluxo

| Nó | O que faz |
|---|---|
| **Meta · verificação (GET)** + **Devolver hub.challenge** | responde a verificação do webhook da Meta |
| **Meta · eventos (POST)** | recebe os eventos; responde imediatamente (`onReceived`) para a Meta não reenviar |
| **Validar assinatura da Meta** | confere `X-Hub-Signature-256` (HMAC-SHA256 com o app secret) em comparação de tempo constante; assinatura inválida derruba a execução |
| **Extrair referral do anúncio** | fica só com mensagens que têm `referral` de anúncio; descarta status de entrega, mensagem comum e payload incompleto |
| **Deduplicar por message_id** | janela de 24h em memória do workflow; a Meta reenvia quando não recebe 2xx a tempo, e vários apps inscritos amplificam |
| **Kommo · achar contato pelo telefone** | busca o contato pela API oficial |
| **Esperar a Kommo criar o lead** | 20s de espera: o evento chega na Meta e na Kommo ao mesmo tempo, então o lead pode ainda não existir quando olhamos |
| **Kommo · buscar lead do contato** | busca o lead pelo telefone |
| **Montar atribuição (primeiro toque)** | monta o PATCH **só com campo vazio** — atribuição existente nunca é sobrescrita |
| **Tem campo a gravar?** | se tudo já estava preenchido, o fluxo para aqui sem tocar no CRM |
| **Kommo · gravar atribuição** | PATCH nos campos de origem |
| **Kommo · nota de auditoria** | nota no lead registrando anúncio e `ctwa_clid` gravados |

### Regra de atribuição: primeiro toque vence

Se o lead já tem origem preenchida, o fluxo **não sobrescreve**. Um paciente que voltou
por outra campanha não apaga a campanha que o trouxe — senão o relatório de origem passa
a refletir o último clique, não o que gerou o paciente.

### Lead que ainda não existe

O fluxo **não cria lead**. A Kommo cria o lead sozinha ao receber a mensagem; criar em
paralelo geraria duplicata para o mesmo telefone. A espera de 20s cobre a diferença de
tempo; se mesmo assim não achar, o item sai marcado como `semLead` e fica na execução
para auditoria.

## 5. Credenciais e variáveis

| O que | Onde | Uso |
|---|---|---|
| `META_APP_SECRET` | variável de ambiente do n8n | validar a assinatura do webhook |
| Verify token | digitado no Meta e no n8n | verificação inicial do webhook |
| Credencial **Kommo Long Lived API** | credencial do n8n (já existe para Boa Vista) | os três nós HTTP da Kommo |

Nenhum token fica escrito no workflow.

## 6. Passo a passo — Meta for Developers

1. **Confirme quem é dono do WABA.** Em Business Manager → Contas do WhatsApp, veja se o
   WABA do número aparece no seu portfólio. Se ele pertence ao portfólio da Kommo/BSP, você
   não consegue inscrever outro app — nesse caso o caminho é ler a origem do próprio
   webhook da Kommo, e o desenho muda.
2. Crie (ou use) um app em Meta for Developers com o produto **WhatsApp**.
3. Em **WhatsApp → Configuration → Webhook**: Callback URL =
   `https://webhook-n8n.doutordigitalconsultoria.com/webhook/ctwa-kommo-boa-vista`,
   Verify token = o mesmo configurado no n8n. Assine o campo **`messages`**.
4. Inscreva o app no WABA sem mexer no que já existe:
   `POST /{WABA_ID}/subscribed_apps` — confira com `GET /{WABA_ID}/subscribed_apps`
   que a inscrição da Kommo **continua listada**.
5. Copie o **App Secret** para `META_APP_SECRET` no n8n.

## 7. Plano de testes

| Cenário | Como testar | Esperado |
|---|---|---|
| Lead novo por anúncio | clicar no anúncio CTWA e mandar mensagem de um número que não existe no CRM | Kommo cria o lead; ~20s depois os 4 campos de rastreio aparecem preenchidos + nota de auditoria |
| Lead já existente | repetir com número que já é lead, **sem** origem preenchida | grava a atribuição no lead existente, sem criar outro |
| Lead com origem anterior | repetir com número que já tem `⌂ Anúncio` preenchido | nada é sobrescrito; execução termina em "Tem campo a gravar? = não" |
| Clique sem mensagem | clicar no anúncio e não enviar nada | nenhum evento chega — comportamento correto, o `referral` viaja na mensagem |
| Mensagem sem campanha | mandar mensagem direta ao número | descartada no "Extrair referral"; a Kommo segue atendendo normalmente |
| Evento duplicado | reenviar o mesmo evento pelo Graph API Explorer | segundo passa pela assinatura mas é descartado na deduplicação |
| Erro da API da Kommo | revogar o token na credencial e disparar | nó falha com `neverError` desligado no log; execução fica salva para reprocessar |
| Kommo intacta | durante todos os testes | conversa, funil e bots da Kommo funcionando como antes |

## 8. Relação com o que já existe

O projeto `Doutor-Digital-Origens` já tem `n8n/1-ctwa-origens-realtime.json`, que captura o
mesmo `referral` e posta no **nosso back-end** (`OriginEvent`, atribuição por telefone) para
alimentar o mini-dash de origens. Os dois podem coexistir: um alimenta o **dashboard**, este
alimenta o **CRM**. Se preferir um só, o caminho é acrescentar o passo de Kommo naquele
fluxo — mas mantê-los separados isola falhas, que é o que este documento pede.
