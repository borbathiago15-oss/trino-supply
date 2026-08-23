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
| MMS-002 | Item Catalog | 1.1.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/README.md` |
| MMS-002-01 | Item Catalog — Business Context | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/01-business-context.md` |
| MMS-002-02 | Item Catalog — Business Rules | 1.1.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/02-business-rules.md` |
| MMS-002-03 | Item Catalog — State Machine | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/03-state-machine.md` |
| MMS-002-04 | Item Catalog — Domain Model | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/04-domain-model.md` |
| MMS-002-05 | Item Catalog — Event Storming | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/05-event-storming.md` |
| MMS-002-06 | Item Catalog — BPMN | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/06-bpmn.md` |
| MMS-002-07 | Item Catalog — Use Cases | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/07-use-cases.md` |
| MMS-002-08 | Item Catalog — User Stories | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/08-user-stories.md` |
| MMS-002-09 | Item Catalog — Permissions | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/09-permissions.md` |
| MMS-002-10 | Item Catalog — Notifications | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/10-notifications.md` |
| MMS-002-11 | Item Catalog — Database Model | 1.0.0 | 🟢 Approved | `docs/06-database/materials/MMS-002/11-database.md` |
| MMS-002-12 | Item Catalog — Business Journey | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-002/12-business-journey.md` |
| MMS-002-13 | Item Catalog — API | 1.0.0 | 🟢 Approved | `docs/07-api/materials/MMS-002/README.md` |
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
| MMS-004-10 | Inventory Management — Notifications | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-004/10-notifications.md` |
| MMS-004-11 | Inventory Management — Database Model | 1.0.0 | 🟢 Approved | `docs/06-database/materials/MMS-004/11-database.md` |
| MMS-004-12 | Inventory Management — Business Journey | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-004/12-business-journey.md` |
| MMS-004-13 | Inventory Management — API | 1.0.0 | 🟢 Approved | `docs/07-api/materials/MMS-004/README.md` |
| MMS-004-14 | Inventory Management — UX | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-004/14-ux.md` |
| MMS-004-15 | Inventory Management — Wireframes | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-004/15-wireframes.md` |
| MMS-004-16 | Inventory Management — Acceptance Criteria | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-004/16-acceptance-criteria.md` |
| MMS-004-17 | Inventory Management — Test Scenarios | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-004/17-test-scenarios.md` |
| MMS-005 | Receiving | 1.0.0 | 🟢 Approved | `docs/03-business/materials/MMS-005/README.md` |

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
| Mecanismo de pseudonimização LGPD compatível com cadeia de hash (cofre selado vs. HMAC com chave rotacionada) | FD-001-06, seção 6 | Em análise — exigirá ADR antes da v2 do Audit Service |
| Períodos de orçamento com teto por ciclo (requisições de compra dentro de ciclo orçamentário com limite por período — inspiração validada em análise de ERPs open source, 2026-08-23) | Análise de continuidade (sessão 2026-08-23); PR-001-01 (centro de custo) | Em análise — módulo/extensão fora da ordem oficial de roadmap: exigirá ADR própria (ADR-012, consequência 4) antes de qualquer documento |
| Divergência de numeração das regras IV-BR: o catálogo MMS-004-02 e os documentos MMS-004-07/08/09 (e, por consequência, 10–17) usam esquemas de códigos IV-BR/IV-ERR/TC-IV distintos para as mesmas regras | Detectada na produção do pacote MMS-004 (2026-08-23) | Em análise — exigirá revisão de reconciliação (1.1.0) do MMS-004-02 ou dos MMS-004-07/08/09 para restabelecer a rastreabilidade única |

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
| 2026-08-23 | MMS-004-10 — Notifications do Inventory Management criado e aprovado (versão 1.0.0): 14 eventos notificáveis (parcimônia — movimentações bem-sucedidas não notificam), 13 templates IV-NOT, cadeias de escalonamento (ajuste, ruptura, item inativado, DLQ), especificação operacional (queues, prioridades, retry, template version, localization, scheduling por timers TMR-IV, preferências, NOT-IV-BR-001..013, auditoria de entrega) — padrão MMS-002-10 | MMS-004-10 |
| 2026-08-23 | MMS-004-11 — Database Model do Inventory Management criado e aprovado (versão 1.0.0): DDL PostgreSQL completo no schema `materials` (ADR-009) com 11 tabelas + document_sequence, stock_balance como projeção não editável (available_qty gerado, saldo nunca negativo, escrita somente com documento confirmado — triggers de última linha de defesa e verificação de privilégios como prova de integridade do DoD), numeração sequencial por empresa, idempotência de origem, reserva ativa única por chave, deduplicação de alerta aberto, views (posição, fila do almoxarifado, alertas), keyset pagination com lock por chave de saldo, planos de indexação/performance/migração/backup — padrão MMS-002-11 | MMS-004-11 |
| 2026-08-23 | MMS-004-12 — Business Journey do Inventory Management criado e aprovado (versão 1.0.0): jornada macro do saldo (entrada→reserva→entrega→inventário) e jornadas do Almoxarife, do Supervisor (ajuste com SoD e estorno), do Gestor (alertas e capital), do Solicitante (consumidor indireto) e do Auditor, com pontos de dor, momentos de verdade e KPIs por etapa — padrão MMS-002-12 | MMS-004-12 |
| 2026-08-23 | MMS-004-13 — API do Inventory Management criada e aprovada (versão 1.0.0): convenções (envelope, keyset, idempotência dupla — chave de cliente + origem funcional, autorização com SoD, rate limit com classe M2M), modelos de recursos (StockMovement com saldos por linha, StockBalanceView somente leitura), 31 endpoints `/api/v1/inventory` mapeados a UC/permissão/evento (sem endpoint de escrita de saldo), catálogo de erros IV-ERR → HTTP, segurança, versionamento, NFRs e roadmap — padrão MMS-002-13 (docs/07-api) | MMS-004-13 |
| 2026-08-23 | MMS-004-14 — UX do Inventory Management criado e aprovado (versão 1.0.0): princípios IV-UX-001..008, personas com negação explícita do Requester, telas IV-SCR-01..08 (visão do almoxarifado por vencimento, posição/extrato, documento com resumo de efeito, ajustes com SoD visível, inventário com contagem cega, locais, alertas, configurações), jornadas por papel, WCAG 2.1 AA, responsividade com operação em campo, i18n, DS-001, métricas e critérios de aceite — padrão MMS-002-14 | MMS-004-14 |
| 2026-08-23 | MMS-004-15 — Wireframes do Inventory Management criados e aprovados (versão 1.0.0): WF-IV-01..08 (fila do almoxarifado por vencimento, posição/extrato com saldos antes→depois, documento com diálogo de efeito e estorno, ajustes com SoD visível, inventário com contagem cega e fechamento condicionado, locais, alertas com ruptura priorizada, configurações) com zonas, variantes responsivas, estados, matriz de rastreabilidade e critérios CA-WF-IV — padrão MMS-002-15 | MMS-004-15 |
| 2026-08-23 | MMS-004-16 — Acceptance Criteria do Inventory Management criados e aprovados (versão 1.0.0): 89 critérios AC-IV em Given/When/Then (P0/P1/P2) cobrindo UC-IV-001..011, regras transversais (incluindo a prova de integridade AC-IV-061 — nenhum caminho altera saldo sem documento), ciclo de vida das 4 entidades, eventos com saldos por linha, notificações, API, UX e NFRs, com matriz de rastreabilidade e alinhamento ao DoD — padrão MMS-002-16 | MMS-004-16 |
| 2026-08-23 | MMS-004-17 — Test Scenarios do Inventory Management criados e aprovados (versão 1.0.0): catálogo completo de cenários (62 TC-IV por caso de uso + transversais, ciclo de vida, eventos, notificações, API, UX, wireframes, UI×API, segurança, performance, banco e jobs com relógio controlado), prova de integridade em três frentes (funcional, triggers, privilégios) + reconstrução da projeção, estratégia por camada, matriz de rastreabilidade 100% (89/89 ACs, 16/16 eventos) e alinhamento ao DoD — **fecha o pacote MMS-004 (17/17 documentos), apto a implementação** — padrão MMS-002-17 | MMS-004-17 |
