**Documento:** MMS-003-04 — Domain Model
**Módulo:** MMS-003 — Material Requisition (Solicitação de Material)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-003 (Visão v1.1.0), MMS-003-02 (Business Rules v1.0.0), MMS-003-03 (State Machine v1.0.0), MMS-001 (Documento Mestre), MMS-002 (Item Catalog), MMS-004 (Inventory), FD-001-03/09/10
**Referências:** MMS-004-04 / MMS-002-04 (padrão de formato), PR-001, MMS-005, ADR-009 (domínio não conhece o banco), ADR-013 (unidade base), GOV-001

---

# 1. Objetivo

Representar os conceitos de negócio do Material Requisition, seus relacionamentos, responsabilidades e regras de consistência. O modelo é independente de banco, API e UI (ADR-009).

**Decisão estrutural:** a solicitação é a **fonte da demanda**, não da movimentação — não mantém saldo (MR-BR-050). O atendimento (reserva/separação/entrega) e a compra vivem em outros contextos (MMS-004, PR-001); a solicitação **acompanha** o status por eventos consumidos (MR-BR-051).

---

# 2. Aggregate Roots do Módulo

| Aggregate Root | Responsabilidade central |
|----------------|--------------------------|
| **MaterialRequisition** | Cabeçalho da solicitação + itens; ciclo de vida (MMS-003-03), submissão, decisão de aprovação, roteamento e conclusão |
| **DeliveryLocation** | Cadastro de locais de entrega por empresa (MR-BR-060) |

Nenhuma entidade interna é alterada sem passar pela raiz. A solicitação nunca escreve saldo; interage com MMS-004/PR-001 por contratos/eventos.

---

# 3. Aggregate Diagram

```text
┌──────────────────────────────────────────────────────────────────┐
│              AGGREGATE: MaterialRequisition (AR)                 │
│                                                                   │
│  Id · CompanyId · RequesterId · CostCenterRef ·                   │
│  DeliveryLocationRef · Justification · ReasonRef? ·               │
│  NeededDate · Status · Approval(Decision) · Version               │
│                                                                   │
│  ├── 1..N  RequisitionItem       (Entity)                         │
│  │         ItemRef · SizeCode? · RequestedQty · ApprovedQty? ·    │
│  │         ItemStatus · Route(Stock|Purchase|None) ·              │
│  │         ReservationRef? · PurchaseRef? · DecisionReason?       │
│  │                                                                │
│  └── 0..N  RequisitionAttachment (Entity)                         │
│            DocumentId (FD-001-03) · Kind                          │
│                                                                   │
│  Value Objects: Justification · MasterDataReference (Reason,      │
│  SizeCode) · Quantity · CostCenterRef · DeliveryLocationRef ·     │
│  ApprovalDecision                                                 │
└──────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────┐
│               AGGREGATE: DeliveryLocation (AR)                    │
│  Id · CompanyId · Code(gerado) · Description · OrgUnitRef ·        │
│  Active · Version                                                 │
└──────────────────────────────────────────────────────────────────┘

Referências externas (por Id / referência lógica):
Item (MMS-002) · Company/Unit/CostCenter (FD-001-02) · Reason/SizeGrid
(FD-001-09) · Attachment (FD-001-03) · WorkflowInstance (FD-001-04) ·
Reservation (MMS-004) · PurchaseRequisition (PR-001)
```

---

# 4. Relacionamentos UML

```text
Company (FD-001-02)     1 ── N   MaterialRequisition / DeliveryLocation
MaterialRequisition     1 ── N   RequisitionItem
MaterialRequisition     1 ── N   RequisitionAttachment
MaterialRequisition     N ── 1   DeliveryLocation   (ref lógica)
RequisitionItem         N ── 1   Item (MMS-002)     (ItemRef)
RequisitionItem         1 ── 0..1 Reservation (MMS-004)         (por ReservationRef)
RequisitionItem         1 ── 0..1 PurchaseRequisition (PR-001)  (por PurchaseRef — rastreab. bidirecional)
MaterialRequisition     1 ── 0..1 WorkflowInstance (FD-001-04)
```

Cardinalidades de negócio:
- `RequisitionItem`: 1..N; cada item aprovado recebe **rota explícita** (MR-BR-042);
- `RequisitionAttachment`: 0..N; obrigatório quando o motivo o exige (MR-BR-013);
- `DeliveryLocation`: ref obrigatória e ativa (MR-BR-006).

---

# 5. Entidades

## MaterialRequisition (Aggregate Root)
Cabeçalho da demanda. Atributos: Id; CompanyId (MR-BR-001); RequesterId (MR-BR-002); CostCenterRef (MR-BR-005); DeliveryLocationRef (MR-BR-006); Justification (MR-BR-003); ReasonRef? (MR-BR-004); NeededDate; Status (MMS-003-03); ApprovalDecision (parecer/decisor); Version.

## RequisitionItem
Linha da solicitação. Atributos: Id; ItemRef (item Ativo — MR-BR-010); SizeCode? (obrigatório com grade — MR-BR-012); RequestedQty (> 0, unidade base — MR-BR-011); ApprovedQty? (aprovação parcial — MR-BR-031); ItemStatus (pendente/reservado/separado/entregue/em compra/recebido/rejeitado); Route (Stock | Purchase | None); ReservationRef? (MMS-004); PurchaseRef? (PR-001); DecisionReason? (motivo do decisor quando reduzido/rejeitado).

## RequisitionAttachment
Referência a documento no FD-001-03. Atributos: Id; DocumentId; Kind. Regra: exigido por motivo configurado (MR-BR-013); binário no MinIO, nunca no aggregate.

## DeliveryLocation (Aggregate Root)
Local de entrega da empresa. Atributos: Id; CompanyId; Code (**gerado pelo sistema**, único por empresa); Description; OrgUnitRef (FD-001-02); Active; Version. Inativação exige que nenhuma solicitação em aberto o referencie (MR-BR-060).

---

# 6. Value Objects

- **Justification** — texto (min/máx parametrizável — MR-BR-003); imutável.
- **MasterDataReference** — `typeCode+code` para Reason (MR-BR-004) e SizeCode (MR-BR-012); vigente na data de referência (FD-001-09).
- **Quantity** — NUMERIC(18,4) > 0, na **unidade base** do item (MR-BR-011 / ADR-013); a conversão ocorre no estoque/compra, não aqui.
- **CostCenterRef** / **DeliveryLocationRef** — referências lógicas validadas no domínio (MR-BR-005/006).
- **ApprovalDecision** — decisor + data/hora + parecer + ajustes por item (MR-BR-031); imutável após registro.

---

# 7. Enumerações

## Requisition Status (MMS-003-03)
Rascunho · Submetida · Em Aprovação · Aprovada · Em Atendimento · Aguardando Compra · Concluída · Rejeitada · Cancelada

## Item Route
Stock · Purchase · None (ainda não roteado / rejeitado)

## Item Status
Pendente · Reservado · Separado · Entregue · Em Compra · Recebido · Rejeitado

---

# 8. Invariantes

| Código | Invariante | Origem |
|--------|-----------|--------|
| INV-MR-01 | Solicitação sempre com empresa, solicitante, centro de custo e local de entrega | MR-BR-001/002/005/006 |
| INV-MR-02 | ≥ 1 item; item Ativo; quantidade > 0 na unidade base | MR-BR-010/011 |
| INV-MR-03 | Item com grade exige SizeCode válido | MR-BR-012 |
| INV-MR-04 | Motivo/anexo conforme parametrização | MR-BR-004/013 |
| INV-MR-05 | Estado inicial Rascunho; transições só pela State Machine | MR-BR-020 / MMS-003-03 |
| INV-MR-06 | Aprovador ≠ solicitante | MR-BR-032 |
| INV-MR-07 | Validação de estoque só após Aprovada | MR-BR-040 |
| INV-MR-08 | Todo item aprovado tem rota explícita (Stock/Purchase) | MR-BR-042 |
| INV-MR-09 | Item na rota de compra tem PurchaseRef com referência de origem | MR-BR-043 |
| INV-MR-10 | Conclusão só com todos os itens em estado terminal de atendimento | MR-BR-052 |
| INV-MR-11 | O aggregate nunca escreve saldo | MR-BR-050 |
| INV-MR-12 | `version` incrementa a cada alteração persistida | MR-BR-091 |
| INV-MR-13 | DeliveryLocation.Code é gerado pelo sistema e único por empresa | MR-BR-060 |

---

# 9. Comportamentos

## MaterialRequisition
Create() — Rascunho (INV-MR-01/05)
AddItem() / UpdateItem() / RemoveItem() — somente em Rascunho
AddAttachment() / RemoveAttachment()
Submit() — guards de submissão (MR-BR-021)
Approve(decision) — total/parcial (MR-BR-031); SoD (INV-MR-06)
Reject(reason) / ReturnToRequester()
ApplyStockValidationResult(perItem) — após Aprovada (MR-BR-040/041); define Route
RouteItems() — Stock→reserva (MMS-004); Purchase→demanda (PR-001, INV-MR-09)
RegisterFulfilmentEvent(itemRef, event) — consumido de MMS-004/PR-001/MMS-005 (MR-BR-051)
ConfirmReceipt() — conclui quando todos os itens concluem (MR-BR-052)
Cancel(reason?) — estados permitidos; libera reservas (MR-BR-022)

## DeliveryLocation
Create() / Update() / Inactivate() — código gerado; inativação exige zero uso em aberto (MR-BR-060)

## Factory Methods

| Factory | Garantias |
|---------|-----------|
| `MaterialRequisition.Create(company, requester, costCenter, deliveryLocation, justification, reason?, neededDate, items)` | Valida cadastro/itens (MR-BR-001..013); Rascunho; `version=1` |
| `RequisitionItem.Create(itemRef, sizeCode?, requestedQty)` | Item Ativo (IC-BR-021), quantidade > 0, SizeCode quando grade |
| `DeliveryLocation.Create(company, description, orgUnit)` | Code gerado único por empresa (INV-MR-13) |

---

# 10. Repositories

| Repositório | Operações |
|-------------|-----------|
| `IMaterialRequisitionRepository` | `GetById`, `Search(spec, keyset)`, `GetWarehouseQueue(filters, keyset)`, `Add`, `Update` |
| `IDeliveryLocationRepository` | `GetById`, `GetActive(companyId)`, `Add`, `Update` |
| `RequisitionItem / RequisitionAttachment` | acesso somente via Aggregate |

Regras: filtro `company_id` obrigatório (MR-BR-001); `Update` verifica `version` (MR-BR-091); listas usam keyset; sem `Delete` físico (soft delete de rascunho).

---

# 11. Domain Services

| Serviço | Responsabilidade |
|---------|------------------|
| `StockValidationGateway` | Consulta o MMS-004 (disponível por item×tamanho×local) e retorna atende/não atende (MR-BR-040/041) — nunca reserva aqui |
| `RoutingService` | Define a rota por item e dispara reserva (MMS-004) ou demanda (PR-001) via eventos (MR-BR-042/043) |
| `RequisitionStatusProjector` | Consolida o status dos itens (eventos de MMS-004/PR-001/MMS-005) no cabeçalho (MR-BR-051) |
| `ApprovalCoordinator` | Integra o workflow (FD-001-04); garante SoD (MR-BR-032) e registra a decisão parcial (MR-BR-031) |
| `DeliveryLocationCodeGenerator` | Gera código único por empresa (INV-MR-13) |

---

# 12. Policies

| Política | Gatilho | Ação |
|----------|---------|------|
| POL-MR-01 | Qualquer evento de domínio | Timeline (MR-BR-081) |
| POL-MR-02 | Qualquer alteração do aggregate | Auditoria (MR-BR-080) |
| POL-MR-03 | Solicitação aprovada | Disparar validação de estoque (MR-BR-040) |
| POL-MR-04 | Resultado da validação | Rotear itens; gerar demanda PR-001 para itens sem saldo (MR-BR-042/043) |
| POL-MR-05 | Evento consumido (reserva/entrega/compra/recebimento) | Atualizar status do item e do cabeçalho (MR-BR-051) |
| POL-MR-06 | Solicitação cancelada | Liberar reservas ativas vinculadas (IV-BR-022) |
| POL-MR-07 | Item inativado (MMS-002) | Sinalizar itens de solicitações em Rascunho (MR-BR-010) |

---

# 13. Domain Events Produzidos

Solicitação criada · submetida · em aprovação · aprovada · rejeitada · retornada · cancelada · concluída
Aprovação parcial registrada
Estoque validado para a solicitação · Item roteado · Demanda de compra gerada

(Especificação técnica no MMS-003-05, conforme ADR-010.)

---

# 14. Limites dos Aggregates

Fazem parte: MaterialRequisition + RequisitionItem + RequisitionAttachment; DeliveryLocation.

Não fazem parte: saldo, reserva e movimentação (MMS-004); demanda/compra (PR-001); recebimento (MMS-005); item, unidade, motivo, grade (MMS-002 / FD-001-09); anexo binário (FD-001-03); empresa/unidade/centro de custo (FD-001-02); workflow (FD-001-04).

---

# 15. Dependências

| Dependência | Uso |
|-------------|-----|
| MMS-002 | ItemRef, item ativo, unidade base, grade (MR-BR-010/011/012) |
| MMS-004 | Validação/reserva/entrega (INV-MR-07/08; MR-BR-040..051) |
| PR-001 | Demanda de compra com rastreabilidade (INV-MR-09; MR-BR-043) |
| MMS-005 | Retomada de atendimento de compra (MR-BR-044) |
| FD-001-03/04/09/10 | Anexos, workflow, motivos/grades, parâmetros |
| FD-001-01/02/06/07 | Autorização, escopo, auditoria, timeline |
| MMS-003-03 | Máquina de estados executada pelos comportamentos |

---

# 16. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Criação do Domain Model do Material Requisition: 2 Aggregate Roots (MaterialRequisition com RequisitionItem e RequisitionAttachment; DeliveryLocation), Aggregate Diagram e relacionamentos UML, Value Objects (Justification, MasterDataReference, Quantity na unidade base, CostCenterRef, DeliveryLocationRef, ApprovalDecision), 13 invariantes INV-MR rastreadas às MR-BR, factory methods, repositories (company_id, keyset, sem escrita de saldo), 5 domain services (validação/roteamento/projeção de status/aprovação/gerador de código), 7 policies, eventos produzidos e limites dos aggregates — padrão MMS-004-04, derivado da visão (MMS-003 v1.1.0), das Business Rules (MMS-003-02) e da State Machine (MMS-003-03) |
