# ADR-010 — Eventos de negócio como linguagem oficial

**Status:** 🟢 Accepted
**Data:** 2026-07-30 (decisão original registrada inline em PR-001-05; formalizada nesta ADR no saneamento AUD-001)
**Criticidade:** 🔴 Core

---

## Contexto

Sistemas orientados a eventos podem degradar para eventos técnicos (`RowUpdated`, `RecordSaved`, `APIExecuted`), que não carregam significado de negócio e inviabilizam integrações, automações e auditoria semântica.

## Decisão

Todos os eventos da plataforma são **Business Domain Events**, nomeados como fatos consumados do negócio:

✅ `PurchaseRequisitionSubmitted`

❌ `RowUpdated`, `RecordSaved`, `APIExecuted`, `SaveButtonClicked`, `SQLExecuted`

## Consequências

* Eventos técnicos são proibidos como eventos de domínio.
* O catálogo de eventos por módulo (ex.: PR-001-05) é a referência para APIs, workflow, notificações, auditoria e integrações futuras.
* Preparado para evolução a um **Business Event Catalog** corporativo (proposta registrada em GOV-002, seção 8).

## Referências

* PR-001-05 — Event Storming
* GOV-002 — Document Registry
