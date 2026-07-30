# ADR-012 — Posicionamento da Materials Management Suite no roadmap de módulos

**Status:** 🟢 Accepted
**Data:** 2026-07-30
**Criticidade:** 🔴 Core

---

## Contexto

A visão do Foundation (FD-001) estabeleceu uma ordem de módulos de negócio: **RFQ → Purchase Order → Supplier Management → Contracts → Receiving → Inventory (futuro) → Analytics**. Nessa visão, "Inventory" e "Receiving" eram apenas nomes mapeados, sem documento próprio.

Com a criação do **MMS-001 — Materials Management Suite** (Documento Mestre Funcional, aprovado em 2026-07-30), o domínio de materiais passa a existir formalmente e introduz dois módulos não previstos naquela ordem (**Item Catalog** e **Material Requisition**), além de dar corpo a "Inventory" e reivindicar "Receiving" como parte da suíte. Sem uma decisão registrada, ficariam ambíguos: a ordem oficial de módulos, a propriedade de Receiving e Inventory, e a fronteira entre a suíte MMS e o domínio de Compras (PR-001).

## Decisão

1. **Materials Management é domínio oficial da plataforma**, com documentação em `docs/03-business/materials/`, regido pelo documento mestre **MMS-001**. Nenhum módulo da suíte pode contradizê-lo.
2. **"Inventory (futuro)"** da visão do FD-001 passa a ser **MMS-004 — Inventory Management**.
3. **"Receiving"** da visão do FD-001 passa a ser **MMS-005**, de propriedade da suíte MMS (o recebimento é etapa do ciclo de materiais e gera a entrada no estoque).
4. **Item Catalog (MMS-002)** e **Material Requisition (MMS-003)** são adicionados oficialmente ao roadmap da plataforma como módulos novos.
5. **Purchasing não é módulo da MMS.** Compras permanece no domínio Procurement (PR-001 existente; RFQ, Purchase Order, Supplier Management e Contracts conforme visão do FD-001). A MMS **produz demanda** para esse domínio e **consome resultado** (entrada de material via MMS-005). Nenhuma responsabilidade é duplicada.
6. **Ordem oficial de roadmap de módulos** a partir desta decisão:

```text
Foundation (concluído — ADR-011)
│
├── Procurement:   PR-001 (documentado) → RFQ → Purchase Order → Supplier Management → Contracts
├── Materials:     MMS-001 (documentado) → MMS-002 Item Catalog → MMS-004 Inventory Management
│                  → MMS-003 Material Requisition → MMS-005 Receiving
└── Analytics
```

O intercalamento entre as trilhas Procurement e Materials em cada release é decisão de planejamento de produto, não de arquitetura.

## Consequências

* A documentação dos módulos MMS-002 a MMS-005 fica destravada, devendo seguir o MMS-001 e os padrões Enterprise vigentes (registry, regras codificadas, eventos, roadmap).
* O PR-001 poderá referenciar, em versões futuras, a Material Requisition (MMS-003) como origem de demanda — sem alteração retroativa dos documentos já aprovados.
* A decisão aberta registrada no GOV-002 (seção 10 — "Posicionamento da suíte MMS frente à ordem de módulos do FD-001") é encerrada por esta ADR.
* Qualquer novo módulo fora desta ordem exigirá ADR própria.

## Referências

* MMS-001 — Materials Management Suite (Documento Mestre Funcional)
* FD-001 — Foundation Domain (visão de módulos)
* ADR-011 — Foundation antes das APIs
* PR-001 — Purchase Requisition
