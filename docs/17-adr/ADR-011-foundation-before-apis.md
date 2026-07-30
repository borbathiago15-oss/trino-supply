# ADR-011 — Foundation antes das APIs

**Status:** 🟢 Accepted
**Data:** 2026-07-30 (decisão original registrada inline em FD-001; formalizada nesta ADR no saneamento AUD-001)
**Criticidade:** 🔴 Core

---

## Contexto

Definir endpoints de API antes de estabelecer a linguagem comum da plataforma (identidade, organização, anexos, workflow, notificações, auditoria, timeline) gera retrabalho em todos os módulos subsequentes (RFQ, Purchase Order, Supplier, Contracts, Receiving, Analytics).

## Decisão

O domínio **Foundation** é especificado e construído **antes** das APIs dos módulos de negócio. Nenhum módulo funcional implementa autenticação, autorização, anexos, comentários, workflow, notificações, auditoria ou timeline por conta própria — todos consomem o Foundation.

Arquitetura de referência:

```text
Foundation
│
├── Identity & Access Management
├── Organization
├── Document Management
├── Workflow Engine
├── Notification Center
├── Audit
├── Timeline
├── Collaboration
├── Master Data
├── Configuration
└── Platform Settings

Procurement / Supplier Management / Contracts / Receiving / Analytics / Platform
```

## Consequências

* PR-001-13 (API) só será especificada após os documentos do Foundation necessários existirem.
* O Foundation não depende de módulos de negócio; módulos de negócio dependem do Foundation.
* Componentes compartilhados são implementados uma única vez.

## Referências

* FD-001 — Foundation Domain
* FD-001-01 — Identity & Access Management
