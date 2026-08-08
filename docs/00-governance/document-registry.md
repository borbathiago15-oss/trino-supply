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
| PR-001-13 | API | 1.0.0 | 🟢 Approved | `docs/07-api/procurement/PR-001/README.md` |
| PR-001-14 | UX | 1.0.0 | 🟢 Approved | `docs/03-business/procurement/PR-001/14-ux.md` |
| PR-001-15 | Wireframes | 1.0.0 | 🟢 Approved | `docs/03-business/procurement/PR-001/15-wireframes.md` |
| PR-001-16 | Acceptance Criteria | 1.0.0 | 🟢 Approved | `docs/03-business/procurement/PR-001/16-acceptance-criteria.md` |
| PR-001-17 | Test Scenarios | 1.0.0 | 🟢 Approved | `docs/03-business/procurement/PR-001/17-test-scenarios.md` |

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
| FD-001-08 | Collaboration | 1.0.0 | 🟢 Approved | `docs/03-business/foundation/FD-001/08-collaboration.md` |
| FD-001-09 | Master Data | 1.0.0 | 🟢 Approved | `docs/03-business/foundation/FD-001/09-master-data.md` |
| FD-001-10 | Configuration | 1.0.0 | 🟢 Approved | `docs/03-business/foundation/FD-001/10-configuration.md` |

---

## 5. Materials Management — MMS-001 (Suíte de Materiais e Estoque)

| ID | Documento | Versão | Status | Caminho |
|----|-----------|--------|--------|---------|
| MMS-001 | Materials Management Suite — Documento Mestre Funcional | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-001/README.md` |
| MMS-002 | Item Catalog | 1.2.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/README.md` |
| MMS-002-01 | Item Catalog — Business Context | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/01-business-context.md` |
| MMS-002-02 | Item Catalog — Business Rules | 1.2.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/02-business-rules.md` |
| MMS-002-03 | Item Catalog — State Machine | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/03-state-machine.md` |
| MMS-002-04 | Item Catalog — Domain Model | 1.1.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/04-domain-model.md` |
| MMS-002-05 | Item Catalog — Event Storming | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/05-event-storming.md` |
| MMS-002-06 | Item Catalog — BPMN | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/06-bpmn.md` |
| MMS-002-07 | Item Catalog — Use Cases | 1.1.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/07-use-cases.md` |
| MMS-002-08 | Item Catalog — User Stories | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/08-user-stories.md` |
| MMS-002-09 | Item Catalog — Permissions | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/09-permissions.md` |
| MMS-002-10 | Item Catalog — Notifications | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/10-notifications.md` |
| MMS-002-11 | Item Catalog — Database Model | 1.1.0 | 🟢 Approved | `docs/06-database/materials/MMS-002/11-database.md` |
| MMS-002-12 | Item Catalog — Business Journey | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/12-business-journey.md` |
| MMS-002-13 | Item Catalog — API | 1.1.0 | 🟢 Approved | `docs/07-api/materials/MMS-002/README.md` |
| MMS-002-14 | Item Catalog — UX | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/14-ux.md` |
| MMS-002-15 | Item Catalog — Wireframes | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/15-wireframes.md` |
| MMS-002-16 | Item Catalog — Acceptance Criteria | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/16-acceptance-criteria.md` |
| MMS-002-17 | Item Catalog — Test Scenarios | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/17-test-scenarios.md` |
| MMS-003 | Material Requisition | 1.1.0 | 🟢 Approved | `docs/03-business/materials/MMS-003/README.md` |
| MMS-004 | Inventory Management (Estoque) | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-004/README.md` |
| MMS-004-01 | Inventory Management — Business Context | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-004/01-business-context.md` |
| MMS-004-02 | Inventory Management — Business Rules | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-004/02-business-rules.md` |
| MMS-004-03 | Inventory Management — State Machine | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-004/03-state-machine.md` |
| MMS-004-04 | Inventory Management — Domain Model | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-004/04-domain-model.md` |
| MMS-004-05 | Inventory Management — Event Storming | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-004/05-event-storming.md` |
| MMS-004-06 | Inventory Management — BPMN | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-004/06-bpmn.md` |
| MMS-004-07 | Inventory Management — Use Cases | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-004/07-use-cases.md` |
| MMS-004-08 | Inventory Management — User Stories | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-004/08-user-stories.md` |
| MMS-004-09 | Inventory Management — Permissions | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-004/09-permissions.md` |
| MMS-005 | Receiving | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-005/README.md` |

---

## 5.1 Arquitetura (04-architecture)

| ID | Documento | Versão | Status | Caminho |
|----|-----------|--------|--------|---------|
| ARC-000 | Architecture Overview (Índice da seção) | 1.0.0 | 🟢 Approved | `docs/04-architecture/README.md` |
| ARC-001 | System Architecture Overview | 1.0.0 | 🟢 Approved | `docs/04-architecture/01-system-architecture-overview.md` |
| ARC-002 | Bounded Contexts & Context Map | 1.0.0 | 🟢 Approved | `docs/04-architecture/02-bounded-contexts-context-map.md` |
| ARC-003 | Solution & Deployment Architecture | 1.0.0 | 🟢 Approved | `docs/04-architecture/03-solution-deployment-architecture.md` |
| ARC-004 | Application Architecture | 1.0.0 | 🟢 Approved | `docs/04-architecture/04-application-architecture.md` |
| ARC-005 | Integration & Eventing Architecture | 1.0.0 | 🟢 Approved | `docs/04-architecture/05-integration-eventing.md` |
| ARC-006 | Cross-Cutting Concerns & NFRs | 1.0.0 | 🟢 Approved | `docs/04-architecture/06-cross-cutting-nfr.md` |
| ARC-007 | Domain Benchmark: Odoo & ERPNext (anexo informativo) | 1.0.0 | 🟢 Approved (informativo) | `docs/04-architecture/07-domain-benchmark-erp.md` |

---

## 5.2 Placeholders estruturais (pastas oficiais TPES-002 ainda sem conteúdo aprovado)

| ID | Documento | Versão | Status | Caminho |
|----|-----------|--------|--------|---------|
| VIS-000 | Vision (índice/placeholder) | 0.1.0 | 🟡 Draft | `docs/01-vision/README.md` |
| PRD-000 | Product (índice/placeholder) | 0.1.0 | 🟡 Draft | `docs/02-product/README.md` |
| QA-000 | Testing (índice/placeholder) | 0.1.0 | 🟡 Draft | `docs/09-testing/README.md` |
| OPS-000 | DevOps (índice/placeholder) | 0.1.0 | 🟡 Draft | `docs/10-devops/README.md` |
| REL-000 | Release (índice/placeholder) | 0.1.0 | 🟡 Draft | `docs/11-release/README.md` |
| DOC-000 | User Guides (índice/placeholder) | 0.1.0 | 🟡 Draft | `docs/12-user-guides/README.md` |

---

## 6. Design System (05-design-system)

| ID | Documento | Versão | Status | Caminho |
|----|-----------|--------|--------|---------|
| DS-001 | Brand & Logo | 1.0.0 | 🟢 Approved | `docs/05-design-system/README.md` |
| DS-ASSET-001 | Logo oficial — área de login (PNG 1448×1086) | 1.0.0 | 🟢 Approved | `docs/05-design-system/assets/logo/trino-supply-logo-login.png` |

---

## 7. Segurança (08-security)

| ID | Documento | Versão | Status | Caminho |
|----|-----------|--------|--------|---------|
| SEC-001 | Arquitetura de Segurança | 1.0.0 | 🟢 Approved | `docs/08-security/01-security-architecture.md` |
| SEC-002 | Threat Model (STRIDE) | 1.0.0 | 🟢 Approved | `docs/08-security/02-threat-model-stride.md` |
| SEC-003 | Padrão de Desenvolvimento Seguro | 1.0.0 | 🟢 Approved | `docs/08-security/03-secure-development-standard.md` |

---

## 8. ADRs (17-adr)

| ID | Decisão | Status | Caminho |
|----|---------|--------|---------|
| ADR-009 | Banco de dados como projeção do domínio | 🟢 Accepted | `docs/17-adr/ADR-009-database-as-domain-projection.md` |
| ADR-010 | Eventos de negócio como linguagem oficial | 🟢 Accepted | `docs/17-adr/ADR-010-business-domain-events.md` |
| ADR-011 | Foundation antes das APIs | 🟢 Accepted | `docs/17-adr/ADR-011-foundation-before-apis.md` |
| ADR-012 | Posicionamento da Materials Management Suite no roadmap de módulos | 🟢 Accepted | `docs/17-adr/ADR-012-materials-management-suite-roadmap.md` |
| ADR-013 | Conversão de Unidade de Medida (compra × estoque) no Item Catalog | 🟢 Accepted | `docs/17-adr/ADR-013-unit-of-measure-conversion.md` |
| ADR-014 | Motor de Regras de Reposição (configurável) | 🟢 Accepted | `docs/17-adr/ADR-014-replenishment-rules-engine.md` |

---

## 9. Arquivados (Superseded)

| ID | Documento | Motivo | Substituído por | Caminho |
|----|-----------|--------|-----------------|---------|
| TS-PRQ-003 | Máquina de Estados (rascunho) | Conflito de estados com o documento oficial | PR-001-03 | `docs/90-archive/superseded/TS-PRQ-003-maquina-de-estados.md` |
| TS-PRQ-004 | Matriz de Permissões (rascunho) | Conflito de permissões com o documento oficial | PR-001-09 | `docs/90-archive/superseded/TS-PRQ-004-matriz-de-permissoes.md` |

---

## 10. Decisões Abertas

| Tema | Origem | Situação |
|------|--------|----------|
| Aggregate enxuto do PR-001 (referências ao Foundation em vez de tabelas próprias de attachment/comment/approval) | PR-001-11, seção 14 | Em análise — exigirá ADR própria |
| Business Event Catalog corporativo | PR-001-05, seção 13 | Aprovado para roadmap, sem data |
| Motor de Regras de Reposição — sugestão revisável já no MVP do MMS-004 vs. integralmente na v1.1/v2.0; catálogo inicial de políticas de quantidade | ARC-007 (P1 #20), ADR-014 | **Resolvida (2026-08-08):** ADR-014 aceita — sugestão revisável na v1.1, execução automática na v2.0 (opt-in); 3 políticas de quantidade iniciais |
| Landed costs / valoração fiscal de estoque (FIFO/AVCO/standard) | ARC-007 (#16/#17), MMS-001 §8.3 | Fora do escopo do MVP por decisão; exigirá ADR quando a valoração fiscal entrar no roadmap |
| Mecanismo de pseudonimização LGPD compatível com cadeia de hash (cofre selado vs. HMAC com chave rotacionada) | FD-001-06, seção 6 | Em análise — exigirá ADR antes da v2 do Audit Service |

---

## 11. Histórico de Governança

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
| 2026-07-30 | FD-001-08 — Collaboration criado e aprovado (versão 1.0.0): comentários com threading, menções com notificação, edição com histórico, moderação, sanitização XSS, rate limit e herança de permissão da entidade | FD-001-08 |
| 2026-07-30 | FD-001-09 — Master Data criado e aprovado (versão 1.0.0): catálogos de referência com códigos imutáveis, vigência temporal, hierarquia sem ciclos, cache com invalidação por evento e fronteira formal com Organization | FD-001-09 |
| 2026-07-30 | FD-001-10 — Configuration criado e aprovado (versão 1.0.0): definições tipadas, escopos com precedência, feature flags, SecretRef, dual control e ConfigSnapshot — **Foundation 100% documentado (10/10 domínios, ADR-011)** | FD-001-10 |
| 2026-07-30 | PR-001-13 — API do Purchase Requisition criada e aprovada (versão 1.0.0): convenções (envelope, keyset, idempotência, autorização), 20 endpoints mapeados a UC/permissão/evento, catálogo PR-ERR, segurança e versionamento | PR-001-13 |
| 2026-07-30 | PR-001-14 — UX do Purchase Requisition criado e aprovado (versão 1.0.0): princípios, personas, arquitetura de informação, jornadas por papel, padrões de interação, acessibilidade WCAG 2.1 AA, responsividade, i18n e métricas | PR-001-14 |
| 2026-07-30 | PR-001-15 — Wireframes do Purchase Requisition criado e aprovado (versão 1.0.0): 7 wireframes (lista, formulário, detalhe, fila de aprovações, detalhe de aprovação, notificações, configurações) com zonas, variantes responsivas, estados e matriz de rastreabilidade | PR-001-15 |
| 2026-07-30 | PR-001-16 — Acceptance Criteria do Purchase Requisition criado e aprovado (versão 1.0.0): 68 critérios formais em Given/When/Then cobrindo UCs, regras transversais, workflow, eventos, notificações, API, UX e NFRs, com prioridades P0/P1/P2, matriz de rastreabilidade e alinhamento ao DoD | PR-001-16 |
| 2026-07-30 | MMS-001 — Materials Management Suite criada e aprovada (versão 1.0.0): Documento Mestre Funcional da suíte de materiais/estoque com módulos MMS-002..005 (Item Catalog, Material Requisition, Inventory Management, Receiving), fluxo corporativo oficial, fronteira formal com PR-001 e roadmap; nova seção 5 no registry e decisão aberta de roadmap de módulos | MMS-001 |
| 2026-07-30 | ADR-012 — Posicionamento da Materials Management Suite no roadmap aceita: MMS como domínio oficial, "Inventory (futuro)" = MMS-004, "Receiving" = MMS-005 (propriedade MMS), Item Catalog e Material Requisition adicionados ao roadmap, Purchasing mantido em Procurement, ordem oficial de módulos atualizada; decisão aberta do GOV-002 encerrada | ADR-012 |
| 2026-07-30 | MMS-002 — Item Catalog criado e aprovado (versão 1.0.0): visão do módulo de catálogo de itens com ciclo de vida (Rascunho/Ativo/Inativo), classificação (estocável/não estocável/sob encomenda), parâmetros de reposição, sinônimos, fronteira formal com Master Data e roadmap | MMS-002 |
| 2026-07-30 | MMS-004 — Inventory Management criado e aprovado (versão 1.0.0): visão do módulo Estoque com saldos derivados de movimentação (MMS-P-08), seis documentos de movimentação, reservas com validade, endereçamento, bloqueios de integridade, alertas, inventário e roadmap | MMS-004 |
| 2026-07-30 | MMS-003 — Material Requisition criado e aprovado (versão 1.0.0): visão do módulo de solicitação interna ao almoxarifado com workflow (FD-001-04), validação de estoque após aprovação (MMS-RG-09), rota mista por item, demanda de compra via PR-001 com rastreabilidade bidirecional, acompanhamento consolidado, entrega e confirmação | MMS-003 |
| 2026-07-30 | MMS-005 — Receiving criado e aprovado (versão 1.0.0): visão do módulo de recebimento físico contra documento de origem (MMS-RG-06), conferência quantitativa, divergências com destino documentado (MMS-RG-07), entrada no estoque via MMS-004, retomada de atendimento pendente e compra dedicada — **suíte MMS 100% com visões aprovadas (MMS-001..005)** | MMS-005 |
| 2026-07-30 | PR-001-17 — Test Scenarios do Purchase Requisition criado e aprovado (versão 1.0.0): catálogo completo de cenários (TC-001..006, 55 TC-UC, transversais, workflow, eventos, notificações, API, UX, wireframes, UI×API, segurança, performance, banco), estratégia de automação por camada, matriz de rastreabilidade 100% (regra × UC × API × AC × teste) e alinhamento ao DoD — **pacote PR-001 100% documentado (17/17), apto a implementação** | PR-001-17 |
| 2026-07-30 | MMS-002-01 — Business Context do Item Catalog criado e aprovado (versão 1.0.0): contexto, problema, objetivos, stakeholders, personas, premissas, restrições, gatilhos, entradas/saídas, indicadores, KPIs, riscos, glossário, princípios e dependências — **início do pacote funcional detalhado do MMS-002 (padrão PR-001)** | MMS-002-01 |
| 2026-07-30 | MMS-002-02 — Business Rules do Item Catalog criado e aprovado (versão 1.0.0): 28 regras codificadas (IC-BR-001..071) em 8 famílias, com validação, erros IC-ERR, eventos, UCs/API/testes conceituais, configurações `materials.item.*` e matriz de rastreabilidade 100% | MMS-002-02 |
| 2026-07-30 | Revisão EPI/Fardamento aprovada (versões 1.1.0): **MMS-002** — atributos de EPI/Fardamento no Item Catalog (CA obrigatório para grupo EPI, grade de tamanhos, imagem via FD-001-03, código externo do ERP; imagem antecipada da v2.0 para o MVP); **MMS-002-02** — nova família de regras IC-BR-080..083, rastreabilidade 32/32; **MMS-003** — motivo estruturado (FD-001-09), anexos (FD-001-03), aprovação parcial por item (alterar quantidades/rejeitar itens), cadastro de locais de entrega, visão do almoxarifado com filtros, indicadores de consumo/custo; decisões registradas sem ADR (extensão funcional dentro das fronteiras existentes) | MMS-002, MMS-002-02, MMS-003 |
| 2026-07-30 | MMS-002-03 — State Machine do Item Catalog criada e aprovada (versão 1.0.0): 4 estados (Rascunho, Ativo, Inativo, Descartado) com entry/exit actions, eventos aceitos/rejeitados, guard conditions (IC-BR-020..024/040–042/080–083), side effects, SLA, responsável e auditoria; matriz de transições e matriz de editabilidade por estado — padrão PR-001-03 | MMS-002-03 |
| 2026-07-30 | MMS-002-04 — Domain Model do Item Catalog criado e aprovado (versão 1.0.0): Aggregate Root Item (Synonym, ReplenishmentParameters), Aggregate Diagram, UML, Value Objects completos, 11 invariantes INV-IC rastreadas às IC-BR, factory methods (incluindo CreateEPI), repositories, 10 specifications, 5 domain services, 5 policies e limites do Aggregate — padrão PR-001-04 | MMS-002-04 |
| 2026-07-30 | MMS-002-05 — Event Storming do Item Catalog criado e aprovado (versão 1.0.0): 13 comandos CMD-IC, 8 eventos de domínio EVT-IC (alinhados à visão MMS-002 v1.1.0), 5 políticas, validações automáticas rastreadas às IC-BR, matriz evento × reação, eventos consumidos do FD-001-09 e especificação técnica completa por evento (exchange trino.materials, Outbox, at-least-once, DLQ, idempotência, ordenação por agregado) — padrão PR-001-05 | MMS-002-05 |
| 2026-07-30 | MMS-002-06 — BPMN do Item Catalog criado e aprovado (versão 1.0.0): ciclo de vida completo (cadastro → parametrização → ativação → manutenção → inativação → reativação/descarte), 2 gateways com regras RG-IC-GW, 4 fluxos alternativos (incl. impacto de Master Data), 6 Business Objects, 5 Data Stores, 9 Message Events, 7 exceções, SLA, escalonamentos, timeouts, compensações e sub-processo de validação de ativação — padrão PR-001-06 | MMS-002-06 |
| 2026-07-30 | MMS-002-07 — Use Cases do Item Catalog criados e aprovados (versão 1.0.0): UC-IC-001..007 em especificação UML completa (fluxos principal/alternativos/exceção, pré/pós-condições, regras, eventos, mensagens, validações, APIs /api/v1/items, permissões IC-PERM, testes TC-IC) + matriz de rastreabilidade — padrão PR-001-07 | MMS-002-07 |
| 2026-07-30 | MMS-002-08 — User Stories do Item Catalog criadas e aprovadas (versão 1.0.0): 15 stories US-IC-001..015 em 5 Features com persona, valor de negócio, critérios Gherkin, regras, eventos, UC e matriz de rastreabilidade — padrão PR-001-08 | MMS-002-08 |
| 2026-07-30 | MMS-002-09 — Permissions do Item Catalog criado e aprovado (versão 1.0.0): modelo híbrido RBAC+ABAC+Escopo, 4 papéis, 10 permissões IC-PERM, restrições por estado, policies POL-IC-AUTH, JWT claims, scopes items.*, Permission Matrix, SoD, Inheritance, Delegation e Evaluation Flow — padrão PR-001-09 | MMS-002-09 |
| 2026-07-30 | MMS-002-10 — Notifications do Item Catalog criado e aprovado (versão 1.0.0): 6 eventos notificáveis (parcimônia), 6 templates IC-NOT, especificação operacional (queues, prioridades, retry, template version, localization, scheduling, escalonamento, preferências, NOT-IC-BR-001..011, auditoria de entrega) — padrão PR-001-10 | MMS-002-10 |
| 2026-07-30 | MMS-002-11 — Database Model do Item Catalog criado e aprovado (versão 1.0.0): DDL PostgreSQL completo no schema `materials` (ADR-009), tabelas item/item_synonym/item_replenishment_parameters com aggregate enxuto, unicidade soft-delete-ciente (company_id,code) e erp_code, constraints, índices parciais company_id-first, triggers TRG-IC-001..003, 3 views + 1 MV, keyset pagination, planos de indexação/performance/migração/backup, versionamento de schema — padrão PR-001-11 | MMS-002-11 |
| 2026-07-30 | MMS-002-12 — Business Journey do Item Catalog criado e aprovado (versão 1.0.0): jornada macro (necessidade→cadastro→enriquecimento→ativação→consumo→manutenção→inativação/descarte), jornadas do mantenedor, do solicitante (busca por sinônimo) e do auditor, pontos de dor, momentos de verdade e KPIs por etapa — padrão PR-001-12 | MMS-002-12 |
| 2026-07-30 | MMS-002-13 — API do Item Catalog criada e aprovada (versão 1.0.0): convenções (envelope, keyset, idempotência, autorização, rate limit), 20 endpoints `/api/v1/items` mapeados a UC/permissão/evento, modelos de recursos (Item, ItemSynonym, ItemListView, CompletenessCheckView), catálogo de erros IC-ERR → HTTP, segurança, versionamento, NFRs e roadmap — padrão PR-001-13 (docs/07-api) | MMS-002-13 |
| 2026-07-30 | MMS-002-14 — UX do Item Catalog criado e aprovado (versão 1.0.0): princípios IC-UX-001..008, personas, arquitetura de informação (IC-SCR-01..05), jornadas por papel (mantenedor, solicitante via componente de busca, auditor, admin), padrões de interação, WCAG 2.1 AA, responsividade, i18n, DS-001, métricas e critérios de aceite — padrão PR-001-14 | MMS-002-14 |
| 2026-07-30 | MMS-002-15 — Wireframes do Item Catalog criados e aprovados (versão 1.0.0): WF-IC-01..06 (lista, formulário com checklist de completude, detalhe com ciclo de vida, notificações, configurações, componente de busca por sinônimo) com zonas, variantes responsivas, estados, matriz de rastreabilidade e critérios CA-WF-IC — padrão PR-001-15 | MMS-002-15 |
| 2026-07-30 | MMS-002-16 — Acceptance Criteria do Item Catalog criados e aprovados (versão 1.0.0): 72 critérios AC-IC em Given/When/Then (P0/P1/P2) cobrindo UC-IC-001..007, regras transversais, ciclo de vida, eventos, notificações, API, UX e NFRs, com matriz de rastreabilidade e alinhamento ao DoD — padrão PR-001-16 | MMS-002-16 |
| 2026-07-30 | MMS-002-17 — Test Scenarios do Item Catalog criados e aprovados (versão 1.0.0): 45 cenários TC-IC por caso de uso + transversais, ciclo de vida, eventos, notificações, API, UX, wireframes, UI×API, segurança, performance e banco; estratégia por camada; matriz de rastreabilidade 100% (27/27 regras, 72/72 ACs) — **fecha o pacote MMS-002 (17/17)** — padrão PR-001-17 | MMS-002-17 |
| 2026-07-30 | MMS-004-01 — Business Context do Inventory Management criado e aprovado (versão 1.0.0): contexto, problema, objetivos, stakeholders, personas, premissas, restrições, gatilhos, entradas/saídas, KPIs (acuracidade ≥ 98%), riscos, glossário e dependências — padrão MMS-002-01 | MMS-004-01 |
| 2026-07-30 | MMS-004-02 — Business Rules do Inventory Management criadas e aprovadas (versão 1.0.0): 40 regras IV-BR em 13 famílias (integridade de saldo, entrada/saída, reserva, transferência, ajuste, locais, segregação, inventário, alertas, auditoria, segurança, performance, EPI/Fardamento) com IV-ERR, eventos, configurações `materials.inventory.*` e rastreabilidade 100% — padrão MMS-002-02 | MMS-004-02 |
| 2026-07-30 | MMS-004-03 — State Machine do Inventory Management criada e aprovada (versão 1.0.0): 4 entidades com ciclo de vida (Documento de Movimentação, Reserva, Ajuste, Inventário — 4 estados cada) com entry/exit actions, guards, side effects, SLA, 4 matrizes de transição e eventos de domínio — padrão MMS-002-03 | MMS-004-03 |
| 2026-07-30 | MMS-004-04 — Domain Model do Inventory Management criado e aprovado (versão 1.0.0): 5 Aggregate Roots (StockMovement, Reservation, Adjustment, InventoryCount, Location) + StockBalance como projeção não editável; 13 Value Objects; 16 invariantes INV-IV; factories; repositories sem escrita de saldo; 13 specifications; 9 domain services; 9 policies — padrão MMS-002-04 | MMS-004-04 |
| 2026-07-30 | MMS-004-05 — Event Storming do Inventory Management criado e aprovado (versão 1.0.0): 20 comandos, 16 eventos EVT-IV, 10 políticas, validações, matriz evento×reação, 5 eventos consumidos e fichas técnicas ADR-010 (outbox, at-least-once, DLQ, ordenação por agregado/chave de saldo) — padrão MMS-002-05 | MMS-004-05 |
| 2026-07-30 | MMS-004-06 — BPMN do Inventory Management criado e aprovado (versão 1.0.0): 7 fluxos principais (entrada, saída/atendimento, reserva com timer de vencimento, transferência, ajuste com aprovação, inventário com ajuste de divergências, estorno), 5 gateways RG-IV-GW, 6 fluxos alternativos, 10 Business Objects, 6 Data Stores, 20 Message Events, 7 exceções, SLA, escalonamentos, timeouts, timers, compensações e 2 sub-processos — padrão MMS-002-06 | MMS-004-06 |
| 2026-07-30 | MMS-004-07 — Use Cases do Inventory Management criados e aprovados (versão 1.0.0): UC-IV-001..011 (entrada, saída/atendimento, criar reserva, liberar/vencer, transferência, ajuste com aprovação, inventário, estorno, locais, alertas, posição/extrato) em especificação UML completa + matriz de rastreabilidade com 100% de cobertura de regras (40/40) e eventos (16/16) — padrão MMS-002-07 | MMS-004-07 |
| 2026-07-30 | MMS-004-08 — User Stories do Inventory Management criadas e aprovadas (versão 1.0.0): 16 stories US-IV-001..016 em 5 Features (Movimentação, Reservas, Ajustes, Inventário, Consulta/Visão/Alertas) com persona, Gherkin, regras, eventos e matriz de rastreabilidade — padrão MMS-002-08 | MMS-004-08 |
| 2026-07-30 | MMS-004-09 — Permissions do Inventory Management criado e aprovado (versão 1.0.0): modelo híbrido RBAC+ABAC+Escopo, 6 papéis (Requester com negação explícita da visão do almoxarifado), 11 permissões IV-PERM, restrições por estado das 4 entidades, policies POL-IV-AUTH, JWT claims, scopes inventory.*, Permission Matrix, SoD (SOD-IV-001/002 não desligáveis), Inheritance, Delegation e Evaluation Flow — padrão MMS-002-09 | MMS-004-09 |
| 2026-08-08 | Seção de Arquitetura criada e aprovada (04-architecture, versão 1.0.0): **ARC-000** (índice), **ARC-001** System Architecture Overview (C4 L1–L2, monólito modular orientado a domínio, fluxo de requisição de referência, atributos de qualidade), **ARC-002** Bounded Contexts & Context Map (9 contextos oficiais alinhados ao TPES-002, Context Map DDD, mapa contexto→schema→exchange, regras de fronteira), **ARC-003** Solution & Deployment Architecture (topologia de contêineres, ambientes, dados/backup, rede/hardening Zero Trust, escala e extração de contexto, contrato CI/CD), **ARC-004** Application Architecture (Clean Architecture + DDD tático, fluxos Command/Query, Outbox, contrato com Foundation, estrutura de solução .NET por contexto), **ARC-005** Integration & Eventing (estilos síncrono/assíncrono, Transactional Outbox, topologia RabbitMQ, at-least-once/idempotência/ordenação, envelope de evento como Published Language, ACL para externos), **ARC-006** Cross-Cutting & NFRs (multi-tenancy, segurança, auditoria, observabilidade, resiliência, performance, configurabilidade, LGPD, matriz de rastreabilidade e índice de ADRs) — formaliza a camada de arquitetura da metodologia TPE antes do avanço de Database/API para os demais módulos; consolida ADR-009/010/011/012 | ARC-001..006 |
| 2026-08-08 | **v1.2 do MMS-002 concluída (superfície UC/API):** **MMS-002-07 Use Cases → 1.1.0** (novo UC-IC-008 Gerenciar Unidades Alternativas e Conversões; UC-IC-007 estendido com política/rota de reposição) e **MMS-002-13 API → 1.1.0** (recurso `Item` com `baseUnitOfMeasure`/`alternativeUnits`/parâmetros de reposição estendidos; +4 endpoints `/base-unit` e `/uom-conversions`, total 24; 5 erros IC-ERR-110/111/112/120/121). Alinhamento de códigos de erro: a faixa de conversão/reposição foi movida para 110–121 para não colidir com IC-ERR-090/091 (estado/motivo) já usados em 07/13 — ajuste refletido também no MMS-002-02. **Pacote MMS-002 100% aderente a ADR-013/014.** | MMS-002-07, MMS-002-13 |
| 2026-08-08 | **Conclusão estrutural da v1.2 do MMS-002 (docs detalhados):** **MMS-002-04 Domain Model → 1.1.0** (nova entidade `AlternativeUnit`, unidade base explícita, VOs `ConversionFactor`/`ReplenishmentPolicy`/`SupplyRoute`, invariantes INV-IC-12..14, `UomConversionService`) e **MMS-002-11 Database → 1.1.0** (tabela `item_alternative_unit` com fator > 0 e unicidade de UoM/papel; colunas `replenishment_policy`/`supply_route`/`fixed_lot_quantity` com check constraints; `unit_of_measure_id` como base; nenhuma coluna de saldo). Restam MMS-002-07 (Use Cases) e MMS-002-13 (API) para o endpoint de conversão e captura em unidade de compra — conclusão final da v1.2 | MMS-002-04, MMS-002-11 |
| 2026-08-08 | **Revisão MMS-002 → 1.2.0 (propagação de ADR-013/014):** README (MMS-002) e Business Rules (MMS-002-02) elevados a 1.2.0. Item Catalog passa a definir unidade de estoque (base) + unidades alternativas com fator de conversão e papel (quantidades persistidas na base), e os parâmetros da regra de reposição (política de quantidade sugerida + rota de suprimento). Business Rules ganham as famílias IC-BR-090..093 (conversão de UoM) e IC-BR-100..101 (parâmetros da regra), matriz 32→38/38. Documentos detalhados do pacote (MMS-002-04 Domain Model, MMS-002-07 Use Cases, MMS-002-11 Database, MMS-002-13 API) permanecem em 1.0.0 — sua revisão para refletir a conversão de UoM é a conclusão pendente da v1.2 | MMS-002, MMS-002-02 |
| 2026-08-08 | **ADR-014 aceita pelo owner** (Proposed → Accepted): resolvidas as duas pendências de aceite — o motor de **sugestão revisável** de reposição entra na **v1.1 do MMS-004** e a **execução automática** permanece na **v2.0** (opt-in), sem alterar o MVP; catálogo inicial de políticas de quantidade com três opções (repor até o máximo; múltiplo de embalagem via fator de UoM do ADR-013; lote econômico fixo). Decisão aberta correspondente no §10 marcada como resolvida; roadmap do MMS-004 a refletir na revisão de módulo | ADR-014 |
| 2026-08-08 | ADR-013 e ADR-014 criadas a partir dos candidatos P1 do ARC-007. **ADR-013 — Conversão de UoM (Accepted):** toda quantidade de materiais é persistida na Unidade de Estoque (base); o Item Catalog passa a definir unidades alternativas com fator de conversão (ex.: 1 CX = 12 UN); conversão nas fronteiras (Receiving/movimentação/compra), núcleo só na base; fator imutável para movimentos históricos; arredondamento parametrizável; impacta MMS-002 (alvo 1.2.0), MMS-004, MMS-005, PR-001, MMS-002-11 em passe de revisão dedicado. **ADR-014 — Motor de Regras de Reposição (Proposed):** regra configurável (gatilho por ponto de pedido, quantidade sugerida, rota estoque/transferência/compra) produzindo sugestão revisável (execução automática opt-in na v2.0), sempre respeitando as fronteiras (MMS não compra; nenhuma movimentação sem documento) — registrada como Proposed, aguardando aceite do owner (nova decisão aberta no §10) | ADR-013, ADR-014 |
| 2026-08-08 | ARC-007 — Domain Benchmark: Odoo & ERPNext criado e aprovado como anexo **informativo** (versão 1.0.0): comparação do modelo de domínio de suprimentos dos ERPs Odoo (LGPLv3) e ERPNext (GPL-3.0) com o Trino (MMS-001/002/004, PR-001); conclui que nenhum "completa" a arquitetura (stack/licença incompatíveis) e que o resultado é majoritariamente de **validação** — o modelo de saldo derivado de movimentação (ADR-009/MMS-P-08) coincide com o padrão de mercado; matriz de 25 conceitos por status de cobertura, candidatos a refinamento priorizados (P1: conversão de UoM e motor de regras de reposição; P2: FEFO/FIFO+putaway, landed costs; P3: supplier scorecard), pontos em que o Trino está à frente (segregação 3PL, imutabilidade+estorno, deny-by-default/SoD, fronteiras de contexto) e aviso de propriedade intelectual (proibido copiar código/texto). Documento não normativo — mudanças adotadas seguem revisão de módulo + ADR | ARC-007 |
| 2026-08-08 | Placeholders estruturais criados (Draft) para completar a taxonomia oficial de `docs/` do TPES-002: **01-vision** (VIS-000), **02-product** (PRD-000), **09-testing** (QA-000), **10-devops** (OPS-000), **11-release** (REL-000), **12-user-guides** (DOC-000) — pastas registradas com índice/escopo previsto e regra de promoção via registry | GOV-002 |
