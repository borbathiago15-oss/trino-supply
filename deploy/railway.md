# Deploy de teste no Railway (e em qualquer PaaS)

> **O mesmo contrato vale em qualquer plataforma.** A API não tem nada específico do Railway:
> ela lê `DATABASE_URL`, `PORT` e `MIGRATE_ON_STARTUP` — convenções usadas também por Render,
> Fly.io, Google Cloud Run, Heroku e afins. Se o Railway pedir um plano, veja
> **[Alternativas](#alternativas-se-o-railway-pedir-um-plano)** no fim deste guia: a tabela de
> variáveis abaixo é a mesma em todas.

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
2. Em **Settings → Build**: deixe **Root Directory = `/`**. O arquivo `railway.json` na raiz do
   repo já diz ao Railway para usar o `deploy/Dockerfile.api` e o healthcheck `/health/ready` —
   normalmente não é preciso configurar nada aqui. (Se o painel não pegar: Builder = `Dockerfile`,
   **Dockerfile Path** = `deploy/Dockerfile.api`.)
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
2. **Settings → Build**: **Root Directory = `web`**. O `web/railway.json` já aponta o
   `web/Dockerfile`. (Fallback manual: Builder = `Dockerfile`, Dockerfile Path = `Dockerfile`.)
3. **Variables**:

   | Variável | Valor |
   | -------- | ----- |
   | `BACKEND_URL` | a URL pública da API (passo 2.4), ex. `https://api-xxxx.up.railway.app` |

   > Opcional (mais rápido e sem custo de saída): use a **rede privada** —
   > `BACKEND_URL = http://<nome-do-servico-api>.railway.internal:<PORT-da-API>` (a porta é a que
   > o Railway injeta no serviço da API — veja a variável `PORT` dele). A API escuta em `[::]`
   > justamente para isso (a rede interna do Railway é IPv6). Se der qualquer problema, volte
   > para a URL pública, que sempre funciona.

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

### 5) (Opcional) Dados de demonstração em um comando
Em vez do `curl` acima, o script abaixo cria a empresa **e já popula tudo**: 2 CNPJs, 4 centros de
custo, 7 itens de EPI/fardamento com saldo, 3 colaboradores, usuários dos três perfis e pedidos em
situações diferentes — com um roteiro de teste impresso no final.

```bash
API_URL=https://sua-api.up.railway.app PROVISIONING_KEY=sua-chave ./deploy/seed-demo.sh
```

Rode **uma vez**, numa instância nova (não é idempotente). Só precisa de `bash` + `curl`.

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

## Alternativas (se o Railway pedir um plano)

O Railway encerrou o trial gratuito para novas contas — sem plano, ele bloqueia a criação do
projeto. Nada disso depende do nosso código: as três rotas abaixo usam **as mesmas variáveis**.

### A) Na sua máquina / servidor interno — R$ 0, disponível hoje
É o **piloto completo** (`deploy/docker-compose.pilot.yml`), com Worker, backup diário e vigia
de alertas — coisas que os PaaS gratuitos não dão:

```bash
docker compose -f deploy/docker-compose.pilot.yml up --build
```

Abra `http://localhost:3000`. Para outras pessoas testarem de fora sem abrir porta nenhuma no
roteador, use o **Cloudflare Tunnel** (grátis) — passo a passo em `deploy/README.md`
(§ Borda HTTPS). Requisito: uma máquina ligada durante o teste.

### B) Railway pago — menor esforço, tudo já configurado
Plano Hobby (na casa de **US$ 5/mês**, confira o valor atual no site). É seguir este guia do
começo: o projeto já está pronto, leva ~10 minutos.

### C) Outro PaaS com camada gratuita
Mesmas variáveis do passo 2.3, mudando só onde se aponta o Dockerfile:

| Plataforma | API (.NET) | Banco | Observação |
| ---------- | ---------- | ----- | ---------- |
| **Render** | Web Service → Docker, `deploy/Dockerfile.api` | Postgres do próprio Render | Serviço grátis **hiberna** após inatividade (primeiro acesso demora); o Postgres grátis tem validade — confirme o prazo atual |
| **Google Cloud Run** | Deploy do container, `PORT` é injetado | **Neon** ou **Supabase** (grátis) via `DATABASE_URL` | Camada gratuita generosa; exige conta com faturamento ativo |
| **Fly.io** | `fly launch` usando o Dockerfile | Neon/Supabase ou Fly Postgres | Pago por uso, valores baixos |

Em qualquer uma: **Root Directory `/`** e Dockerfile `deploy/Dockerfile.api` para a API;
**Root Directory `web`** para o frontend, com `BACKEND_URL` apontando para a URL pública da API.

> Em todas as opções, **defina `APP_DB_PASSWORD`**: é ele que faz a API conectar como
> `trino_app` (sem superuser) e manter o isolamento multi-tenant real. Sem ele, em Production
> a API se recusa a subir quando o banco só oferece um usuário privilegiado (guard SEC-004).
