# PO-001 — Pedido de Compra (MVP)

| Campo | Valor |
|---|---|
| Código | PO-001 |
| Módulo | Procurement — Purchase Order |
| Versão | 0.1.0 (MVP consolidado) |
| Status | 🟡 MVP — pacote completo (01–17) pendente de refinamento |
| Depende de | PR-001, SUP-001, MMS-002, MMS-004, MMS-005 |

> Spec MVP consolidada em documento único, seguindo a regra do projeto de
> documentar antes de implementar. O desdobramento no pacote padrão de 17
> documentos e o módulo de cotação (RFQ) ficam registrados como pendências
> no GOV-002 §10.

## 1. Escopo do MVP

Fecha o ciclo de suprimentos: o comprador transforma demanda aprovada em
pedido a um fornecedor e registra a entrega, que **gera entrada de estoque
automaticamente** (MMS-005) para itens do catálogo.

Origem da demanda (painel "Demandas de compra"):
1. **Requisições de compra aprovadas** (PR-001) sem pedido vinculado — conversão 1-clique copiando os itens.
2. **Itens de solicitação de material em rota de compra** (MMS-003) — visíveis ao comprador para composição manual de pedido.

Fora do MVP: cotação (RFQ), recebimento parcial, aprovação do pedido em
alçada própria, anexos.

## 2. Máquina de estados

```
EMITIDO ──receber──▶ RECEBIDO   (gera entradas de estoque p/ itens de catálogo)
   │
   └──cancelar──▶ CANCELADO     (motivo obrigatório)
```

- **PO-BR-001** — Pedido nasce `EMITIDO`, numerado `PO-aaaa-nnnnnn` (sequência `procurement.po_number_seq`).
- **PO-BR-002** — Fornecedor deve existir e estar ativo na emissão (SUP-BR-002).
- **PO-BR-003** — Pedido criado de uma requisição exige requisição `APPROVED` e sem outro pedido não-cancelado vinculado; os itens são copiados (snapshot).
- **PO-BR-004** — Receber e cancelar são exclusivos do estado `EMITIDO`; documentos são imutáveis após estado final.
- **PO-BR-005** — Recebimento exige local de estoque; cada item vinculado ao catálogo gera movimento de **ENTRADA** origem `RECEBIMENTO` com referência ao número do pedido (INV-IV-01 preservada: saldo só muda via movimentação).
- **PO-BR-006** — Itens sem vínculo de catálogo (descrição livre da requisição) são recebidos sem efeito de estoque.
- **PO-BR-007** — Papéis: emite/recebe/cancela — Comprador, Gestor de Suprimentos, Administrador; consulta inclui Auditor.

## 3. Catálogo de erros

| Código | HTTP | Situação |
|---|---|---|
| PO-ERR-010 | 400 | Quantidade ≤ 0 ou preço < 0 |
| PO-ERR-020 | 400 | Fornecedor inexistente ou inativo |
| PO-ERR-021 | 422 | Requisição de origem não está aprovada |
| PO-ERR-022 | 409 | Requisição já possui pedido vinculado |
| PO-ERR-030 | 400 | Pedido sem itens |
| PO-ERR-040 | 409 | Transição fora do estado `EMITIDO` |
| PO-ERR-041 | 400 | Cancelamento sem motivo |
| PO-ERR-404 | 404 | Pedido inexistente |
| PO-ERR-900 | 403 | Papel sem acesso |

Erros do estoque no recebimento (ex.: `IV-ERR-060` local inválido) são
propagados com seus próprios códigos.

## 4. API (resumo)

| Método | Rota | Papéis |
|---|---|---|
| GET `/api/v1/purchase-orders` | lista pedidos | comprador+ e Auditor |
| GET `/api/v1/purchase-orders/demands` | demandas: PRs aprovadas sem pedido + itens MR em rota de compra | comprador+ |
| POST `/api/v1/purchase-orders` | emite pedido (manual ou `sourcePrId` p/ converter requisição) | comprador+ |
| POST `/api/v1/purchase-orders/{id}/receive` | registra entrega (`locationId`) e gera entradas | comprador+ |
| POST `/api/v1/purchase-orders/{id}/cancel` | cancela com motivo | comprador+ |

"comprador+" = PurchasingOfficer, SupplyManager, SystemAdministrator.
Envelope padrão `{data, correlationId}` / `{error:{...}}`.
