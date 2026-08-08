**Documento:** MMS-004-04 — Domain Model
**Módulo:** MMS-004 — Inventory Management (Estoque)
**Versão:** 1.1.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-004 (Visão do Módulo v1.1.0), MMS-004-02 (Business Rules v1.1.0), MMS-004-03 (State Machine), MMS-001 (Documento Mestre Funcional), MMS-002 (Item Catalog v1.2.0), ADR-013 (Conversão de UoM), ADR-014 (Motor de Regras de Reposição), FD-001-10
**Referências:** MMS-002-04 (padrão de formato), MMS-003, MMS-005, PR-001, ADR-009 (domínio não conhece o banco), GOV-001

---

# 1. Objetivo

Representar os conceitos de negócio do módulo Inventory Management, seus relacionamentos, responsabilidades e regras de consistência.

O modelo de domínio é independente de banco de dados, APIs e interface de usuário.

**Decisão estrutural central:** saldo **não é um Aggregate editável**. O Saldo é uma **projeção derivada** de documentos confirmados (IV-BR-001), materializada para leitura performática e nunca exposta a comandos de escrita direta — nem por usuário, administrador, API ou job.

---

# 2. Aggregate Roots do Módulo

O módulo possui **seis Aggregate Roots**, um por documento/estrutura com ciclo de vida próprio (MMS-004-03):

| Aggregate Root | Responsabilidade central |
|----------------|--------------------------|
| **StockMovement** | Documento de movimentação: entrada, saída, transferência, liberação de reserva, estorno |
| **Reservation** | Bloqueio de saldo disponível em favor de uma solicitação |
| **Adjustment** | Correção de divergência com justificativa e aprovação |
| **InventoryCount** | Inventário físico: escopo, contagens, divergências e fechamento |
| **Location** | Estrutura almoxarifado → depósito → endereço |
| **ReplenishmentSuggestion** | Sugestão revisável de reposição derivada da regra do item (ADR-014, IV-BR-130/131) |

Nenhuma entidade interna poderá ser alterada diretamente sem passar pelo seu Aggregate Root. Nenhum Aggregate altera saldo diretamente: o efeito sobre o saldo é produzido **somente** pela confirmação de documentos, aplicada pelo Domain Service `StockBalanceService`.

---

# 3. Aggregate Diagram

```text
┌──────────────────────────────────────────────────────────────────┐
│                  AGGREGATE: StockMovement (AR)                    │
│                                                                   │
│  Id · CompanyId · MovementType · OriginType · OriginReference ·   │
│  Status · Reason · ReversedById · ReversesId ·                    │
│  CreatedBy/At · ConfirmedBy/At · Version                          │
│                                                                   │
│  └── 1..N  MovementLine          (Entity)                         │
│            ItemRef · SizeCode? · Quantity ·                       │
│            FromLocationRef? · ToLocationRef? · SegregationKey?    │
│                                                                   │
│  Value Objects: MovementType · DocumentOrigin · Quantity ·        │
│  LocationRef · SizeCode · SegregationKey · Reason                 │
└──────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────┐
│                   AGGREGATE: Reservation (AR)                     │
│                                                                   │
│  Id · CompanyId · RequisitionRef (MMS-003) · ItemRef ·            │
│  SizeCode? · Quantity · FulfilledQuantity · LocationRef ·         │
│  SegregationKey? · Status · ExpiresAt · ReleasedReason ·          │
│  Version                                                          │
│                                                                   │
│  Value Objects: Quantity · LocationRef · SizeCode ·               │
│  SegregationKey · ReservationValidity                             │
└──────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────┐
│                    AGGREGATE: Adjustment (AR)                     │
│                                                                   │
│  Id · CompanyId · AdjustmentReasonType · Justification ·          │
│  Status · InventoryRef? · RegisteredBy · ApproverId? ·            │
│  RejectionReason? · MovementId (efeito) · Version                 │
│                                                                   │
│  └── 1..N  AdjustmentLine        (Entity)                         │
│            ItemRef · SizeCode? · QuantityDelta · LocationRef ·    │
│            SegregationKey?                                        │
│                                                                   │
│  Value Objects: QuantityDelta · Justification ·                   │
│  AdjustmentReasonType                                             │
└──────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────┐
│                  AGGREGATE: InventoryCount (AR)                   │
│                                                                   │
│  Id · CompanyId · Scope (ABC class | geral) · Status ·            │
│  ResponsibleId · Deadline · StartedAt · ClosedAt ·                │
│  Summary · Version                                                │
│                                                                   │
│  └── 1..N  CountEntry            (Entity)                         │
│            ItemRef · SizeCode? · LocationRef · CountedQty ·       │
│            SystemQtySnapshot · CountedBy · CountedAt              │
│                                                                   │
│  Value Objects: CountScope · ToleranceLimits · Quantity           │
└──────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────┐
│                     AGGREGATE: Location (AR)                      │
│                                                                   │
│  Id · CompanyId · LocationType (Warehouse|Deposit|Address) ·      │
│  Code · Description · ParentId? · Active · Version                │
│                                                                   │
│  Value Objects: LocationRef · LocationCode                        │
└──────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────┐
│              READ MODEL: StockBalance (PROJEÇÃO)                  │
│        ⚠ NÃO é Aggregate — sem comandos de escrita (IV-BR-001)    │
│                                                                   │
│  CompanyId · ItemRef · SizeCode? · LocationRef ·                  │
│  SegregationKey? · TotalQty · ReservedQty · AvailableQty ·        │
│  AvgCostReference · LastMovementId · LastMovementAt               │
└──────────────────────────────────────────────────────────────────┘

Referências externas (por Id / referência lógica, fora dos Aggregates):
Item (MMS-002) · Company/Unit (FD-001-02) · Requisition (MMS-003) ·
ReceivingDocument (MMS-005) · WorkflowInstance (FD-001-04) ·
User (FD-001-01)
```

---

# 4. Relacionamentos UML

```text
Company (FD-001-02)        1 ────── N   StockMovement / Reservation / Adjustment /
                                        InventoryCount / Location / StockBalance

StockMovement              1 ────── N   MovementLine
StockMovement              1 ────── 0..1 StockMovement   (estorno: reverses / reversedBy)
StockMovement              N ────── 0..1 Reservation      (saída por atendimento baixa reserva)
StockMovement              N ────── 0..1 Adjustment       (efeito do ajuste aprovado)

Adjustment                 1 ────── N   AdjustmentLine
Adjustment                 1 ────── 0..1 StockMovement    (documento de efeito, após aprovação)
Adjustment                 N ────── 0..1 InventoryCount   (origem, quando de inventário)

InventoryCount             1 ────── N   CountEntry
InventoryCount             1 ────── N   Adjustment        (divergências tratadas)

Location                   1 ────── N   Location          (hierarquia por ParentId)
Location                   1 ────── N   StockBalance      (projeção por local)

Item (MMS-002)             1 ────── N   StockBalance / MovementLine / Reservation /
                                        AdjustmentLine / CountEntry   (por ItemRef)

Reservation                1 ────── N   StockMovement     (liberações/atendimentos vinculados)
```

Cardinalidades de negócio:

- `MovementLine`: 1..N por documento; transferência = exatamente um par (saída + entrada vinculadas — IV-BR-030);
- `Reservation`: 1 por solicitação × item × tamanho × local ativa por vez (mesma chave não admite duas reservas ativas);
- `CountEntry`: 1 por item × tamanho × local do escopo;
- `StockBalance`: 1 por chave (empresa, item, tamanho?, local, segregação?) — projeção única por chave;
- `Location.ParentId`: obrigatório para Deposit (pai Warehouse) e Address (pai Deposit); proibido para Warehouse (IV-BR-050).

---

# 5. Entidades

## StockMovement (Aggregate Root)

Representa o documento de movimentação — a única porta de alteração de saldo (IV-BR-002).

Principais atributos:

- Id (identidade interna gerada pelo sistema);
- CompanyId (isolamento — IV-BR-006);
- MovementType: Entrada / Saída / Transferência / LiberaçãoDeReserva / Estorno;
- OriginType + OriginReference (recebimento MMS-005, devolução/atendimento MMS-003, ajuste, carga inicial, operação — IV-BR-010/011);
- Status: Rascunho / Confirmado / Estornado / Cancelado (MMS-004-03);
- Reason (motivo — obrigatório em estorno e liberação quando parametrizada);
- ReversesId / ReversedById (vínculo de estorno — IV-BR-003);
- Version (optimistic concurrency — IV-BR-012).

---

## MovementLine

Representa uma linha de efeito do documento.

Atributos:

- Id
- ItemRef (referência ao MMS-002 — somente item Ativo em novas operações, IV-BR-007)
- SizeCode (obrigatório quando o item tem grade — IV-BR-120)
- Quantity (> 0, na **unidade base** do item — IV-BR-009)
- CapturedQuantity / CapturedUom / AppliedFactor (quando a linha é capturada em unidade alternativa — ex.: recebimento em caixas; a conversão para a base fica registrada com o fator aplicado no momento — ADR-013, IV-BR-009/093)
- FromLocationRef / ToLocationRef (conforme o tipo: saída exige From; entrada exige To; transferência exige ambos — IV-BR-008/030)
- SegregationKey (cliente/contrato, quando habilitada — IV-BR-060)

---

## Reservation (Aggregate Root)

Representa o bloqueio de disponível em favor de uma solicitação.

Principais atributos:

- Id; CompanyId; RequisitionRef (MMS-003, obrigatória — IV-BR-020);
- ItemRef + SizeCode?; Quantity; FulfilledQuantity;
- LocationRef; SegregationKey?;
- Status: Ativa / Atendida / Liberada / Vencida (MMS-004-03);
- ExpiresAt (criação + TTL — IV-BR-021);
- Version.

---

## Adjustment (Aggregate Root)

Representa a correção de divergência com governança.

Principais atributos:

- Id; CompanyId; AdjustmentReasonType (divergência de inventário, perda, avaria, achado, erro de lançamento — IV-BR-040);
- Justification (obrigatória);
- Status: Pendente / Aprovado / Rejeitado / Estornado (MMS-004-03);
- InventoryRef? (vínculo com inventário de origem — IV-BR-072);
- RegisteredBy; ApproverId? (≠ RegisteredBy — IV-BR-041); RejectionReason?;
- MovementId (documento de efeito, após confirmação);
- Version.

---

## InventoryCount (Aggregate Root)

Representa um ciclo de contagem física.

Principais atributos:

- Id; CompanyId; Scope (classe ABC ou geral, com locais — IV-BR-070);
- Status: Aberto / Em Contagem / Fechado / Cancelado (MMS-004-03);
- ResponsibleId; Deadline; StartedAt; ClosedAt;
- Summary (contados, divergentes, ajustados, dentro da tolerância);
- Version.

---

## CountEntry

Representa a contagem de um item × local do escopo.

Atributos:

- Id
- ItemRef + SizeCode?; LocationRef
- CountedQty (≥ 0)
- SystemQtySnapshot (saldo do sistema no instante da contagem — IV-BR-073)
- CountedBy; CountedAt

Regra: contagem nunca altera saldo — alimenta divergência (IV-BR-071).

---

## Location (Aggregate Root)

Representa um nó da estrutura almoxarifado → depósito → endereço.

Principais atributos:

- Id; CompanyId; LocationType (Warehouse / Deposit / Address);
- Code (único por empresa e tipo); Description;
- ParentId? (hierarquia — IV-BR-050);
- Active (inativação exige saldo zero — IV-BR-052);
- Version.

---

## ReplenishmentSuggestion (Aggregate Root) — ADR-014

Representa uma sugestão **revisável** de reposição, gerada pela avaliação da regra do item (IV-BR-130) sobre o saldo disponível. Não altera saldo e não compra por conta própria — orienta uma ação humana (ou automática, opt-in na v2.0).

Principais atributos:

- Id; CompanyId; ItemRef; LocationRef (depósito);
- TriggeredAvailableQty; ReorderPoint (snapshot do gatilho);
- Policy (`ate_maximo` / `multiplo_embalagem` / `lote_fixo` — IC-BR-100) e SuggestedQuantity (na unidade base, já resolvida pela política);
- Route (`transferencia` / `compra` / `manual` — IC-BR-101);
- Status: Sugerida / Confirmada / Descartada (ver §7);
- OutcomeReference (referência ao PR-001 gerado ou ao documento de transferência, quando confirmada);
- CreatedAt; DecidedBy?; DecidedAt?; Version.

Regras: a quantidade sugerida respeita a política do item (multiplo_embalagem usa o fator de UoM); a confirmação por rota `compra` gera demanda ao PR-001, por `transferencia` gera documento de movimentação (IV-BR-131); execução automática apenas com `materials.replenishment.auto-execute=true`.

---

# 6. Value Objects

## Quantity

- Valor (NUMERIC(18,4))

Imutável. Invariante: `> 0` em movimentações; `≥ 0` em contagens; **sempre na unidade base do item** (IV-BR-009). Quando a linha é capturada em unidade alternativa, a conversão para a base é aplicada com o fator do item (ADR-013) e registrada em `CapturedQuantity`/`CapturedUom`/`AppliedFactor`; o saldo é sempre afetado na base.

---

## QuantityDelta

- Valor (NUMERIC(18,4), com sinal)

Imutável. Usado em AdjustmentLine: positivo aumenta saldo, negativo reduz (respeitando IV-BR-004).

---

## MovementType

- Entrada, Saída, Transferência, LiberaçãoDeReserva, Estorno

Conjunto fechado definido pela suíte (MMS-004 README, escopo 2). Estorno sempre referencia o documento original (IV-BR-003).

---

## DocumentOrigin

- OriginType + OriginReference

Imutável. Invariante: OriginType dentro do conjunto fechado por tipo de documento (IV-BR-010/011); referência obrigatória e verificável.

---

## LocationRef

- LocationId + LocationType + path (warehouse/deposit/address)

Imutável. Invariante: local existente, ativo e compatível com a granularidade configurada (IV-BR-008/051).

---

## SizeCode

- Código de tamanho (referência à grade do item — Master Data)

Imutável. Invariante: pertence à grade vigente do item quando o item tem grade; vazio quando não tem (IV-BR-120).

---

## SegregationKey

- ClientId + ContractId (quando segregação habilitada)

Imutável. Invariante: estoque dedicado só atende a mesma chave (IV-BR-060); transferência preserva a chave (IV-BR-031).

---

## ReservationValidity

- CreatedAt + ExpiresAt

Imutável. Invariante: `ExpiresAt = CreatedAt + materials.inventory.reservation.ttl` (IV-BR-021); vencimento avaliado pelo job, nunca por escrita direta.

---

## BalanceSnapshot

- TotalQty + ReservedQty + AvailableQty no instante

Imutável. Registrado na auditoria de toda confirmação (saldo anterior/posterior — IV-BR-090) e no `SystemQtySnapshot` da contagem (IV-BR-073).

---

## Justification / Reason

- Texto (mínimo/máximo parametrizável)

Imutável. Obrigatório em ajuste (IV-BR-040), estorno (IV-BR-003), rejeição (IV-BR-041), cancelamento de inventário e liberação de reserva quando parametrizada (IV-BR-022).

---

## AdjustmentReasonType

- Divergência de inventário, Perda, Avaria, Achado, Erro de lançamento

Conjunto padrão extensível por configuração (`materials.inventory.adjustment.reasons` — IV-BR-040).

---

## CountScope

- Tipo (classe ABC | geral) + Classes? + LocationRefs

Imutável. Invariante: escopo não vazio; sem sobreposição com outro inventário aberto (IV-BR-070).

---

## ToleranceLimits

- ToleranceQty + TolerancePercent

Imutável. Divergência ≤ tolerância: registrada sem ajuste; acima: proposta de ajuste (IV-BR-072).

---

# 7. Enumerações

## Movement Status (MMS-004-03)
Rascunho · Confirmado · Estornado · Cancelado

## Reservation Status (MMS-004-03)
Ativa · Atendida · Liberada · Vencida

## Adjustment Status (MMS-004-03)
Pendente de Aprovação · Aprovado · Rejeitado · Estornado

## Inventory Status (MMS-004-03)
Aberto · Em Contagem · Fechado · Cancelado

## LocationType
Warehouse · Deposit · Address

## ReplenishmentSuggestion Status (ADR-014)
Sugerida · Confirmada · Descartada

---

# 8. Invariantes

Um documento de movimentação nunca poderá existir:

- sem empresa (IV-BR-006);
- sem documento de origem (IV-BR-002/010/011);
- sem ao menos uma linha com quantidade > 0 (IV-BR-009);
- com item inativo em nova operação (exceto devolução — IV-BR-007);
- com local inválido, inativo ou fora da granularidade (IV-BR-008/051);
- com tamanho fora da grade do item (IV-BR-120).

Nenhum saldo poderá jamais:

- ser alterado fora da confirmação de documento (IV-BR-001);
- ficar negativo, em qualquer visão (IV-BR-004);
- dedicado, atender outro cliente/contrato (IV-BR-060).

Invariantes formalizadas:

| Código | Invariante | Origem |
|--------|-----------|--------|
| INV-IV-01 | Saldo só muda por confirmação de documento | IV-BR-001 (MMS-P-08) |
| INV-IV-02 | Todo documento tem origem referenciável | IV-BR-002/010/011 (MMS-P-07) |
| INV-IV-03 | Documento Confirmado não aceita comandos de edição; correção = estorno | IV-BR-003 |
| INV-IV-04 | `total ≥ 0` e `disponível ≥ 0` sempre | IV-BR-004/005 |
| INV-IV-05 | `disponível = total − reservado` por construção | IV-BR-005 (MMS-RG-09) |
| INV-IV-06 | Reserva ativa única por (requisição, item, tamanho, local, segregação) | IV-BR-020 |
| INV-IV-07 | `fulfilledQuantity ≤ quantity` na reserva | IV-BR-023 |
| INV-IV-08 | Transferência = saída + entrada na mesma transação | IV-BR-030 |
| INV-IV-09 | SegregationKey de origem = de destino na transferência | IV-BR-031 |
| INV-IV-10 | `approver ≠ registrant` em todo ajuste | IV-BR-041 |
| INV-IV-11 | Contagem não altera saldo; divergência vs. snapshot | IV-BR-071/073 |
| INV-IV-12 | Local com saldo ≠ 0 não pode ser inativado | IV-BR-052 |
| INV-IV-13 | Tamanho da reserva = tamanho da entrega | IV-BR-121 |
| INV-IV-14 | Transições somente pelas matrizes da State Machine | MMS-004-03 §10 |
| INV-IV-15 | `version` incrementa a cada alteração persistida; confirmação sequencial por chave de saldo | IV-BR-012 |
| INV-IV-16 | Toda confirmação grava BalanceSnapshot anterior/posterior na auditoria | IV-BR-090 |
| INV-IV-17 | Linha capturada em unidade alternativa persiste `Quantity` na base + `AppliedFactor` do momento | IV-BR-009 (ADR-013) |
| INV-IV-18 | Sugestão de reposição nunca altera saldo nem compra; confirmação por rota gera PR-001/transferência | IV-BR-130/131 (ADR-014) |

---

# 9. Comportamentos

## StockMovement

Create() — Rascunho, validações de entrada (IV-BR-006..009/120)

Update() — somente em Rascunho

Confirm() — aplica efeito via `StockBalanceService`; publica evento do tipo (MMS-004-03)

Cancel(reason) — somente Rascunho

Reverse(reason) — gera documento de estorno vinculado (IV-BR-003)

## Reservation

Create() — bloqueia disponível via `StockBalanceService` (IV-BR-020)

Fulfill(quantity, movementRef) — baixa atômica na saída (IV-BR-023)

Release(reason?) — liberação manual (IV-BR-022)

Expire() — exclusiva do job de vencimento (IV-BR-021)

## Adjustment

Register() — Pendente (ou Aprovado direto quando approval não exigida — IV-BR-041)

Approve(approverId) — confirma e gera StockMovement de efeito

Reject(approverId, reason)

Reverse(reason) — estorno (aprovado quando parametrizado)

## InventoryCount

Open(scope, responsible, deadline)

StartCounting()

RegisterCount(entry) — com snapshot do saldo (IV-BR-073)

Close() — gera propostas de ajuste e sumário (IV-BR-072)

Cancel(reason) — somente Aberto sem contagens

## Location

Create() / Update() / Inactivate() — inativação exige saldo zero (IV-BR-052)

## ReplenishmentSuggestion (ADR-014)

Generate(itemRef, locationRef, availableQty) — cria a sugestão a partir da regra do item (IV-BR-130)

Confirm(decidedBy, route) — gera PR-001 (compra) ou documento de transferência (IV-BR-131)

Discard(decidedBy, reason?) — descarta a sugestão sem efeito

---

## Factory Methods

Criação sempre via fábricas do Aggregate — construtores nunca expostos:

| Factory | Assinatura | Garantias |
|---------|-----------|-----------|
| `StockMovement.CreateEntry` | (companyId, origin, lines, locations) | Tipo Entrada; origem válida (IV-BR-010); Rascunho; `version=1` |
| `StockMovement.CreateIssue` | (companyId, origin, lines, reservationRef?) | Tipo Saída; reserva vinculada quando atendimento (IV-BR-011) |
| `StockMovement.CreateTransfer` | (companyId, lines com from/to) | Par saída+entrada; mesma SegregationKey (IV-BR-030/031) |
| `StockMovement.CreateReversal` | (originalId, reason, authorId) | Espelha o original com efeito inverso (IV-BR-003) |
| `Reservation.Create` | (companyId, requisitionRef, itemRef, sizeCode?, quantity, locationRef, segregationKey?) | Disponível suficiente (IV-BR-005/020); ExpiresAt calculado (IV-BR-021) |
| `Adjustment.Register` | (companyId, reasonType, justification, lines, inventoryRef?) | Justificativa obrigatória (IV-BR-040); status inicial conforme parâmetro (IV-BR-041) |
| `InventoryCount.Open` | (companyId, scope, responsibleId, deadline) | Escopo válido e sem sobreposição (IV-BR-070) |
| `Location.Create` | (companyId, type, code, parentId?) | Hierarquia íntegra (IV-BR-050) |
| `ReplenishmentSuggestion.Generate` | (companyId, itemRef, locationRef, availableQty, reorderPoint, policy, route) | Quantidade sugerida pela política (IV-BR-130); sem duplicidade de sugestão aberta por item×depósito |

---

# 10. Repositories

Interfaces de persistência do módulo (implementação em Infrastructure; o domínio não conhece o banco — ADR-009):

| Repositório | Operações |
|-------------|-----------|
| `IStockMovementRepository` | `GetById(id)`, `GetByOrigin(originType, originRef)`, `Add(movement)`, `Update(movement)`, `Search(specification, keyset)` |
| `IReservationRepository` | `GetById(id)`, `GetActiveByKey(requisitionRef, itemRef, sizeCode?, locationRef)`, `FindExpiring(until)`, `Add(reservation)`, `Update(reservation)` |
| `IAdjustmentRepository` | `GetById(id)`, `FindPending(companyId)`, `Add(adjustment)`, `Update(adjustment)` |
| `IInventoryCountRepository` | `GetById(id)`, `FindOpen(companyId)`, `Add(count)`, `Update(count)` |
| `ILocationRepository` | `GetById(id)`, `GetChildren(parentId)`, `Search(companyId, type)`, `Add(location)`, `Update(location)` |
| `IStockBalanceReadRepository` | `GetByKey(itemRef, sizeCode?, locationRef, segregationKey?)`, `Search(specification, keyset)` — **somente leitura** |
| `MovementLine / AdjustmentLine / CountEntry` | (acesso somente via Aggregate; nunca direto) |

Regras:

- Todo repositório aplica filtro `company_id` obrigatório (IV-BR-006);
- `Update` verifica `version` (optimistic concurrency — IV-BR-012);
- Confirmações sobre a mesma chave de saldo são serializadas (IV-BR-012) — a implementação usa bloqueio por chave na camada de Infrastructure;
- Listagens usam Keyset Pagination (IV-BR-111);
- Não existe `Delete` físico em nenhum repositório; cancelamentos são soft delete;
- **Não existe repositório de escrita de saldo** — a projeção `StockBalance` é atualizada exclusivamente pelo `StockBalanceService` na confirmação de documentos (INV-IV-01).

---

# 11. Specifications

Consultas e validações expressas como especificações combináveis:

| Specification | Propósito | Uso |
|---------------|-----------|-----|
| `AvailableBalanceByItem` | Disponível por item × local × segregação | Validação MMS-003 (IV-BR-005) |
| `BalanceBelowMinimum` | Disponível < mínimo do item (MMS-002) | Alerta de mínimo (IV-BR-080) |
| `StockoutCondition` | Disponível = 0 ∧ demanda aberta | Alerta de ruptura (IV-BR-081) |
| `ActiveReservationsByRequisition` | Reservas ativas de uma solicitação | MMS-003 |
| `ExpiringReservations` | Ativas com `expires_at` na janela | Alerta pré-vencimento (IV-BR-082) |
| `ExpiredReservations` | Ativas com `expires_at` vencido | Job de vencimento (IV-BR-021) |
| `MovementsByItem` | Extrato por item | UC-IV-011 |
| `MovementsByDocument` | Extrato por documento/origem | UC-IV-011; auditoria |
| `PendingAdjustments` | Ajustes pendentes por empresa/escopo | Fila do aprovador (IV-BR-041) |
| `CountScopeItems` | Itens × locais do escopo do inventário | Abertura (IV-BR-070) |
| `DivergentEntries` | Contagens com divergência > tolerância | Fechamento (IV-BR-072) |
| `LocationsWithBalance` | Locais com saldo ≠ 0 | Guarda de inativação (IV-BR-052) |
| `WithinCompanyScope` | Escopo organizacional do usuário | Transversal (IV-BR-101) |

Composição: `And`, `Or`, `Not`.

---

# 12. Domain Services

Operações de domínio que não pertencem naturalmente a uma entidade:

| Serviço | Responsabilidade |
|---------|------------------|
| `StockBalanceService` | **Único ponto de aplicação de efeitos sobre saldo** (INV-IV-01): aplica confirmações na projeção, com serialização por chave (IV-BR-012), grava BalanceSnapshot anterior/posterior na auditoria (IV-BR-090) |
| `StockValidationService` | Responde à validação de estoque do MMS-003 (atende/não atende por item) usando somente o disponível (IV-BR-005/060) |
| `ReservationExpirationProcessor` | Job de vencimento: emite documentos de liberação para reservas expiradas (IV-BR-021) — nunca escrita direta |
| `ReservationExpiringNotifier` | Avalia reservas na janela e dispara alerta pré-vencimento (IV-BR-082) |
| `TransferCoordinator` | Garante atomicidade do par saída+entrada e preservação de segregação (IV-BR-030/031) |
| `DivergenceAnalyzer` | Calcula divergências contra snapshots, aplica tolerância e propõe ajustes (IV-BR-072) |
| `StockAlertEvaluator` | Avalia mínimo e ruptura após confirmações e emite alertas via FD-001-05 (IV-BR-080/081/083) |
| `LocationHierarchyValidator` | Integridade da estrutura e granularidade (IV-BR-050/051) |
| `InventoryReadModelRefresher` | Mantém o cache de leitura de saldos/posição (IV-BR-110) a partir dos eventos |
| `UomConversionApplier` | Converte a quantidade capturada em unidade alternativa para a base pelo fator do item e registra `AppliedFactor` na linha (ADR-013, IV-BR-009/093) |
| `ReplenishmentEvaluator` | Avalia a regra do item após confirmação que reduz o disponível (ou por agenda) e gera `ReplenishmentSuggestion` (IV-BR-130); nunca escreve saldo |

---

# 13. Policies

Políticas de negócio reativas a eventos:

| Política | Gatilho | Ação |
|----------|---------|------|
| POL-IV-01 | Qualquer evento de domínio | Registrar marco na Timeline (IV-BR-091) |
| POL-IV-02 | Confirmação de documento / reserva / ajuste | Registrar auditoria com saldo anterior/posterior (IV-BR-090) |
| POL-IV-03 | Entrada/Saída/Transferência registrada | Reavaliar mínimo e ruptura dos itens afetados (IV-BR-080/081) |
| POL-IV-04 | Reserva criada (disponível reduzido) | Reavaliar mínimo/ruptura; alertar se cruzar limiares |
| POL-IV-05 | Reserva vencida | Notificar almoxarifado e MMS-003; KPI de vencidas |
| POL-IV-06 | Item inativado (evento consumido — MMS-002) | Bloquear novas reservas do item; saldo segue movimentável (IV-BR-007) |
| POL-IV-07 | Recebimento conferido (evento consumido — MMS-005) | Gerar documento de entrada vinculado (IV-BR-010) |
| POL-IV-08 | Solicitação cancelada (evento consumido — MMS-003) | Liberar reservas ativas vinculadas (IV-BR-022) |
| POL-IV-09 | Qualquer evento que altere projeção de saldo | Invalidar cache de leitura (IV-BR-110) |
| POL-IV-10 | Confirmação que reduz o disponível abaixo do ponto de pedido (regra habilitada) | Avaliar a regra de reposição e gerar `ReplenishmentSuggestion` (IV-BR-130) |
| POL-IV-11 | Sugestão confirmada por rota `compra` / `transferencia` | Gerar demanda PR-001 ou documento de transferência com rastreabilidade (IV-BR-131) |

---

# 14. Domain Events Produzidos

Entrada registrada · Saída registrada · Transferência registrada · Estorno registrado
Reserva criada · Reserva atendida · Reserva liberada · Reserva vencida
Ajuste registrado · Ajuste aprovado · Ajuste rejeitado
Inventário iniciado · Contagem registrada · Divergência aprovada
Alerta de estoque mínimo · Alerta de ruptura
Sugestão de reposição gerada · Sugestão de reposição confirmada · Sugestão de reposição descartada (ADR-014)

(Especificação técnica no MMS-004-05, conforme ADR-010.)

---

# 15. Limites dos Aggregates

Fazem parte do módulo:

- StockMovement + MovementLine
- Reservation
- Adjustment + AdjustmentLine
- InventoryCount + CountEntry
- Location
- ReplenishmentSuggestion (ADR-014)
- StockBalance (projeção de leitura, sem Aggregate próprio)

Não fazem parte (outros Bounded Contexts ou referências externas):

- Item, unidade, categoria, grade de tamanhos e parâmetros de reposição (MMS-002 / FD-001-09);
- Solicitação de material e separação (MMS-003);
- Recebimento e conferência (MMS-005);
- Demanda de compra (PR-001);
- Empresa, unidade e centro de custo (FD-001-02);
- Workflow de aprovação (FD-001-04 — referência por Id);
- Notificações (FD-001-05), Auditoria (FD-001-06) e Timeline (FD-001-07).

---

# 16. Dependências

| Dependência | Uso |
|-------------|-----|
| MMS-002 (Item Catalog) | ItemRef, estado do item (IV-BR-007), grade (IV-BR-120), mínimo (IV-BR-080) |
| FD-001-01 / FD-001-02 | Autorização, segregação de funções (INV-IV-10), escopo e empresa |
| FD-001-04 | Workflow de aprovação de ajuste (IV-BR-041) |
| FD-001-05 | Alertas (POL-IV-03..05; IV-BR-083) |
| FD-001-06 / FD-001-07 | Auditoria e timeline (IV-BR-090/091) |
| FD-001-10 | Parâmetros `materials.inventory.*` |
| MMS-004-03 | Máquinas de estado executadas pelos comportamentos |
| MMS-003 / MMS-005 | RequisitionRef / origem de entradas (integrações) |

---

# 17. Histórico de Versão

| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-07-30 | Criação do Domain Model do Inventory Management: 5 Aggregate Roots (StockMovement, Reservation, Adjustment, InventoryCount, Location) + StockBalance como projeção não editável; Aggregate Diagram e relacionamentos UML; Value Objects completos (Quantity, QuantityDelta, MovementType, DocumentOrigin, LocationRef, SizeCode, SegregationKey, ReservationValidity, BalanceSnapshot, Justification, AdjustmentReasonType, CountScope, ToleranceLimits); 16 invariantes formalizadas (INV-IV-01..16) rastreadas às IV-BR; factory methods; repositories (sem escrita de saldo; company_id, optimistic concurrency, keyset); 13 specifications; 9 domain services (StockBalanceService como único ponto de efeito sobre saldo); 9 policies; eventos produzidos e limites dos Aggregates — no padrão MMS-002-04, derivado da visão do módulo, das Business Rules e da State Machine |
| 1.1.0 | 2026-08-08 | Incorporação de ADR-013 e ADR-014: `MovementLine` ganha `CapturedQuantity`/`CapturedUom`/`AppliedFactor` (captura em unidade alternativa convertida para a base; `Quantity` passa a ser explicitamente na base — IV-BR-009); novo Aggregate Root **ReplenishmentSuggestion** (Sugerida/Confirmada/Descartada) com factory `Generate` e comportamentos `Confirm`/`Discard`; domain services `UomConversionApplier` e `ReplenishmentEvaluator`; políticas POL-IV-10/11; eventos de sugestão de reposição; invariantes INV-IV-17/18; agora 6 Aggregate Roots. Rastreado a IV-BR-009/130/131 (MMS-004-02 v1.1.0) |
