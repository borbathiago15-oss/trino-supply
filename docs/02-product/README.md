**Documento:** PRD-001 — Engenharia de Produto
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Criticidade:** 🔴 Core
**Escopo:** Plataforma Trino Supply

> Documentos relacionados: VIS-001 (01-vision), TPES-002, MMS-001, PR-001, FD-001, ARC-001.

# PRD-001 — Engenharia de Produto

## 1. Objetivo
Definir como o produto é planejado e priorizado: capacidades, roadmap, priorização e métricas — conectando a visão (VIS-001) aos módulos documentados.

## 2. Capacidades do Produto (por domínio / Bounded Context — ARC-002)

| # | Capacidade | Bounded Context | Documentação |
|---|-----------|-----------------|--------------|
| 1 | Plataforma base (IAM, Org, Workflow, Notif, Audit, Timeline, Docs, Master Data, Config) | Foundation | FD-001 (10/10) |
| 2 | Solicitação de Compra + Workflow | Procurement | PR-001 (17/17) |
| 3 | Catálogo de Itens | Materials | MMS-002 (17/17) |
| 4 | Solicitação de Material | Materials | MMS-003 (17/17) |
| 5 | Estoque (Inventory) | Materials | MMS-004 (17/17) |
| 6 | Recebimento | Materials | MMS-005 (17/17) |
| 7 | RFQ / Equalização / Pedido de Compra | Procurement | Roadmap |
| 8 | Supplier Management | Supplier | Roadmap |
| 9 | Contract Management | Contract | Roadmap |
| 10 | Analytics (dashboards/KPIs) | Analytics | Roadmap |

## 3. Roadmap de Produto (ondas)

| Onda | Escopo | Estado |
|------|--------|--------|
| **MVP** | Foundation + Procurement (PR-001) + Materials (MMS-002/003/004/005) | Documentação completa; implementação a iniciar |
| **v1.1** | RFQ, refinamentos de reposição (fila de sugestões), relatórios | Roadmap |
| **v2.0** | Supplier Management, Contracts, reposição automática, lote/série, mobile | Roadmap |
| **Futuro** | Analytics avançado, integração ERP, IA (fora do MVP — TPES-002) | Roadmap |

> A ordem oficial de módulos segue ADR-012; Foundation antes das APIs (ADR-011).

## 4. Priorização
Critérios: (a) dependência técnica (Foundation habilita tudo); (b) valor de negócio (estoque-primeiro reduz custo); (c) risco/integridade (módulos transacionais críticos primeiro). Resultado: **Foundation → Item Catalog → Inventory → Material Requisition → Receiving → Procurement avançado → Supplier/Contract → Analytics**.

## 5. Métricas de Produto (North Star + drivers)
- **North Star:** taxa de atendimento pelo estoque.
- **Drivers:** acuracidade de estoque (≥ 98%), ciclo de aprovação, tempo de atendimento, ruptura, adoção por papel, redução de compras emergenciais.

## 6. Definição de Pronto (produto)
Uma capacidade está pronta quando: pacote de documentação 17/17 aprovado (DoD do módulo) → implementação com testes (QA-001) verdes → observabilidade e segurança no lugar (OPS-001/SEC-003).

## 7. Governança de Produto
Toda mudança de escopo/capacidade é registrada no GOV-002; mudanças de arquitetura exigem ADR; nada é "oficial" fora do registry.

## 8. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Engenharia de Produto: capacidades por Bounded Context com estado de documentação, roadmap por ondas (MVP/v1.1/v2.0/futuro), critérios de priorização, métricas de produto (North Star + drivers), DoD de produto e governança — conecta VIS-001 aos módulos e ao registry. |
