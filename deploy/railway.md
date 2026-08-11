# Deploy de teste no Railway

Guia para subir o Trino Supply no [Railway](https://railway.app) e testar pelo navegador.
São **3 serviços**: Postgres (gerenciado), API (.NET) e Web (Next.js). O Worker/RabbitMQ é
opcional e não é necessário para o teste (sem ele, só a estatística de fornecedor não atualiza).

A API já entende as convenções do Railway (`host/Api/Startup/CloudEnvironment.cs`):
`DATABASE_URL`, `PORT` e `MIGRATE_ON_STARTUP=true` (aplica as migrations e cria as roles
`trino_app`/`trino_worker` no boot — substitui o serviço `bootstrap` do compose).

## Passo a passo

### 1) Projeto + banco
1. **New Project → Deploy PostgreSQL** (só o banco, por enquanto).

### 2) Serviço da API
1. **New → GitHub Repo** → selecione `borbathiago15-oss/trino-supply` (branch
   `claude/docs-system-architecture-4driu8`).
2. Em **Settings → Build**: Builder = `Dockerfile`, **Dockerfile Path** = `deploy/Dockerfile.api`
   (Root Directory = `/`, o Dockerfile usa a raiz como contexto).
3. Em **Variables**, adicione:

   | Variável | Valor |
   | -------- | ----- |
   | `DATABASE_URL` | Reference → Postgres → `DATABASE_URL` |
   | `MIGRATE_ON_STARTUP` | `true` |
   | `APP_DB_PASSWORD` | uma senha forte (a API roda como `trino_app` — RLS efetivo) |
   | `WORKER_DB_PASSWORD` | outra senha forte |
   | `JWT_SIGNING_SECRET` | segredo com **32+ caracteres** |
   | `PROVISIONING_KEY` | chave para criar a 1ª empresa (`X-Provisioning-Key`) |
   | `ASPNETCORE_ENVIRONMENT` | `Production` |

4. Em **Settings → Networking**: **Generate Domain** (anote, ex.:
   `https://api-xxxx.up.railway.app`). Healthcheck path (opcional): `/health/ready`.

### 3) Serviço do Web
1. **New → GitHub Repo** → mesmo repo/branch.
2. **Settings → Build**: Builder = `Dockerfile`, **Root Directory** = `web`
   (o `web/Dockerfile` usa o diretório `web` como contexto).
3. **Variables**:

   | Variável | Valor |
   | -------- | ----- |
   | `BACKEND_URL` | a URL pública da API (passo 2.4), ex. `https://api-xxxx.up.railway.app` |

4. **Generate Domain** → esta é a URL que você abre no navegador
   (ex.: `https://web-xxxx.up.railway.app`).
5. Volte nas **Variables da API** e adicione `WEB_BASE_URL` = a URL do web
   (usada nos links de e-mail de criação de senha; sem SMTP os links saem no log da API).

### 4) Criar a primeira empresa + admin
Com a API no ar (deploy verde), rode no seu terminal (ou use um cliente REST):

```bash
curl -X POST https://api-xxxx.up.railway.app/api/v1/companies \
  -H 'Content-Type: application/json' \
  -H 'X-Provisioning-Key: SUA_PROVISIONING_KEY' \
  -d '{"legalName":"Grupo Trino","taxId":"11.111.111/0001-11",
       "adminSubject":"admin","adminEmail":"admin@trino.com",
       "adminName":"Admin","adminPassword":"troque-esta-senha"}'
```

Guarde o `companyId` retornado. Abra a URL do web, faça login com o `companyId`,
`admin@trino.com` e a senha. Pronto para testar.

## Dicas
- **Logs**: painel do serviço → Deploy Logs. Convite de senha sem SMTP aparece lá
  (`SMTP não configurado — e-mail NÃO enviado ... /criar-senha?...`).
- **Redeploy**: push no branch → Railway rebuilda sozinho.
- **Migrations novas**: chegam junto com o deploy (o boot aplica o que faltar; é idempotente).
- **Segurança**: a API se recusa a subir em Production conectada como superuser (guard SEC-004);
  com `APP_DB_PASSWORD` definido ela conecta como `trino_app` e o guard passa — foi validado
  em simulação idêntica (Postgres 16 + DATABASE_URL + PORT injetado).
- **Custos**: os 3 serviços cabem no plano Hobby; o Postgres do Railway já faz backup próprio,
  mas o teste não substitui o piloto do compose (backup/alertas/worker completos).
