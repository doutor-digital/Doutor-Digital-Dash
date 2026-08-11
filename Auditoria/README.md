# Auditoria de Prontuários

Varre os atendimentos de uma unidade em `app.doutorhernia.com.br`, extrai o
prontuário completo de cada tratamento e roda 16 regras de auditoria sobre a
evolução, o questionário de incapacidade, o CBDF e o prognóstico. Publica um
dashboard estático com os achados ranqueados.

Escopo padrão: **IMPERATRIZ - MA**, mês corrente.

## Por que scraping

Não existe endpoint que devolva atendimento ou evolução em JSON. A aplicação é
CodeIgniter com renderização server-side; o que há de API é parcial:

| Endpoint | Retorno |
|---|---|
| `GET /pacientes/getOne/{id}` | JSON do paciente |
| `GET /pacientes/get` | JSON — **base inteira de pacientes** |
| `GET /atendimentos/get_historic_anamnese/{cli}/{trat}` | JSON |
| `GET /atendimentos/listagem/{offset}` | HTML paginado (20/página) |
| `POST /atendimentos/filtrar` | HTML — filtros `id_company`, `id_staff`, `created`, `keyword_attendance` |
| `GET /atendimentos/acompanhar/{id}` | HTML (~290 KB) com evolução, anamnese, CBDF, prognóstico |

Duas armadilhas do filtro, ambas já tratadas em `src/client.ts`:

- `created` **não pode ir vazio**. Com string vazia o controller descarta o
  filtro inteiro e devolve todas as unidades. O formato é `DD/MM/AAAA - DD/MM/AAAA`.
- O filtro fica na sessão do servidor, não na URL — precisa ser aplicado antes
  de paginar.

## Uso

Leia o cookie de sessão do Chrome já logado:

```bash
browser-harness <<'PY'
cs = cdp("Network.getCookies", urls=["https://app.doutorhernia.com.br"])["cookies"]
print(next(c["value"] for c in cs if c["name"] == "sessions"))
PY
```

```bash
npm install
DH_SESSION=<cookie> npm run scrape
npm run serve            # http://localhost:4123
```

| Variável | Padrão | Efeito |
|---|---|---|
| `DH_SESSION` | — | cookie `sessions` (obrigatório) |
| `DH_UNIDADE` | `133` (Imperatriz) | `id_company` |
| `DH_PERIODO` | mês corrente | `DD/MM/AAAA - DD/MM/AAAA` |
| `PORTA` | `4123` | porta do dashboard |

O dashboard precisa de HTTP: é ES module, e o Chrome bloqueia módulos em
`file://`. Daí o `npm run serve`.

## Unidade de auditoria é o tratamento, não o atendimento

`/acompanhar/{id}` abre a **mesma ficha** para todas as sessões de um
tratamento — evolução, questionário e CBDF pertencem ao tratamento. Auditar por
atendimento multiplicaria cada achado pelo número de sessões (na primeira
execução, 84 atendimentos viraram 84 fichas em vez de 53). O scraper agrupa por
`id_treatment`; avaliações avulsas ("Iniciando Avaliação", sem aba de evolução)
viram registro próprio e ficam fora das regras de tratamento.

## Regras

Escore de risco = crítico × 10 + alerta × 3 + info × 1.

**Críticos** — comprometem a auditabilidade do prontuário:

| Regra | O que detecta |
|---|---|
| `questionario-retroativo` | Roland-Morris "inicial" criado dias/meses após a 1ª consulta — mede memória, não estado |
| `alta-sem-testes` | evolução de alta que não nomeia nenhum teste objetivo (Lasègue, Slump, reflexos, força, sensibilidade) |
| `sessao-relampago` | atendimento com menos de 5 minutos |
| `atendido-sem-evolucao` | tratamento com sessões ATENDIDO e aba Evolução vazia |

**Alertas** — inconsistências de registro:

`dia-duplicado`, `cabecalho-x-corpo`, `protocolo-repetido`, `gap-sessoes`
(> 14 dias sem justificativa), `eva-sem-final`, `eva-classificacao` (adjetivo
incoerente com o número), `contador-divergente`, `cbdf-desatualizado`,
`prognostico-ausente`, `alta-sessao-curta`, `questionario-contradiz-evolucao`.

**Info**: `protocolo-estourado` (encerrado além do prazo do plano).

## Estrutura

```
src/types.ts    contrato compartilhado (Node + browser)
src/client.ts   HTTP com cookie, throttle de 350ms, backoff
src/parse.ts    HTML → estrutura
src/audit.ts    as 16 regras
src/scrape.ts   orquestra e agrupa por tratamento
src/serve.ts    servidor estático
web-src/app.ts  dashboard (SVG à mão, sem framework)
```

Saída em `data/relatorio.json` e `web/relatorio.js`. **Ambos ficam fora do git**
— contêm nome, idade, queixa e evolução clínica de paciente.

## Dois bugs da aplicação de origem

Encontrados durante a auditoria, não corrigíveis aqui:

- **Duração epoch**: atendimentos EM ANDAMENTO exibem duração de ~29.774.500
  minutos (≈ 56 anos) na listagem. O scraper descarta durações acima de 1440 min.
- **`GET /pacientes/get` vaza a base inteira**: 8,2 MB com nome, CPF, e-mail,
  WhatsApp e data de nascimento de **todos** os pacientes, para qualquer usuário
  autenticado, sem filtro de unidade — enquanto a interface mostra só a unidade
  ativa. Dado sensível de saúde: LGPD Art. 11.
