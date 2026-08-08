**Documento:** PRC-001 — Procurement Suite (Documento Mestre Funcional)
**Módulo:** PRC — Procurement (Suprimentos / Compras)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** FD-001 (Foundation), PR-001 (Purchase Requisition — módulo existente), MMS-001 (Materials — fronteira de demanda), TPES-002, ARC-002 (Bounded Contexts)
**Referências:** ADR-011, ADR-012, GOV-001, GOV-002

> Documento mestre da **suíte de Procurement**. Define visão, módulos, fluxo corporativo **procure-to-pay**, fronteiras e roadmap. É a fonte oficial de verdade para todos os documentos dos módulos de compras. Nenhum documento de módulo da suíte pode contradizê-lo. Equivale, para Compras, ao que o MMS-001 é para Materiais.

---

# 1. Visão Geral

A **Procurement Suite** é o domínio do Trino Supply responsável por **transformar uma necessidade aprovada em compra executada e recebida**, com governança, cotação competitiva e rastreabilidade ponta a ponta.

Responde à pergunta central de Compras: **"o que precisa ser comprado, de quem, por qual preço e sob qual contrato?"** — sempre partindo de uma **demanda** (o usuário solicita uma necessidade, nunca um fornecedor — princípio PR-001-12).

## 1.1 Objetivos
1. Comprar apenas o necessário, no melhor custo, com competição (RFQ/equalização).
2. Governar o gasto: aprovação, alçadas, segregação de funções, auditoria.
3. Rastrear a compra da demanda ao recebimento (procure-to-pay).
4. Profissionalizar a relação com fornecedores (homologação, avaliação, contratos).

## 1.2 Problemas Resolvidos
| Problema | Como a suíte resolve |
|----------|---------------------|
| Compra sem cotação/competição | RFQ com múltiplos fornecedores + equalização |
| Preço/fornecedor escolhido "no feeling" | Equalização objetiva + política de compras |
| Fornecedor não homologado | Supplier Management (cadastro, homologação, avaliação) |
| Compra fora de contrato | Contract Management (vigências, obrigações, alertas) |
| Falta de rastro demanda→pedido→recebimento | Rastreabilidade bidirecional em toda a cadeia |

---

# 2. Estrutura da Suíte

| Módulo | ID | Papel | Estado |
|--------|-----|-------|--------|
| **Purchase Requisition** | PR-001 | Solicitação de compra + workflow (demanda) | ✅ Documentado 17/17 |
| **RFQ (Cotação)** | PRC-002 (planejado) | Solicitação de cotação a fornecedores | 🗓️ Roadmap |
| **Equalização** | PRC-003 (planejado) | Comparação objetiva de propostas + decisão | 🗓️ Roadmap |
| **Purchase Order (Pedido de Compra)** | PRC-004 (planejado) | Emissão e acompanhamento do pedido | 🗓️ Roadmap |
| **Supplier Management** | PRC-005 (planejado) | Cadastro, homologação, avaliação, documentação | 🗓️ Roadmap |
| **Contract Management** | PRC-006 (planejado) | Contratos, vigências, obrigações, alertas | 🗓️ Roadmap |

> A numeração PRC-002..006 é reservada por este documento; cada módulo será documentado no padrão de 17 artefatos (como PR-001 e a suíte MMS).

---

# 3. Fluxo Corporativo (procure-to-pay)

```
Necessidade
   ↓
Solicitação de Compra (PR-001) ── aprovação/alçada (FD-001-04)
   ↓
RFQ / Cotação (PRC-002) ──► Fornecedores homologados (PRC-005)
   ↓
Equalização (PRC-003) ── decisão com política de compras
   ↓
Pedido de Compra (PRC-004) ──► sob Contrato (PRC-006), quando aplicável
   ↓
Recebimento (MMS-005) ──► Entrada no estoque (MMS-004)
   ↓
(Pagamento — fora da suíte no MVP)
```

Regras do fluxo:
1. **Toda compra parte de uma demanda aprovada** (PR-001 ou reposição do MMS-004 — ADR-014).
2. **RFQ usa apenas fornecedores homologados** (PRC-005).
3. **Equalização é objetiva e auditável**; a decisão registra critério e responsável (SoD).
4. **Pedido referencia a demanda de origem** (rastreabilidade bidirecional) e, quando houver, o **contrato** vigente.
5. **Recebimento é do domínio Materials** (MMS-005); a suíte de Compras **produz o pedido** e **consome o status de recebimento**.

---

# 4. Fronteiras (invioláveis)
- **Procurement não controla estoque nem recebe fisicamente** — isso é da suíte Materials (MMS-004/005). A fronteira Materials↔Procurement é a **demanda** (Materials→Procurement) e o **recebimento** (Materials consome o pedido).
- **Procurement não reimplementa Foundation** — workflow, notificações, auditoria, timeline, documentos, identidade, organização, master data e configuração vêm de FD-001-01..10 (ADR-011).
- **Pagamento/financeiro** está fora do MVP da suíte.
- **Analytics** consome eventos; não é produzido aqui.

---

# 5. Integração com os demais domínios
| Integração | Direção | Comportamento |
|------------|---------|---------------|
| MMS (Materials) | Materials → Procurement | Demanda de compra (item sem estoque) com referência de origem |
| MMS-005 Receiving | Procurement → Materials | Pedido é documento de origem do recebimento; status retorna à demanda |
| Foundation | Consumo | Workflow (alçadas), notificações, auditoria, timeline, documentos, master data |
| Analytics (futuro) | Procurement → Analytics | Eventos de RFQ/pedido/contrato para KPIs (saving, lead time, aderência a contrato) |

---

# 6. Regras Gerais da Suíte (PRC-RG)
| Código | Regra |
|--------|-------|
| PRC-RG-01 | Toda compra origina-se de demanda aprovada, com rastreabilidade bidirecional |
| PRC-RG-02 | RFQ apenas com fornecedores homologados e ativos (PRC-005) |
| PRC-RG-03 | Equalização registra critério, comparativo e decisor; SoD (quem cota não decide sozinho quando parametrizado) |
| PRC-RG-04 | Pedido respeita alçadas (FD-001-04) e, quando houver, o contrato vigente (PRC-006) |
| PRC-RG-05 | Toda operação é auditada (FD-001-06) e aparece na timeline (FD-001-07) |
| PRC-RG-06 | Comportamentos variáveis são parâmetros (`procurement.*`, FD-001-10), nunca código |
| PRC-RG-07 | Multiempresa e escopo organizacional obrigatórios (MMS-001 §6.2 análogo) |

---

# 7. KPIs da Suíte
Saving de compra (RFQ), lead time de compra (demanda→recebimento), aderência a contrato, taxa de competição (nº de propostas por RFQ), desempenho de fornecedor (divergências de recebimento — via MMS-005), ciclo de aprovação.

---

# 8. Roadmap
| Onda | Escopo |
|------|--------|
| **Atual** | PR-001 (Solicitação de Compra) documentado e pronto para implementação |
| **Próxima** | PRC-005 Supplier Management (base cadastral) → PRC-002 RFQ → PRC-003 Equalização → PRC-004 Purchase Order |
| **Seguinte** | PRC-006 Contract Management |
| **Futuro** | Integração fiscal/financeira (pagamento), portal do fornecedor, scorecard automático |

> Ordem sugerida coloca **Supplier Management** primeiro (base cadastral para RFQ/PO/Contratos), analogamente ao Item Catalog ser base da suíte Materials. A ordem final por release é decisão de produto (PRD-001), não de arquitetura.

---

# 9. Critérios de Conclusão (DoD da suíte)
1. Este documento aprovado no registry (GOV-002).
2. Cada módulo PRC-00x documentado no padrão de 17 artefatos, sem contradizer este mestre.
3. Fluxo procure-to-pay operável com as integrações Materials/Foundation.
4. Rastreabilidade bidirecional demanda↔pedido↔recebimento verificável na timeline.

---

# 10. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Criação do Documento Mestre Funcional da Procurement Suite: visão, estrutura (PR-001 + RFQ/Equalização/Purchase Order/Supplier Management/Contract Management), fluxo corporativo procure-to-pay, fronteiras com Materials/Foundation, regras gerais PRC-RG, KPIs, roadmap e DoD — enquadra a próxima onda de Compras, no padrão do MMS-001. |
