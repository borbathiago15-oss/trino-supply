# Trino Platform — monorepo (Fases F0 e F1)

Base multi-tenant da plataforma Trino: schemas `core` e `auditoria` no Postgres,
Prisma com multi-schema, isolamento de tenant imposto por extensão do client
(F0) e a API NestJS com autenticação real, escopo por token e auditoria
global (F1). Convive com o app .NET do Trino Supply (`../src`) sem tocar nele.

## Estrutura

```
platform/
  packages/db/          @trino/db — Prisma + extensão de tenant
    prisma/schema.prisma        modelos core + auditoria (tradução fiel do DDL validado)
    prisma/migrations/          migrations versionadas (init_core_and_audit)
    src/tenant-extension.js     forTenant(prisma, tenantId)
    test/tenant-isolation.test.js
  apps/api/             @trino/api — API NestJS (F1)
    src/auth/                   login argon2 + JWT, JwtAuthGuard, TenantGuard
    src/audit/                  AuditInterceptor global + @Auditar
    src/usuarios/               CRUD de usuários do tenant (rotas auditadas)
    scripts/seed.js             bootstrap de tenant + admin
    test/e2e.test.js            e2e contra Postgres real
```

## Rodando

```bash
cd platform && npm install

# banco
cp packages/db/.env.example packages/db/.env   # aponte para o seu Postgres
cd packages/db
npx prisma migrate dev        # aplica migrations + gera o client (dev)
npx prisma migrate deploy     # produção: só aplica, nunca gera diff
node test/tenant-isolation.test.js

# API
cd ../../apps/api
cp .env.example .env          # DATABASE_URL + JWT_SECRET (obrigatório)
SEED_ADMIN_SENHA='...' npm run seed
npm run build && npm start    # http://127.0.0.1:3001/api/v1
npm run test:e2e              # sobe o Nest de verdade contra o Postgres
```

Login: `POST /api/v1/auth/login` com `{ cnpj, email, senha }` devolve
`tokenAcesso` (Bearer). As demais rotas exigem `Authorization: Bearer <token>`.

## Regras da fundação

- **Todo acesso de aplicação usa `forTenant(prisma, tenantId)`** — o filtro de
  tenant é injetado em todas as operações; registro de outro tenant se comporta
  como inexistente (`TENANT-ERR-404`). O client base fica para bootstrap,
  jobs administrativos e o catálogo global de permissões.
- **`$queryRaw`/`$executeRaw` não passam pela extensão**: SQL cru precisa
  filtrar `tenant_id` explicitamente e passa por revisão.
- **CHECKs e o particionamento do `audit_log`** não são expressáveis no
  schema.prisma: vivem na migration SQL (editada via `--create-only`).
  Ao criar novas migrations, nunca remova essas cláusulas. Crie as partições
  anuais do `audit_log` antes da virada do ano (a partição DEFAULT segura o
  que escapar).

## Regras da autenticação (F1)

- **Não existe usuário assumido.** O login valida CNPJ do tenant + `(tenant_id,
  email)` em `core.usuario` + `argon2.verify` contra `senha_hash`. Usuário
  inexistente, inativo ou tenant inativo caem no mesmo `401 AUTH-ERR-001`, e o
  caminho de falha ainda paga um verify de sacrifício para não denunciar contas
  pelo tempo de resposta.
- **`JWT_SECRET` é obrigatório**: sem ele a API não sobe (falha no bootstrap).
- **O `tenantId` vem exclusivamente do payload do token.** O `TenantGuard` só lê
  `request.user` (montado pela `JwtStrategy` a partir do token assinado) e
  publica `request.tenantId` e `request.db = forTenant(...)`. Headers como
  `x-tenant-id`, body e query string nunca são consultados — os controllers não
  recebem tenant por parâmetro e por isso não têm como aceitar um forjado.
- **Serviços recebem `request.db`, nunca o client base.** Um service de
  aplicação não vê `tenantId` e não consegue vazar dados de outro grupo.
- **Senhas e segredos nunca saem**: `senha_hash` e `mfa_secret` ficam fora das
  respostas da API e fora dos snapshots de auditoria.

## Auditoria (F1)

- `AuditInterceptor` é global (`APP_INTERCEPTOR`) e cobre qualquer módulo,
  presente ou futuro. Ele age em handlers **mutantes** (POST/PUT/PATCH/DELETE)
  marcados com `@Auditar('entidade')` — a marcação é o contrato explícito do que
  é mutação crítica.
- Cada registro em `auditoria.audit_log` leva ator (do token), entidade,
  `entity_id`, ação, `before_json`/`after_json` (JSONB), IP, user-agent e
  `correlation_id` (o do header `x-correlation-id`, se vier em formato UUID, ou
  um novo).
- O snapshot **antes** é lido pelo próprio client escopado antes do handler; o
  **depois** é a resposta do handler (nulo em DELETE).
- A escrita do log é aguardada dentro da requisição: se a auditoria falhar, a
  requisição falha. Auditoria de mutação crítica não é melhor-esforço.
