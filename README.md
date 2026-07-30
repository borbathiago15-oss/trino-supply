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
├── 03-business          # Negócio: foundation (FD-001) e procurement (PR-001)
├── 05-design-system     # Marca e logo oficial (DS-001)
├── 06-database          # Modelos físicos por módulo
├── 17-adr               # Architecture Decision Records
└── 90-archive           # Documentos substituídos (somente histórico)
```

## Regras

1. Primeiro documentação, depois implementação.
2. Em conflito, a documentação `Approved` no registry vence.
3. Nenhuma funcionalidade sem os artefatos obrigatórios (ver TPES-002).
4. Mudanças de arquitetura exigem ADR.
