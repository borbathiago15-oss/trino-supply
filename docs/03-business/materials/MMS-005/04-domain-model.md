**Documento:** MMS-005-04 — Domain Model
**Módulo:** MMS-005 — Receiving (Recebimento)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-005-02 (Business Rules), MMS-005-03 (State Machine), MMS-002, MMS-004, PR-001, FD-001-04
**Referências:** MMS-004-04 / MMS-003-04 (padrão), ADR-009, ADR-013, GOV-001

# 1. Objetivo
Representar os conceitos do Receiving. O módulo **não persiste saldo** (RC-BR-020): a conferência concluída dispara a entrada no MMS-004.

# 2. Aggregate Root
**Receiving** — documento de recebimento (cabeçalho) com linhas de conferência e divergências; ciclo de vida ST-RC (MMS-005-03).

# 3. Aggregate Diagram
```text
┌───────────────────────────────────────────────────────────┐
│                 AGGREGATE: Receiving (AR)                 │
│  Id · CompanyId · OriginType · OriginReference ·          │
│  Status · WarehouseRef · CreatedBy/At · CompletedBy/At ·  │
│  StockEntryMovementId? · Version                          │
│  ├── 1..N ReceivingLine  (Entity)                         │
│  │       ItemRef · SizeCode? · ExpectedQty · ReceivedQty ·│
│  │       CapturedUom? · AppliedFactor? · BaseReceivedQty  │
│  └── 0..N Divergence     (Entity)                         │
│          Type(Falta|Excesso|Avaria) · Qty · Destination · │
│          ApprovalStatus?                                  │
│  Value Objects: DocumentOrigin · Quantity · DivergenceType│
└───────────────────────────────────────────────────────────┘
Refs externas: Item (MMS-002) · PurchaseOrder (PR-001) · Transfer (MMS-004) ·
Requisition (MMS-003) · StockMovement (MMS-004) · WorkflowInstance (FD-001-04)
```

# 4. Relacionamentos UML
```text
Company 1 ── N Receiving
Receiving 1 ── N ReceivingLine
Receiving 1 ── N Divergence
Receiving 1 ── 0..1 StockMovement (MMS-004, entrada gerada)
ReceivingLine N ── 1 Item (MMS-002)
```

# 5. Entidades
- **Receiving (AR):** OriginType/Reference (pedido/transferência/devolução — RC-BR-001), Status, WarehouseRef, StockEntryMovementId (após conclusão), Version.
- **ReceivingLine:** ItemRef; SizeCode?; ExpectedQty; ReceivedQty (informada); CapturedUom?/AppliedFactor? (conversão — RC-BR-010/ADR-013); BaseReceivedQty (na unidade base).
- **Divergence:** Type (falta/excesso/avaria), Qty, Destination (destino documentado — RC-BR-011), ApprovalStatus? (quando exige aprovação — RC-BR-040).

# 6. Value Objects
- **DocumentOrigin** (OriginType+Reference, imutável — RC-BR-001).
- **Quantity** (NUMERIC(18,4) ≥ 0; conversão para base via fator — RC-BR-010).
- **DivergenceType** (falta/excesso/avaria; conjunto fechado).

# 7. Enumerações
Receiving Status: Aguardando · Em Conferência · Concluído · Concluído com Divergência · Cancelado (ST-RC-001..005).
Divergence Type: Falta · Excesso · Avaria.

# 8. Invariantes
| Código | Invariante | Origem |
|--------|-----------|--------|
| INV-RC-01 | Recebimento sempre com documento de origem | RC-BR-001 |
| INV-RC-02 | ReceivedQty ≥ 0; BaseReceivedQty = ReceivedQty × fator (se capturado em UoM alternativa) | RC-BR-010 |
| INV-RC-03 | Toda divergência tem tipo e destino | RC-BR-011 |
| INV-RC-04 | Conclusão gera exatamente um StockMovement de entrada | RC-BR-020 |
| INV-RC-05 | Documento nunca altera saldo diretamente | RC-BR-020 (MMS-P-08) |
| INV-RC-06 | Transições só pela State Machine | MMS-005-03 |
| INV-RC-07 | `version` incrementa a cada alteração | RC-BR-030 |

# 9. Comportamentos
Receiving: Create(origin) → Aguardando · StartInspection() → Em Conferência · RegisterLine(item, receivedQty, uom?) — converte para base · RegisterDivergence(type, qty, destination) · Complete() — gera entrada (MMS-004) e, se compra dedicada, entra reservada (RC-BR-021) · Cancel().

Factory: `Receiving.CreateFromOrigin(company, originType, originRef, warehouse, expectedLines)`.

# 10. Repositories
`IReceivingRepository`: GetById, GetByOrigin(originType, originRef), FindPending(companyId), Add, Update, Search(spec, keyset). Filtro company_id; version; sem escrita de saldo.

# 11. Domain Services
- `UomConversionApplier` (reuso conceitual do MMS-004) — converte ReceivedQty para base.
- `StockEntryGateway` — dispara o documento de entrada no MMS-004 na conclusão (RC-BR-020); compra dedicada reservada (RC-BR-021).
- `DivergenceApprovalCoordinator` — workflow de aprovação de destino (RC-BR-040).

# 12. Policies
POL-RC-01 Timeline/Auditoria (RC-BR-050) · POL-RC-02 Conclusão → entrada MMS-004 · POL-RC-03 Compra dedicada → atendimento retomado (MMS-003) · POL-RC-04 Pedido confirmado (PR-001) → cria Receiving Aguardando.

# 13. Domain Events
Recebimento registrado · Em conferência · Divergência registrada · Recebimento concluído · Concluído com divergência · Entrada no estoque gerada · Atendimento retomado · Recebimento cancelado.

# 14. Limites do Aggregate
Fazem parte: Receiving + ReceivingLine + Divergence. Não fazem parte: saldo/movimentação (MMS-004), item/unidade (MMS-002), pedido (PR-001), solicitação (MMS-003), workflow (FD-001-04).

# 15. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Domain Model do Receiving: Aggregate Root Receiving (ReceivingLine + Divergence), Value Objects (DocumentOrigin, Quantity, DivergenceType), 7 invariantes INV-RC (incl. conversão de UoM e entrada única no MMS-004), factory, repositories sem escrita de saldo, 3 domain services (conversão, entrada no estoque, aprovação de divergência), 4 policies e eventos — padrão MMS-004-04/MMS-003-04. |
