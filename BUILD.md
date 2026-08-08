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

## Estado atual (esqueleto)
- ✅ Estrutura da solution por contexto; `BuildingBlocks` (Result, CompanyId/ITenantContext).
- ✅ `host/Api` com health check (`/health`, `/api/v1/health`) e composição pronta para DI.
- ✅ `host/Worker` com `OutboxRelayWorker` (placeholder do publisher — ARC-005).
- ✅ `docker-compose` (Postgres/Redis/RabbitMQ/MinIO) e pipeline CI.
- ⏳ **Próximo (GO-001 · sprint 1):** fatia vertical do Foundation — IAM (JWT), multi-tenant + RLS,
  Configuration/Master Data, Outbox→RabbitMQ real, Auditoria; com testes (QA-001).

> Nota: este esqueleto foi escrito seguindo as convenções do .NET 9, porém **não foi compilado
> no ambiente de geração** (sem SDK .NET). Rode `dotnet build` localmente para validar.
