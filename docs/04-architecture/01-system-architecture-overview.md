**Documento:** ARC-001 — System Architecture Overview
**Versão:** 1.0.0
**Status:** Approved
**Criticidade:** 🔴 Core
**Escopo:** Plataforma Trino Supply (todos os módulos)

> Referências normativas: C4 Model (Context/Container/Component), Domain-Driven Design, Clean Architecture, ISO/IEC 25010.
> Documentos relacionados: TPES-002, ARC-002 (Bounded Contexts), ARC-003 (Deployment), ARC-004 (Application), SEC-001, ADR-009/010/011/012.

# ARC-001 — System Architecture Overview

> Documento âncora da arquitetura. Define o estilo arquitetural, a visão de contexto (C4 L1) e a visão de contêineres (C4 L2) do Trino Supply.

---

## 1. Objetivo e contexto

O Trino Supply é uma plataforma **SaaS Enterprise multi-tenant** de Gestão de Suprimentos que substitui planilhas, e-mails e controles descentralizados por um ambiente único, seguro, auditável e configurável (TPES-002).

Esta arquitetura precisa sustentar:

- Ciclo completo de suprimentos: Cadastro Mestre → Requisição → Workflow → RFQ → Equalização → Pedido → Recebimento → Estoque → Analytics.
- **Multi-tenant** com isolamento por `company_id` em toda operação.
- **Auditoria imutável** de operações críticas (SEC-001, FD-001-06).
- Evolução futura para IA, ERP e mobile **sem** que o MVP dependa delas.

---

## 2. Estilo arquitetural

| Decisão | Escolha | Justificativa |
|---------|---------|---------------|
| Estilo macro | **Monólito modular orientado a domínio** (Modular Monolith) | Baixo acoplamento por Bounded Context com deploy único simplifica operação no MVP; preparado para extração de serviços por contexto quando o volume justificar (ver ARC-003 §7). |
| Organização interna | **Bounded Contexts (DDD)** | Nunca por camadas técnicas globais; cada contexto é autônomo (TPES-002, ARC-002). |
| Estrutura de cada contexto | **Clean Architecture + DDD tático** | Domínio no centro, infraestrutura na borda (ARC-004). |
| Persistência | Banco como **projeção do domínio** | ADR-009. |
| Integração entre contextos | **Business Domain Events** (assíncrono) + contratos síncronos mínimos | ADR-010, ARC-005. |
| Capacidades compartilhadas | **Foundation** consumido por todos | ADR-011. |

> **Por que monólito modular e não microsserviços no MVP?** O acoplamento é controlado no nível de código (Bounded Contexts + eventos), não pela topologia de rede. Isso entrega os benefícios de modularidade sem o custo operacional de sistemas distribuídos, mantendo a costura por contratos (eventos) que permite extrair um contexto para serviço próprio sem reescrever o domínio. A decisão é revisável por ADR quando um contexto exigir escala independente.

---

## 3. C4 Nível 1 — Diagrama de Contexto

```text
                         ┌──────────────────────────────────────┐
      Solicitante ─────► │                                      │
      Aprovador  ─────► │                                      │ ─► E-mail (SMTP) [Notification]
      Comprador  ─────► │           TRINO SUPPLY               │
      Almoxarife ─────► │   Plataforma SaaS de Suprimentos     │ ─► (Roadmap) ERP externo
      Auditor    ─────► │                                      │ ─► (Roadmap) Provedor SSO/MFA
      Administrador ──► │                                      │
                         └──────────────────────────────────────┘
                                        ▲
                                        │ (Roadmap v2) integrações
                                        │  Fornecedor externo (portal RFQ)
```

**Atores (papéis, detalhados em FD-001-01 e nos `*-09-permissions`):** Solicitante, Aprovador, Comprador, Almoxarife/Estoquista, Auditor, Administrador do Sistema. Fornecedor externo entra no escopo com RFQ/Portal (roadmap).

**Sistemas externos:** servidor de e-mail (único externo do MVP, via Notification Center); ERP, SSO/MFA e portal de fornecedor são **roadmap** — a arquitetura os prevê como pontos de extensão, o MVP não depende deles.

---

## 4. C4 Nível 2 — Diagrama de Contêineres

```text
┌─────────────────────────────────────────────────────────────────────────┐
│                            NAVEGADOR (usuário)                            │
└───────────────────────────────┬───────────────────────────────────────────┘
                                 │ HTTPS (TLS 1.3)
                    ┌────────────▼────────────┐
                    │  Web App (Next.js/React)│  SSR + SPA, TypeScript, Tailwind
                    │  BFF opcional (Next API)│  apenas orquestração de UI
                    └────────────┬────────────┘
                                 │ HTTPS/JSON (REST /api/v1)  — JWT Bearer
                    ┌────────────▼────────────┐
                    │  API (ASP.NET Core .NET9)│  Modular Monolith
                    │  ┌────────────────────┐  │
                    │  │ Bounded Contexts   │  │  Foundation, Procurement,
                    │  │ (ver ARC-002)      │  │  Materials, Supplier, Contract,
                    │  └────────────────────┘  │  Receiving, Analytics, Admin, Audit
                    └───┬──────┬──────┬──────┬─┘
          ┌─────────────┘      │      │      └───────────────┐
   ┌──────▼──────┐   ┌─────────▼───┐ ┌▼──────────┐   ┌───────▼────────┐
   │ PostgreSQL  │   │   Redis     │ │ RabbitMQ  │   │     MinIO      │
   │ (SoT dados) │   │ (cache/     │ │ (eventos/ │   │ (anexos/       │
   │ schema por  │   │  sessão/    │ │  Outbox → │   │  documentos    │
   │ contexto)   │   │  rate-limit)│ │  broker)  │   │  FD-001-03)    │
   └─────────────┘   └─────────────┘ └───────────┘   └────────────────┘
```

| Contêiner | Tecnologia | Responsabilidade | Referência |
|-----------|-----------|------------------|-----------|
| Web App | Next.js, React, TypeScript, Tailwind | UI/UX, SSR, reflexo de permissões (server-side é a autoridade) | `*-14-ux`, DS-001 |
| API | ASP.NET Core / .NET 9 | Hospeda todos os Bounded Contexts; autoridade de autorização e regras | ARC-002, ARC-004 |
| PostgreSQL | PostgreSQL | Fonte de verdade transacional; um schema por contexto; projeção do domínio | ADR-009, `*-11-database` |
| Redis | Redis | Cache de leitura, cache de decisão de autorização (≤ 60 s), rate limiting, sessão | SEC-001 §4 |
| RabbitMQ | RabbitMQ | Transporte de Business Domain Events (exchange por suíte, ex. `trino.materials`) | ADR-010, ARC-005 |
| MinIO | MinIO (S3-compat) | Armazenamento de anexos e documentos | FD-001-03 |

---

## 5. Fluxo de requisição de referência

Exemplo: **submeter uma Requisição de Compra** (PR-001), ilustrando as camadas atravessadas.

```text
1. Web App  → POST /api/v1/purchase-requisitions/{id}/submit  (JWT Bearer, Idempotency-Key)
2. API      → AuthN (JWT) → AuthZ server-side (RBAC+ABAC+Escopo, deny by default)  [SEC-001, PR-001-09]
3. App Layer→ Command handler valida invariantes do Aggregate (Domain)             [PR-001-04]
4. Domain   → transição de estado (Draft→Submitted) + emite PurchaseRequisitionSubmitted [PR-001-03/05]
5. Infra    → persiste no PostgreSQL + grava evento na Outbox (mesma transação)     [ADR-009, ARC-005]
6. Outbox   → publisher relê e publica no RabbitMQ (at-least-once)                  [ARC-005]
7. Consumidores → Workflow (inicia aprovação), Notification, Audit, Timeline reagem  [FD-001-04/05/06/07]
8. API      → responde 200 + representação; Audit registra a decisão               [SEC-001 §12]
```

Este fluxo é o **contrato de referência** que todo módulo segue; variações estão nos respectivos `*-07-use-cases` e `*-13-api`.

---

## 6. Visão lógica de camadas (por contexto)

Detalhada em ARC-004. Resumo:

```text
┌──────────────────────────────────────────────┐
│ Presentation  — Controllers/Minimal APIs      │  contratos REST, sem regra de negócio
├──────────────────────────────────────────────┤
│ Application   — Use Cases / Commands / Queries│  orquestração, transações, Outbox
├──────────────────────────────────────────────┤
│ Domain        — Aggregates, VOs, Eventos, Pol.│  regras e invariantes (centro)
├──────────────────────────────────────────────┤
│ Infrastructure— EF Core, Redis, RabbitMQ, MinIO│  implementa portas do domínio
└──────────────────────────────────────────────┘
Dependências apontam sempre para o centro (Dependency Rule — Clean Architecture).
```

---

## 7. Restrições e premissas arquiteturais

| # | Restrição / Premissa | Origem |
|---|----------------------|--------|
| R1 | Stack fixa: Next.js/React/TS/Tailwind (front); .NET 9/ASP.NET Core (back); PostgreSQL/Redis/RabbitMQ/MinIO; Docker/GitHub Actions. | TPES-002 |
| R2 | Sem IA, sem integração ERP, sem app mobile no MVP; arquitetura preparada para todos. | TPES-002 |
| R3 | Multi-tenant lógico por `company_id` (não há banco por tenant no MVP). | ADR-009, SEC-001 |
| R4 | Autorização sempre server-side; frontend nunca é autoridade. | SEC-001, `*-09` |
| R5 | Toda operação crítica é auditada de forma imutável. | SEC-001, FD-001-06 |
| R6 | RabbitMQ só quando há real necessidade de assíncrono; caminho síncrono é o default de leitura. | TPES-002 |

---

## 8. Atributos de qualidade priorizados (ISO/IEC 25010)

| Atributo | Meta arquitetural | Onde é tratado |
|----------|-------------------|----------------|
| Segurança | Deny by default, Zero Trust, auditoria imutável | SEC-001, ARC-006 |
| Manutenibilidade | Modularidade por contexto, Clean Arch | ARC-002, ARC-004 |
| Confiabilidade | Outbox at-least-once, idempotência, DLQ | ARC-005 |
| Desempenho | Cache Redis, keyset pagination, índices `company_id`-first | ARC-006, `*-11-database` |
| Escalabilidade | Contexto extraível para serviço; stateless na API | ARC-003 |
| Auditabilidade | Eventos de negócio + trilha append-only | ADR-010, FD-001-06 |

Detalhamento e metas quantitativas em **ARC-006**.

---

## 9. Rastreabilidade

| Este documento | Deriva de / realiza |
|----------------|---------------------|
| Estilo (monólito modular, DDD) | TPES-002 (Domínios, Princípios) |
| Contêineres e stack | TPES-002 (Stack Tecnológica) |
| Persistência como projeção | ADR-009 |
| Eventos de negócio | ADR-010 |
| Foundation compartilhado | ADR-011, FD-001 |
| Segurança transversal | SEC-001 |
| Contextos e mapa | ARC-002 |
