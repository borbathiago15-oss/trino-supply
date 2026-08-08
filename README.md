# trino-supply

Enterprise Supply Management System

Plataforma SaaS de Gestão de Suprimentos — DDD, Clean Architecture, SOLID, organização por domínios.

## Stack

- **Frontend:** Next.js, React, TypeScript, Tailwind CSS
- **Backend:** .NET 9, ASP.NET Core Web API
- **Banco:** PostgreSQL · **Cache:** Redis · **Storage:** MinIO · **Mensageria:** RabbitMQ
- **Infra:** Docker, GitHub Actions

## Documentação (Single Source of Truth)

Toda implementação segue rigorosamente `docs/`. O índice oficial é
[`docs/00-governance/document-registry.md`](docs/00-governance/document-registry.md).

```text
docs/
├── 00-governance        # Registro mestre, Prompt Master (TPES-002)
├── 01-vision            # Visão do produto (placeholder)
├── 02-product           # Engenharia de produto (placeholder)
├── 03-business          # Negócio: foundation (FD-001), procurement (PR-001), materials (MMS-*)
├── 04-architecture      # Arquitetura do sistema (ARC-001..006)
├── 05-design-system     # Marca e logo oficial (DS-001)
├── 06-database          # Modelos físicos por módulo
├── 07-api               # Contratos de API por módulo
├── 08-security          # Arquitetura de segurança (SEC-001..003)
├── 09-testing           # Estratégia de testes (placeholder)
├── 10-devops            # DevOps & CI/CD (placeholder)
├── 11-release           # Gestão de releases (placeholder)
├── 12-user-guides       # Guias do usuário (placeholder)
├── 17-adr               # Architecture Decision Records
└── 90-archive           # Documentos substituídos (somente histórico)
```

## Regras

1. Primeiro documentação, depois implementação.
2. Em conflito, a documentação `Approved` no registry vence.
3. Nenhuma funcionalidade sem os artefatos obrigatórios (ver TPES-002).
4. Mudanças de arquitetura exigem ADR.

## Código

A implementação segue o **GO-001 — Build Kickoff** (`docs/00-governance/GO-001-build-kickoff.md`). O esqueleto inicial está em `src/` (Bounded Contexts), `host/` (Api/Worker), `web/` (Next.js) e `deploy/` (docker-compose). Como buildar: ver [`BUILD.md`](BUILD.md).
