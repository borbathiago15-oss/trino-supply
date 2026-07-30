# GOV-002 — Document Registry

> Registro mestre de todos os documentos oficiais do Trino Supply.
> Este arquivo é o índice da Single Source of Truth. Nenhum documento é oficial se não estiver registrado aqui.

**Documento:** GOV-002 — Document Registry
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Criticidade:** 🔴 Core

---

## 1. Regras de Registro

1. Todo documento oficial possui ID único, versão, status e caminho.
2. Status válidos: `Draft`, `In Review`, `Approved`, `Superseded`, `Archived`.
3. Conflitos entre documentos são resolvidos a favor do documento com status `Approved` registrado aqui.
4. Documentos substituídos são movidos para `docs/90-archive/superseded/` e nunca reutilizados como referência.
5. Mudanças de arquitetura exigem ADR em `docs/17-adr/`.

---

## 2. Governança (00-governance)

| ID | Documento | Versão | Status | Caminho |
|----|-----------|--------|--------|---------|
| TPES-002 | Prompt Master — Trino Supply Product Engineering | 1.0.0 | 🟢 Approved | `docs/00-governance/prompt-master-trino-supply-product-engineering.md` |
| GOV-002 | Document Registry (este arquivo) | 1.0.0 | 🟢 Approved | `docs/00-governance/document-registry.md` |

---

## 3. Procurement — PR-001 Purchase Requisition

| ID | Documento | Versão | Status | Caminho |
|----|-----------|--------|--------|---------|
| PR-001 | Visão do Módulo | 1.1.0 | 🟢 Approved | `docs/03-business/procurement/PR-001/README.md` |
| PR-001-01 | Business Context | 1.1.0 | 🟢 Approved | `docs/03-business/procurement/PR-001/01-business-context.md` |
| PR-001-02 | Business Rules | 1.1.0 | 🟢 Approved | `docs/03-business/procurement/PR-001/02-business-rules.md` |
| PR-001-03 | State Machine | 1.1.0 | 🟢 Approved | `docs/03-business/procurement/PR-001/03-state-machine.md` |
| PR-001-04 | Domain Model | 1.1.0 | 🟢 Approved | `docs/03-business/procurement/PR-001/04-domain-model.md` |
| PR-001-05 | Event Storming | 1.1.0 | 🟢 Approved | `docs/03-business/procurement/PR-001/05-event-storming.md` |
| PR-001-06 | BPMN | 1.1.0 | 🟢 Approved | `docs/03-business/procurement/PR-001/06-bpmn.md` |
| PR-001-07 | Use Cases | 1.1.0 | 🟢 Approved | `docs/03-business/procurement/PR-001/07-use-cases.md` |
| PR-001-08 | User Stories | 1.0.0 | 🟢 Approved | `docs/03-business/procurement/PR-001/08-user-stories.md` |
| PR-001-09 | Permissions & Authorization | 1.1.0 | 🟢 Approved | `docs/03-business/procurement/PR-001/09-permissions.md` |
| PR-001-10 | Notifications | 1.1.0 | 🟢 Approved | `docs/03-business/procurement/PR-001/10-notifications.md` |
| PR-001-11 | Database Model | 1.1.0 | 🟢 Approved | `docs/06-database/procurement/PR-001/11-database.md` |
| PR-001-12 | Business Journey | 1.0.0 | 🟢 Approved | `docs/03-business/procurement/PR-001/12-business-journey.md` |
| PR-001-13 | API | — | ⚪ Planned | `docs/07-api/procurement/PR-001/` |
| PR-001-14 | UX | — | ⚪ Planned | — |
| PR-001-15 | Wireframes | — | ⚪ Planned | — |
| PR-001-16 | Acceptance Criteria | — | ⚪ Planned | — |
| PR-001-17 | Test Scenarios | — | ⚪ Planned | — |

---

## 4. Foundation — FD-001

| ID | Documento | Versão | Status | Caminho |
|----|-----------|--------|--------|---------|
| FD-001 | Foundation Domain | 1.0.0 | 🟢 Approved | `docs/03-business/foundation/FD-001/README.md` |
| FD-001-01 | Identity & Access Management (IAM) | 1.0.0 | 🟢 Approved | `docs/03-business/foundation/FD-001/01-identity-access-management.md` |
| FD-001-02 | Organization | 1.0.0 | 🟢 Approved | `docs/03-business/foundation/FD-001/02-organization.md` |
| FD-001-03 | Document Management | 1.0.0 | 🟢 Approved | `docs/03-business/foundation/FD-001/03-document-management.md` |
| FD-001-04 | Workflow Engine | 1.0.0 | 🟢 Approved | `docs/03-business/foundation/FD-001/04-workflow-engine.md` |
| FD-001-05 | Notification Center | 1.0.0 | 🟢 Approved | `docs/03-business/foundation/FD-001/05-notification-center.md` |
| FD-001-06 | Audit Service | 1.0.0 | 🟢 Approved | `docs/03-business/foundation/FD-001/06-audit-service.md` |
| FD-001-07 | Timeline Service | 1.0.0 | 🟢 Approved | `docs/03-business/foundation/FD-001/07-timeline-service.md` |
| FD-001-08 | Collaboration | — | ⚪ Planned | — |
| FD-001-09 | Master Data | — | ⚪ Planned | — |
| FD-001-10 | Configuration | — | ⚪ Planned | — |

---

## 5. Design System (05-design-system)

| ID | Documento | Versão | Status | Caminho |
|----|-----------|--------|--------|---------|
| DS-001 | Brand & Logo | 1.0.0 | 🟢 Approved | `docs/05-design-system/README.md` |
| DS-ASSET-001 | Logo oficial — área de login (PNG 1448×1086) | 1.0.0 | 🟢 Approved | `docs/05-design-system/assets/logo/trino-supply-logo-login.png` |

---

## 6. Segurança (08-security)

| ID | Documento | Versão | Status | Caminho |
|----|-----------|--------|--------|---------|
| SEC-001 | Arquitetura de Segurança | 1.0.0 | 🟢 Approved | `docs/08-security/01-security-architecture.md` |
| SEC-002 | Threat Model (STRIDE) | 1.0.0 | 🟢 Approved | `docs/08-security/02-threat-model-stride.md` |
| SEC-003 | Padrão de Desenvolvimento Seguro | 1.0.0 | 🟢 Approved | `docs/08-security/03-secure-development-standard.md` |

---

## 7. ADRs (17-adr)

| ID | Decisão | Status | Caminho |
|----|---------|--------|---------|
| ADR-009 | Banco de dados como projeção do domínio | 🟢 Accepted | `docs/17-adr/ADR-009-database-as-domain-projection.md` |
| ADR-010 | Eventos de negócio como linguagem oficial | 🟢 Accepted | `docs/17-adr/ADR-010-business-domain-events.md` |
| ADR-011 | Foundation antes das APIs | 🟢 Accepted | `docs/17-adr/ADR-011-foundation-before-apis.md` |

---

## 8. Arquivados (Superseded)

| ID | Documento | Motivo | Substituído por | Caminho |
|----|-----------|--------|-----------------|---------|
| TS-PRQ-003 | Máquina de Estados (rascunho) | Conflito de estados com o documento oficial | PR-001-03 | `docs/90-archive/superseded/TS-PRQ-003-maquina-de-estados.md` |
| TS-PRQ-004 | Matriz de Permissões (rascunho) | Conflito de permissões com o documento oficial | PR-001-09 | `docs/90-archive/superseded/TS-PRQ-004-matriz-de-permissoes.md` |

---

## 9. Decisões Abertas

| Tema | Origem | Situação |
|------|--------|----------|
| Aggregate enxuto do PR-001 (referências ao Foundation em vez de tabelas próprias de attachment/comment/approval) | PR-001-11, seção 14 | Em análise — exigirá ADR própria |
| Business Event Catalog corporativo | PR-001-05, seção 13 | Aprovado para roadmap, sem data |
| Mecanismo de pseudonimização LGPD compatível com cadeia de hash (cofre selado vs. HMAC com chave rotacionada) | FD-001-06, seção 6 | Em análise — exigirá ADR antes da v2 do Audit Service |

---

## 10. Histórico de Governança

| Data | Evento | Referência |
|------|--------|------------|
| 2026-07-30 | Saneamento do repositório: caminhos recursivos corrigidos, duplicata de `08-user-stories.md` removida, numeração do índice do módulo alinhada aos IDs reais, série TS-PRQ arquivada, Jornada promovida a PR-001-12 | AUD-001 |
| 2026-07-30 | FD-001-02 — Organization criado e aprovado (versão 1.0.0) | FD-001-02 |
| 2026-07-30 | FD-001-03 — Document Management criado e aprovado (versão 1.0.0) | FD-001-03 |
| 2026-07-30 | Elevação Enterprise do módulo PR-001: PR-001 (README) e PR-001-01 a PR-001-11 revisados para a versão 1.1.0 (todo conteúdo original preservado; PR-001-08 User Stories mantido em 1.0.0 por não constar do escopo da revisão) | PR-001 |
| 2026-07-30 | Pacote de segurança criado e aprovado: SEC-001 (Arquitetura de Segurança), SEC-002 (Threat Model STRIDE) e SEC-003 (Padrão de Desenvolvimento Seguro), versão 1.0.0; nova seção 08-security registrada | SEC-001, SEC-002, SEC-003 |
| 2026-07-30 | FD-001-04 — Workflow Engine criado e aprovado (versão 1.0.0): definições versionadas, routing rules, níveis sequenciais/paralelos, SLA, escalonamento, delegação, compensações e eventos WF-EVT | FD-001-04 |
| 2026-07-30 | FD-001-05 — Notification Center criado e aprovado (versão 1.0.0): canais SYSTEM + EMAIL, templates versionados, filas por prioridade, retry/DLQ, preferências, digest, escalonamento e auditoria de entrega | FD-001-05 |
| 2026-07-30 | FD-001-06 — Audit Service criado e aprovado (versão 1.0.0): trilha append-only particionada, cadeia de hash, retenção 5 anos, LGPD/LegalHold, exportação assinada e APIs administrativas | FD-001-06 |
| 2026-07-30 | FD-001-07 — Timeline Service criado e aprovado (versão 1.0.0): projeção reconstruível de linha do tempo por entidade, MilestoneMapper configurável, visibilidade em 4 regras, rebuild por escopo e APIs funcionais | FD-001-07 |
