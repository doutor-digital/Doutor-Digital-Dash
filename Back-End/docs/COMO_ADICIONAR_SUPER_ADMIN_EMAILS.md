# Como adicionar `SuperAdminEmails`

Você pode configurar e-mails de super admin de 2 formas no backend.

## 1) Via `appsettings.json`

No arquivo `LeadAnalytics.Api/appsettings.json`:

```json
"Auth": {
  "SuperAdminEmails": [
    "admin@cloudia.com.br",
    "diretoria@empresa.com"
  ]
}
```

## 2) Via variável de ambiente (produção)

### Opção A (array nativo do .NET)

```bash
Auth__SuperAdminEmails__0=admin@cloudia.com.br
Auth__SuperAdminEmails__1=diretoria@empresa.com
```

### Opção B (CSV)

```bash
Auth__SuperAdminEmailsCsv=admin@cloudia.com.br,diretoria@empresa.com
```

> A aplicação aceita as duas opções e faz merge dos e-mails.

## Como funciona no login

- Se o e-mail do usuário estiver na lista de super admins:
  - role = `super-admin`
  - `availableUnits` retorna **todas as unidades**
- Caso contrário:
  - role = `user`
  - `availableUnits` retorna apenas Araguaína (`clinicId=8020`, `id=1`)

## Endpoint

`POST /api/auth/login`

Exemplo de body:

```json
{
  "name": "Admin Cloudia",
  "email": "admin@cloudia.com.br",
  "password": "sua_senha"
}
```

Resposta inclui `accessToken`, `role`, `selectedUnit` e `availableUnits`.
