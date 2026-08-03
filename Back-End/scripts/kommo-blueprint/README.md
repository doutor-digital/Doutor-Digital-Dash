# Blueprint de estrutura da Kommo

Replica a estrutura de uma conta Kommo (funis, etapas, grupos, campos, enums,
campos obrigatórios por etapa, motivos de perda) em outra conta, de forma
**idempotente** e **versionada**.

Nasceu para provisionar a unidade de **Boa Vista** com a mesma estrutura de
**Imperatriz** (unit 15), depois que a migração da ITZ mostrou o custo de fazer
isso com script descartável: os scripts das 8 fases moravam no scratchpad e se
perderam, sobrando só a conta viva como fonte de verdade.

## Arquivos

| arquivo | o que é |
|---|---|
| `kommo.py` | cliente da API v4 com os gotchas embutidos (paginação `order[id]=asc`, retry, `patch_bisect`) |
| `export_account.py` | lê uma conta (**somente leitura**) e gera o blueprint JSON |
| `apply_blueprint.py` | aplica o blueprint numa conta destino (**dry-run por padrão**) |
| `blueprint.clinica-v1.json` | blueprint da vertical clínica, exportado de `attivacorpoementeitz` |
| `blueprint.<unidade>.lock.json` | mapa `chave -> id` da conta destino: **use isto** no n8n, na Sofia e no dashboard, nunca id chumbado |

## Uso

```bash
# 1) exportar a matriz (não escreve nada na origem)
python3 export_account.py --subdomain attivacorpoementeitz \
    --token-file /caminho/token_itz.txt \
    --skip-pipeline "NÂO USAR" \
    --out blueprint.clinica-v1.json

# 2) ver o plano na conta destino (nada é escrito)
python3 apply_blueprint.py --blueprint blueprint.clinica-v1.json \
    --subdomain <destino> --token-file /caminho/token_destino.txt \
    --lock blueprint.<destino>.lock.json

# 3) aplicar
python3 apply_blueprint.py ... --apply --confirm-subdomain <destino>

# 4) verificar: reexportar o destino e comparar com o blueprint
python3 export_account.py --subdomain <destino> --token-file ... --out /tmp/destino_now.json
```

Rodar o passo 3 duas vezes não duplica nada — o que já existe é reconhecido e
mantido. É assim que se acrescenta um grupo novo depois (ex.: ASAAS) sem
recriar o resto.

## Importar a base do CRM antigo

`import_leads_csv.py` sobe leads de um CSV usando o de-para de
`mapping.<unidade>.json` (etapa → pipeline/status/motivo de perda/campos, e
origem → enum). O mapping é editável; o script não tem nada hardcoded.

```bash
python3 import_leads_csv.py --csv "contatos.csv" --mapping mapping.boa-vista.json \
    --lock blueprint.boa-vista.lock.json --subdomain <destino> \
    --token-file token.txt --state import.<destino>.jsonl        # dry-run
... --piloto                                                     # 1 lead por etapa de origem
... --apply --confirm-subdomain <destino>                        # tudo
```

* **Idempotente por telefone**: cada lead criado vai para o `--state` (JSONL);
  reexecutar pula o que já subiu e continua de onde parou.
* **`created_at` histórico**: sem ele a base inteira entra com a data de hoje e
  estoura o KPI do dia no dashboard. O importador tenta a data embutida no nome
  (convenção do CRM antigo, "Fulano 19/1/25") e cai para a data da última mensagem.
* **Perdido já nasce com `loss_reason_id`** no mesmo POST — a Kommo recusa o
  motivo em request separado.
* Fuso vem de `_embedded.datetime_settings.timezone_offset` da conta (fica em
  `_embedded`, não na raiz); sem ele o script aborta em vez de gravar data torta.
* **Não existe desfazer**: `DELETE /leads` = 405. Por isso dry-run → piloto → tudo.

## Identidade dos campos

A Kommo dá ids diferentes em cada conta, então o blueprint não carrega id. A
chave de cada campo é:

* o `code` do campo, quando existe (`ASAAS_*`, `IA_*`…);
* senão `<grupo>::<nome-slug>#<n>` — determinístico, com `#n` desempatando os
  separadores visuais que repetem nome (`**!`, `##!`).

O `lock.json` é o que traduz essa chave para o id real da conta destino,
inclusive `enum_id` de cada opção de select. **Toda automação (n8n, Sofia,
webhook) deve resolver o id pelo lock**, não copiar número. Foi exatamente o
acoplamento a ids fixos que quebrou a entrada de leads da ITZ quando os campos
legados foram apagados.

## Segurança

* `apply_blueprint.py` só escreve com `--apply` **e** `--confirm-subdomain`
  igual ao `--subdomain`;
* a lista `PROTECTED` bloqueia as contas de produção já existentes —
  `attivacorpoementeitz` está lá porque é a **matriz**, nunca destino;
* `export_account.py` instancia o cliente com `read_only=True`: qualquer
  POST/PATCH/DELETE levanta exceção antes de sair da máquina.

## Gotchas da API da Kommo (todos verificados em conta real)

1. **Paginação sempre `order[id]=asc`.** Ordenar por `updated_at` pula registro
   quando a conta está sendo mutada.
2. **`is_unsorted_on` é obrigatório** no `POST /leads/pipelines` (400 FieldMissing).
3. **`currency` é obrigatório em campo `monetary`** — no POST *e* em qualquer
   PATCH do campo, mesmo que o PATCH só mexa em `required_statuses`.
4. **`sort` de motivo de perda tem de ficar entre 1 e 100000** (a origem exporta 0).
5. **142/143 não se renomeiam por API.** O PATCH responde 200 e ignora. Os nomes
   viajam em `system_statuses` e o script avisa quais renomear na UI.
6. **`required_statuses` não gruda em 142 nem em 143.** A API responde 200 com
   `required_statuses: []`. O script lista esses campos no fim, para marcar em
   Configurações do funil > etapa > campos obrigatórios.
7. **`required_statuses` referencia `(pipeline_id, status_id)`.** 142/143 repetem
   o mesmo id em todos os funis — indexar só por `status_id` faz o obrigatório do
   funil A ser lido como se fosse do funil B (bug real, pego pelo passo 4).
8. **PATCH em lote morre inteiro por um item ruim** — daí o `patch_bisect`.
9. Tag: `color` só no endpoint `/leads/tags`, nunca no vínculo lead↔tag; hex de
   6 chars maiúsculo, da paleta fixa. `DELETE` de tag e de lead = 405.

## O que o blueprint NÃO carrega (de propósito)

* **tags** — na matriz são lixo de disparo em massa (250+ nomes de importação);
* **campos de contato** — sobras da Cloudia;
* **campos do grupo `statistic`/`Main`** — a Kommo cria os predefinidos sozinha;
* **dados de lead** — isto provisiona estrutura, não migra base.
