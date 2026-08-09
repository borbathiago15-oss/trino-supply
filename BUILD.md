# Trino Supply — Guia de Build (esqueleto)

> Implementa o **GO-001 — Build Kickoff** (item 0, "esteira"). A documentação em `docs/` é a Single Source of Truth; este código a realiza (ARC-004, ADR-009).

## Pré-requisitos
- **.NET SDK 9.0+**
- **Node 22+** (frontend)
- **Docker** (infra local)

## Estrutura (por Bounded Context — ARC-002/ARC-004)
```
src/
  BuildingBlocks/            kernel técnico (Result, CompanyId, Outbox…)
  Foundation/{Domain,Application,Infrastructure}
host/
  Api/                       Modular Monolith (.NET 9) — composição, endpoints
  Worker/                    consumidores de eventos (Outbox → RabbitMQ)
web/                         frontend Next.js/React/TS
deploy/docker-compose.yml    postgres, redis, rabbitmq, minio(=R2 dev)
.github/workflows/ci.yml     pipeline (OPS-001)
```

## Subir a infra local
```bash
docker compose -f deploy/docker-compose.yml up -d
```

## Build & run (backend)
```bash
dotnet restore TrinoSupply.slnx
dotnet build   TrinoSupply.slnx -c Release
dotnet run --project host/Api        # health: http://localhost:5xxx/health e /api/v1/health
dotnet run --project host/Worker     # OutboxRelayWorker (heartbeat)
```
> Se a sua versão do `dotnet` não abrir `.slnx`, use os projetos diretamente
> (`dotnet build host/Api`) ou gere uma solution clássica com `dotnet new sln` + `dotnet sln add`.

## Frontend
```bash
cd web && npm install && npm run dev
```

## Migração de banco (dev) — EF Core é o SSOT do schema
```bash
docker compose -f deploy/docker-compose.yml up -d postgres

# Aplica as migrations (rode com role privilegiada; NÃO com a role da aplicação).
dotnet tool install --global dotnet-ef --version 9.0.0   # uma vez
export ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=trino;Username=trino;Password=trino"
dotnet ef database update \
  --project src/Foundation/Infrastructure --startup-project src/Foundation/Infrastructure

# Nova migration após mudar o modelo:
# dotnet ef migrations add <Nome> --project src/Foundation/Infrastructure \
#   --startup-project src/Foundation/Infrastructure --output-dir Persistence/Migrations
```
> A migration `InitialFoundation` cria `company`/`outbox`/`role`/`app_user`, os índices e — via
> `migrationBuilder.Sql` — a função `current_company()` e as **policies RLS** (SEC-004), que o EF
> não gera do modelo. Os arquivos em `deploy/db/` (`001`/`002`) são **referência** (caminho sem EF)
> e `rls.sql` documenta o hardening da role de aplicação (que roda fora das migrations).

## Estado atual (esqueleto)
- ✅ Estrutura da solution por contexto; `BuildingBlocks` (Result, Entity/AggregateRoot,
  eventos de domínio, Outbox, CompanyId/ITenantContext, IClock).
- ✅ `Foundation`: agregado `Company` (tenant) + `FoundationDbContext` (EF Core) que grava
  eventos de domínio no **Outbox** na mesma transação (ARC-005 §3).
- ✅ **Migrations EF Core (SSOT do schema):** `InitialFoundation` cria `company`/`outbox`/`role`/
  `app_user` + índices + função `current_company()` + policies **RLS** (via `migrationBuilder.Sql`).
  Aplicada e validada com `dotnet ef database update` (SEC-004). SQL em `deploy/db/` é referência.
- ✅ **Enforcement do RLS (SEC-004):** `TenantConnectionInterceptor` define `app.current_company`
  por conexão, **fail-closed** (sem tenant → nega tudo). A `outbox`/`company` não usam RLS (infra/
  catálogo — isolamento por role).
- ✅ `host/Api`: health check + **AuthN JWT fail-closed** (validação estrita; recusa iniciar em
  produção sem AuthN) + `HttpTenantContext` (tenant do token) + endpoint protegido `/api/v1/whoami`.
- ✅ `host/Worker` com `OutboxRelayWorker` (placeholder do publisher).
- ✅ **IAM (FD-001-01) — Fase 1 (validada em Postgres real):** agregados `User` e `Role`,
  catálogo de permissões, autorização **deny-by-default** (`IPermissionChecker`), provisionamento de
  empresa com admin, e **gestão de papéis por API** — criar papel, conceder/revogar permissão e
  atribuir/remover papel de usuário (`/api/v1/roles`, `/api/v1/users/{id}/roles`). Concessão e
  revogação **mudam o acesso dinamicamente** (comprovado). **Métricas de uso** (`IUsageMetrics`)
  por tenant para o case de sucesso. RLS em `role`/`app_user` (`deploy/db/002_iam.sql`); isolamento
  entre tenants provado **sem filtro na aplicação** (só RLS).
- ✅ `docker-compose` (Postgres/Redis/RabbitMQ/MinIO) e pipeline CI.
- ⏳ **Próximo (GO-001 · sprint 1):** migrations EF Core; policies RLS aplicadas às tabelas de
  negócio reais; publisher Outbox→RabbitMQ real com role dedicada; IAM (usuários/papéis) e
  Auditoria; testes de integração de isolamento (Testcontainers — QA-001 / SEC-004 §8).

> Nota: este esqueleto foi escrito seguindo as convenções do .NET 9, porém **não foi compilado
> no ambiente de geração** (sem SDK .NET). Rode `dotnet build` localmente para validar.
