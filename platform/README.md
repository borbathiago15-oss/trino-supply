# Trino Platform — monorepo (Fase F0)

Base multi-tenant da plataforma Trino: schemas `core` e `auditoria` no Postgres,
Prisma com multi-schema e isolamento de tenant imposto por extensão do client.
Convive com o app .NET do Trino Supply (`../src`) sem tocar nele.

## Estrutura

```
platform/
  packages/db/          @trino/db — Prisma + extensão de tenant
    prisma/schema.prisma        modelos core + auditoria (tradução fiel do DDL validado)
    prisma/migrations/          migrations versionadas (init_core_and_audit)
    src/tenant-extension.js     forTenant(prisma, tenantId)
    test/tenant-isolation.test.js
```

## Rodando

```bash
cd platform && npm install
cp packages/db/.env.example packages/db/.env   # aponte para o seu Postgres
cd packages/db
npx prisma migrate dev        # aplica migrations + gera o client (dev)
npx prisma migrate deploy     # produção: só aplica, nunca gera diff
node test/tenant-isolation.test.js
```

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
