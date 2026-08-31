# Trino Platform — monorepo (Fases F0, F1 e F2)

Base multi-tenant da plataforma Trino: schemas `core` e `auditoria` no Postgres,
Prisma com multi-schema, isolamento de tenant imposto por extensão do client
(F0); API NestJS com autenticação real, escopo por token e auditoria global
(F1); catálogo de materiais, fornecedores e elegibilidade nos schemas
`catalogo` e `fornecimento` (F2). Convive com o app .NET do Trino Supply
(`../src`) sem tocar nele.

## Estrutura

```
platform/
  packages/db/          @trino/db — Prisma + extensão de tenant
    prisma/schema.prisma        core, auditoria, catalogo e fornecimento (tradução fiel do DDL)
    prisma/migrations/          init_core_and_audit, init_catalogo_e_fornecimento
    src/tenant-extension.js     forTenant(prisma, tenantId)
    src/limpeza-testes.js       limparBancoDeTestes (ordem das FKs, só testes)
    test/tenant-isolation.test.js
  apps/api/             @trino/api — API NestJS (F1 + F2)
    src/auth/                   login argon2 + JWT, JwtAuthGuard, TenantGuard
    src/audit/                  AuditInterceptor global + @Auditar
    src/usuarios/               CRUD de usuários do tenant (rotas auditadas)
    src/catalogo/               família → tipo → SKU base → variante, ROP e saldo
    src/fornecedores/           fornecedores, documentos, CA e elegibilidade
    scripts/seed.js             bootstrap de tenant + admin
    test/e2e.test.js            e2e da F1 contra Postgres real
    test/e2e-f2.test.js         e2e da F2 contra Postgres real
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
npm run test:e2e              # sobe o Nest de verdade contra o Postgres (F1 + F2)
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
- **CHECKs, índices parciais e o particionamento do `audit_log`** não são
  expressáveis no schema.prisma: vivem nas migrations SQL (editadas via
  `--create-only`). São eles: o particionamento e os 6 CHECKs da F0; os 8 CHECKs
  da F2 mais `ux_variante_ean` (EAN único por tenant só quando preenchido) e
  `ix_documento_validade` (parcial em `obrigatorio = TRUE`). Ao gerar uma
  migration nova, **revise o SQL antes de aplicar**: o Prisma não enxerga essas
  cláusulas e pode propor DROPs. Crie as partições anuais do `audit_log` antes
  da virada do ano (a partição DEFAULT segura o que escapar).
- **A ordem de exclusão dos testes mora em `limparBancoDeTestes`** (`@trino/db`):
  toda fase que acrescentar tabelas atualiza essa lista, e as suítes das fases
  anteriores continuam rodando.

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

## Catálogo e fornecimento (F2)

Hierarquia do catálogo: **família → tipo de produto → SKU base → variante**.
A variante é a unidade que tem saldo, parâmetro de ROP, CA e preço de
fornecedor. `sku_base.exige_ca = true` marca o item de EPI.

- `GET|POST /catalogo/familias`, `/catalogo/tipos`, `/catalogo/skus`,
  `/catalogo/variantes` (+ `PATCH /:id`); os `GET` aceitam filtro pelo pai.
- `PUT /catalogo/parametros-estoque` define o ROP do par (variante, centro de
  custo) — um só por par, redefinir atualiza.
- `GET|POST /catalogo/saldos` e `PATCH /catalogo/saldos/:id` **com bloqueio
  otimista**: o corpo informa a `version` lida; se o saldo mudou nesse meio
  tempo a resposta é `409 EST-ERR-409` e ninguém sobrescreve leitura velha.
- Validação por **class-validator** em todos os payloads, com `whitelist` e
  `forbidNonWhitelisted` globais: campo desconhecido é `400`, não algo
  silenciosamente ignorado. Query params de id também são validados como UUID.

Fornecedores: `GET|POST /fornecedores`, `GET /fornecedores/:id` (traz
documentos, CAs e SKUs), `PATCH /fornecedores/:id`, `PATCH
/fornecedores/:id/status`, `POST /fornecedores/:id/documentos`,
`POST /fornecedores/:id/certificados`, `POST /fornecedores/:id/skus`.
Sair de HOMOLOGADO exige motivo. O arquivo do documento vive fora do banco: o
que guardamos é a `arquivoUri`.

### Elegibilidade — a regra que precede a compra

`GET /fornecedores/:id/elegibilidade?varianteId=…` responde com **todos** os
motivos de bloqueio de uma vez, para o comprador resolver tudo numa ida:

| código | bloqueio |
| --- | --- |
| `FOR-ELG-001` | fornecedor não está `HOMOLOGADO` |
| `FOR-ELG-002` | documento **obrigatório** vencido |
| `FOR-ELG-003` | certidão exigida nunca cadastrada |
| `FOR-ELG-004` | item exige CA (EPI) e não há CA válido do fornecedor para a variante |

Detalhes que valem contrato:

- **Vale o documento mais recente de cada tipo**: reemitir a certidão substitui
  a anterior, sem apagar histórico.
- Documento com `obrigatorio = false` vencido **não** bloqueia.
- Certidões exigidas por padrão: `CND_FEDERAL`, `FGTS`, `TRABALHISTA`,
  ajustável com `FORNECEDOR_CERTIDOES_EXIGIDAS`. Lista vazia desliga a
  exigência de presença — nunca a de validade.
- `POST /fornecedores/:id/skus` (vínculo em processo de compra) chama
  `exigirElegivel` e devolve `409 FOR-ELG-409` com os motivos. Qualquer módulo
  de compras futuro deve usar o mesmo `ElegibilidadeService`, exportado pelo
  `FornecedoresModule`.
