# Deploy na VPS (Docker Swarm + Traefik + CI/CD)

CI/CD por Git: `push` na `main` → GitHub Actions builda a imagem → publica no GHCR
→ conecta na VPS por SSH → `docker stack deploy` faz **rolling update sem downtime**
(sobe o container novo, espera o `/health`, só então mata o antigo; se o novo não
ficar saudável, o Swarm reverte sozinho).

A VPS **não** usa `docker compose up`. Ela roda **Docker Swarm** com Traefik já
emitindo TLS (Let's Encrypt). O `docker-compose.yml` é aplicado com
`docker stack deploy` — os dois pontos que dão o zero-downtime (`order: start-first`
+ `healthcheck`) só existem no modo Swarm.

## Arquitetura

```
push main ──> GitHub Actions ──> ghcr.io/<owner>/doutordigital-api:<sha>
                                        │
                                   SSH  ▼
                        VPS (Swarm) ── docker stack deploy ddapi
                                        │  rede overlay: portainer-next
                          ┌─────────────┼──────────────┐
                       Traefik      Postgres         (n8n, origens, …)
                     TLS/roteamento   já existe          já rodando
```

- Back-end (esta stack): `ddapi_api` → `https://${API_DOMAIN}`
- Front-end (repo Front-End): `ddweb_web` → `https://${WEB_DOMAIN}`
- Postgres e Traefik já rodam na VPS; a stack só se conecta na overlay `portainer-next`.

## Estratégia de virada (sem derrubar o que está no ar)

A produção atual (`186.233.225.157` e Vercel) continua intacta. Sobe-se primeiro em
**subdomínios novos**:

- `api-vps.doutordigitalconsultoria.com`
- `dash-kommo.doutordigitalconsultoria.com`

Valida com dados reais e só então troca o DNS de `api.*`/`dashboard.*`. Zero risco
para a produção enquanto valida.

---

## Setup (uma vez)

### 1. DNS

Crie registros **A** apontando para a VPS **antes do primeiro deploy** (senão o
Traefik queima o rate limit do Let's Encrypt tentando emitir cert de domínio que
não resolve):

```
api-vps.doutordigitalconsultoria.com.   A   89.116.214.130
dash-kommo.doutordigitalconsultoria.com.  A   89.116.214.130
```

### 2. Token de leitura do GHCR (PAT)

O Swarm precisa de credencial de vida longa para dar `pull` num restart futuro (o
`GITHUB_TOKEN` do job expira). Crie um **PAT clássico** com escopo `read:packages`
em https://github.com/settings/tokens — será o secret `GHCR_PULL_TOKEN`.

### 3. Diretório e `.env` na VPS

```bash
ssh -i ~/.ssh/doutordigital_vps root@89.116.214.130

mkdir -p /opt/doutordigital/api /opt/doutordigital/web
```

Na sua máquina, copie os modelos e edite os valores **na VPS** (nunca commite):

```bash
# back-end
scp -i ~/.ssh/doutordigital_vps \
  .env.deploy.example root@89.116.214.130:/opt/doutordigital/api/.env
ssh -i ~/.ssh/doutordigital_vps root@89.116.214.130 'chmod 600 /opt/doutordigital/api/.env'
```

Preencha `/opt/doutordigital/api/.env`:
- `POSTGRES_PASSWORD` → a senha do Postgres da VPS
  (`42847069be737d247499076257416cc9`, confirmada no `docker service inspect`).
- `JWT_SECRET` → **gere uma nova** com `openssl rand -base64 32`. A que está no
  `appsettings.json` está versionada e deve ser trocada.
- `LOGS_AUTH_PASSWORD`, `RESEND_API_KEY` conforme necessário.

Para o front, o `.env` só precisa de `GHCR_OWNER`, `IMAGE_TAG` e `WEB_DOMAIN`.

### 4. Secrets e Variables no GitHub (nos DOIS repos)

**Secrets** (Settings → Secrets and variables → Actions → Secrets):

| Secret            | Valor                                                        |
|-------------------|-------------------------------------------------------------|
| `VPS_HOST`        | `89.116.214.130`                                            |
| `VPS_USER`        | `root`                                                      |
| `VPS_SSH_KEY`     | conteúdo de `~/.ssh/doutordigital_vps` (a chave privada)    |
| `GHCR_PULL_TOKEN` | o PAT `read:packages` do passo 2                           |

**Variables** (aba Variables):

| Repo       | Variable             | Valor                                       |
|------------|----------------------|---------------------------------------------|
| Back-End   | `API_DOMAIN`         | `api-vps.doutordigitalconsultoria.com`      |
| Front-End  | `WEB_DOMAIN`         | `dash-kommo.doutordigitalconsultoria.com`     |
| Front-End  | `VITE_API_BASE_URL`  | `https://api-vps.doutordigitalconsultoria.com` |

> A chave privada já está autorizada na VPS (foi usada para toda a inspeção). Para
> confirmar: `ssh-copy-id -i ~/.ssh/doutordigital_vps.pub root@89.116.214.130`.

### 5. Primeiro deploy

Feito o setup, qualquer `push` na `main` dispara o pipeline. Para forçar sem commitar,
use **Actions → deploy-api → Run workflow** (`workflow_dispatch`).

---

## Migração dos dados (na virada, não agora)

O banco `leadanalytics` da VPS está **vazio**. Os dados vivem na produção atual. Na
hora de virar o DNS:

```bash
# dump da produção atual → restore no Postgres da VPS
pg_dump "<CONNSTRING_PROD_ATUAL>" -Fc -f prod.dump
scp -i ~/.ssh/doutordigital_vps prod.dump root@89.116.214.130:/root/
ssh -i ~/.ssh/doutordigital_vps root@89.116.214.130 \
  'docker exec -i $(docker ps -q -f name=postgres_postgres) \
     pg_restore -U postgres -d leadanalytics --clean --if-exists < /root/prod.dump'
```

A API roda `Migrate()` no boot, então o schema se ajusta sozinho depois do restore.

## Operação

```bash
# estado do rollout
docker service ps ddapi_api

# logs ao vivo
docker service logs -f ddapi_api

# rollback manual para a versão anterior
docker service rollback ddapi_api

# deploy manual (sem CI), a partir de /opt/doutordigital/api
set -a && . ./.env && set +a
docker stack deploy -c docker-compose.yml --with-registry-auth --prune ddapi
```

## Rollback automático

Se o container novo não passar no `/health` dentro de `monitor` (90s), o Swarm
**reverte sozinho** para a imagem anterior (`failure_action: rollback`) e o job do CI
falha vermelho. A produção nunca fica servindo uma versão quebrada.
