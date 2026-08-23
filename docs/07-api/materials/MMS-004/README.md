# MMS-004-13 — API do Inventory Management

| Campo | Valor |
|---|---|
| **Documento** | MMS-004-13 |
| **Módulo** | Materials — Inventory Management (MMS-004) |
| **Versão** | 1.0.0 |
| **Status** | 🟢 Approved |
| **Data** | 2026-08-23 |
| **Dependências** | MMS-004 (Visão) v1.0.0, MMS-004-02 (Business Rules), MMS-004-03 (State Machine), MMS-004-05 (Event Storming), MMS-004-07 (Use Cases), MMS-004-09 (Permissions), MMS-004-11 (Database), FD-001-01 (IAM), FD-001-05 (Notifications), FD-001-06 (Audit), FD-001-07 (Timeline), FD-001-09 (Master Data), FD-001-10 (Configuration), SEC-001/003, ADR-009, ADR-010 |
| **Escopo** | API REST pública `v1` do módulo Inventory Management |

---

## 1. Objetivo

Este documento é a especificação oficial da **API REST do módulo Inventory Management**. Ele consolida os endpoints referenciados pelos casos de uso (MMS-004-07) em um contrato único, com convenções, modelos de dados, códigos de status, erros, segurança e versionamento.

**Regra-mestre:** este documento descreve; as regras de negócio permanecem em MMS-004-02, as transições em MMS-004-03, as permissões em MMS-004-09 e os eventos em MMS-004-05. Em caso de conflito, prevalecem os documentos de negócio. **Nenhum endpoint escreve saldo** — o efeito é sempre da confirmação de documento (MMS-P-08; INV-IV-01).

---

## 2. Convenções Gerais

### 2.1 Base URL e versionamento

- Base: `/api/v1/inventory`
- Versionamento por path (`v1`); mudanças incompatíveis exigem `v2` — nunca quebra em versão publicada.
- Contrato formal em OpenAPI 3.1 gerado a partir deste documento (artefato de build), com este texto como fonte semântica.

### 2.2 Envelope de resposta

**Sucesso (recurso único):**

```json
{
  "data": { "id": "…", "number": "ENT-2026-000123" },
  "correlationId": "01J…"
}
```

**Sucesso (coleção, keyset):**

```json
{
  "data": [ … ],
  "page": { "nextCursor": "eyJ…", "hasMore": true, "pageSize": 50 },
  "correlationId": "01J…"
}
```

**Erro:**

```json
{
  "error": {
    "code": "IV-ERR-020",
    "message": "Saldo insuficiente: disponível 4, solicitado 6.",
    "details": [{ "field": "lines[0].quantity", "reason": "INSUFFICIENT_AVAILABLE", "available": 4, "requested": 6 }],
    "correlationId": "01J…",
    "traceId": "…"
  }
}
```

### 2.3 Paginação (keyset)

- Ordenação padrão: `(confirmed_at, id) DESC` no extrato e listagem de documentos; `(item_id, location_id, id) ASC` na posição; `(expires_at, id) ASC` na fila de reservas; cursor **assinado**, validade 15 minutos (padrão Foundation).
- Parâmetros: `cursor`, `pageSize` (padrão 50 — `materials.inventory.search.page-size`, máx. 100).
- Offset/limit é **proibido** (MMS-004-11 §15.9).
- Cursor inválido/expirado ⇒ `400 IV-ERR-400`.

### 2.4 Idempotência

- Endpoints de escrita aceitam header `Idempotency-Key` (UUID por operação do cliente); replay dentro de 24h retorna a resposta original (`200`/`201` com `idempotentReplay: true`).
- **Idempotência funcional de origem** (IV-BR-090; FA-IV-005): entrada com a mesma chave de origem (ex.: mesmo recebimento MMS-005) retorna o documento já registrado, sem efeito duplicado — mesmo sem `Idempotency-Key`.
- Sem a chave, dupla submissão é tratada pelas regras de estado e unicidade (`409 IV-ERR-090` estado não permite, `409 IV-ERR-409` conflito de versão).

### 2.5 Autenticação e autorização

- Bearer JWT (15 min) + refresh rotativo (FD-001-01, SEC-003).
- Scopes do módulo: `inventory.read`, `inventory.write`, `inventory.reserve`, `inventory.adjust`, `inventory.adjust.approve`, `inventory.count`, `inventory.reverse`, `inventory.locations`, `inventory.admin`, `inventory.audit` (MMS-004-09 §12.5).
- Fluxo de autorização por request: **escopo (404) → RBAC → ABAC (ABAC-IV-01..06) → delegação (403)** — deny by default (POL-IV-AUTH-001..011).
- SoD avaliado server-side: aprovação exige `userId ≠ registeredBy` (IV-BR-085); estorno de ajuste exige `userId ≠ approvedBy` (IV-BR-114) — violação ⇒ `403 IV-ERR-085` + alerta de segurança.
- Perfil Requester não possui nenhuma permissão neste módulo (IV-BR-097): recurso fora do escopo ⇒ `404`.
- Todo `403` e toda ação sensível geram trilha de auditoria (FD-001-06).

### 2.6 Cabeçalhos obrigatórios e suportados

| Header | Uso |
|---|---|
| `Authorization: Bearer` | Obrigatório em todos os endpoints. |
| `X-Correlation-Id` | Opcional; se ausente, o servidor gera. Propagado a eventos e auditoria (cadeia MMS-005 → entrada → alerta). |
| `Idempotency-Key` | Obrigatório em POST de escrita (MMS-004-07 §0). |
| `If-Match` | Obrigatório em PATCH de recursos versionados (controle otimista; `409 IV-ERR-409` em conflito). |
| `Accept-Language` | `pt-BR` (padrão) ou `en-US` — afeta `message` de erros. |

### 2.7 Códigos HTTP

| Código | Uso |
|---|---|
| `200` | Sucesso em consulta/ação síncrona (confirmar, liberar, aprovar, fechar). |
| `201` | Recurso criado (com `Location`): documento, reserva, ajuste, inventário, contagem, local, estorno. |
| `204` | Sem conteúdo (não usado para documentos — cancelamento retorna `200` com o estado final). |
| `400` | Validação de entrada (`IV-ERR-4xx` de formato, cursor, payload; motivo ausente). |
| `401` | Token ausente/inválido/expirado. |
| `403` | Autenticado, mas sem permissão (RBAC/ABAC/delegação/SoD) — `IV-ERR-900`/`IV-ERR-085`. |
| `404` | Recurso inexistente **ou fora do escopo** do usuário (anti-enumeração — `IV-ERR-404`). |
| `409` | Conflito de estado (`IV-ERR-090`/`IV-ERR-110`), de versão (`IV-ERR-409`) ou de unicidade (`IV-ERR-061`). |
| `422` | Regra de negócio violada com payload válido (saldo insuficiente, segregação, tamanho, tolerância, escopo de inventário). |
| `429` | Rate limit excedido (`Retry-After` presente) — `IV-ERR-429`. |
| `500` | Erro interno (sempre com `correlationId`; nunca stack trace). |

### 2.8 Rate limit

| Classe | Limite padrão | Escopo |
|---|---|---|
| Leitura (posição, extrato, filas) | 600 req/min | por usuário |
| Escrita (documentos, reservas, contagens) | 240 req/min | por usuário |
| Ações de decisão (aprovar, rejeitar, estornar, fechar) | 60 req/min | por usuário |
| Integrações machine-to-machine (MMS-003/MMS-005) | 1200 req/min | por credencial de serviço |

Resposta `429` com corpo de erro `IV-ERR-429` e `Retry-After`. Limites ajustáveis via Configuration (`materials.inventory.rate-limit.*`, FD-001-10).

---

## 3. Modelo de Recursos (contratos)

### 3.1 `StockMovement`

```json
{
  "id": "uuid",
  "number": "ENT-2026-000123",
  "movementType": "ENTRY | ISSUE | TRANSFER | RESERVATION_RELEASE | REVERSAL",
  "originType": "RECEIVING | RETURN | ADJUSTMENT | INITIAL_LOAD | FULFILLMENT | CONSUMPTION | OPERATION | INVENTORY | REVERSAL | EXPIRATION",
  "originReference": "REC-2026-000045",
  "reservationId": "uuid|null",
  "adjustmentId": "uuid|null",
  "reversesId": "uuid|null",
  "reversedById": "uuid|null",
  "reason": "string|null",
  "status": "DRAFT | CONFIRMED | REVERSED | CANCELLED",
  "lines": [
    {
      "lineId": "uuid",
      "itemId": "uuid",
      "sizeCode": "M|null",
      "quantity": 50,
      "fromLocationId": "uuid|null",
      "toLocationId": "uuid|null",
      "clientId": "uuid|null",
      "contractId": "uuid|null",
      "balanceBefore": { "total": 100, "reserved": 20, "available": 80 },
      "balanceAfter":  { "total": 150, "reserved": 20, "available": 130 }
    }
  ],
  "confirmedAt": "…|null",
  "confirmedBy": "uuid|null",
  "version": 2,
  "createdAt": "2026-08-23T14:00:00Z",
  "updatedAt": "2026-08-23T14:01:00Z"
}
```

- `status` segue a máquina de estados MMS-004-03 (`ST-IV-001..004`); documento `CONFIRMED` é imutável — correção por `POST …/reverse`.
- `balanceBefore/After` por linha aparecem **somente após a confirmação** (IV-BR-095) — o extrato reconstrói a trilha sem consultar o Audit Service.
- `sizeCode` obrigatório quando o item possui grade (IV-BR-120); deve pertencer à grade vigente.
- `version` sustenta `If-Match` em `PATCH` de rascunho.

### 3.2 `Reservation`

```json
{
  "id": "uuid",
  "number": "RSV-2026-000815",
  "requisitionReference": "MR-2026-000512|null",
  "itemId": "uuid",
  "sizeCode": "M|null",
  "quantity": 10,
  "fulfilledQuantity": 4,
  "remainingQuantity": 6,
  "locationId": "uuid",
  "clientId": "uuid|null",
  "contractId": "uuid|null",
  "status": "ACTIVE | FULFILLED | RELEASED | EXPIRED",
  "expiresAt": "2026-08-26T14:00:00Z",
  "expiringSoon": false,
  "releasedReason": "string|null",
  "version": 3,
  "createdAt": "…"
}
```

### 3.3 `Adjustment`

```json
{
  "id": "uuid",
  "number": "ADJ-2026-000042",
  "reasonTypeId": "uuid",
  "reasonTypeLabel": "Divergência de inventário",
  "justification": "string",
  "status": "PENDING | APPROVED | REJECTED | REVERSED",
  "inventoryCountId": "uuid|null",
  "registeredBy": "uuid",
  "approvedBy": "uuid|null",
  "rejectionReason": "string|null",
  "movementId": "uuid|null",
  "lines": [
    { "itemId": "uuid", "sizeCode": null, "quantityDelta": -3, "locationId": "uuid" }
  ],
  "version": 2,
  "createdAt": "…",
  "decidedAt": "…|null"
}
```

### 3.4 `InventoryCount`

```json
{
  "id": "uuid",
  "number": "INV-2026-000007",
  "scopeType": "GENERAL | CYCLIC",
  "scopeClasses": "A|null",
  "scopeLocationIds": ["uuid"],
  "status": "OPEN | COUNTING | CLOSED | CANCELLED",
  "responsibleId": "uuid",
  "deadline": "2026-08-26",
  "summary": { "counted": 120, "divergent": 3, "adjusted": 3, "withinTolerance": 0 },
  "accuracyRate": 97.5,
  "version": 4,
  "startedAt": "…|null",
  "closedAt": "…|null"
}
```

### 3.5 `StockBalanceView` (posição — somente leitura)

```json
{
  "itemId": "uuid",
  "sizeCode": "M|null",
  "locationId": "uuid",
  "locationCode": "DEP-CENTRAL",
  "clientId": "uuid|null",
  "contractId": "uuid|null",
  "totalQty": 150,
  "reservedQty": 20,
  "availableQty": 130,
  "avgCostReference": 12.5,
  "lastMovementAt": "…"
}
```

- Projeção derivada (IV-BR-096): **não existe** endpoint de escrita sobre este recurso.

### 3.6 `StockAlertView`

```json
{
  "id": "uuid",
  "alertType": "MINIMUM | STOCKOUT",
  "itemId": "uuid",
  "sizeCode": null,
  "locationId": "uuid",
  "status": "OPEN | NORMALIZED",
  "availableQtyAtTrigger": 8,
  "minStockAtTrigger": 10,
  "openDemand": [{ "requisitionReference": "MR-…", "quantity": 6 }],
  "acknowledgedBy": "uuid|null",
  "openedAt": "…",
  "normalizedAt": "…|null"
}
```

### 3.7 `StatementEntryView` (extrato)

```json
{
  "movementId": "uuid",
  "number": "SAI-2026-000210",
  "movementType": "ISSUE",
  "originType": "FULFILLMENT",
  "originReference": "MR-2026-000512",
  "sizeCode": null,
  "quantity": 6,
  "direction": "OUT",
  "balanceBeforeTotal": 36,
  "balanceAfterTotal": 30,
  "confirmedAt": "…",
  "confirmedBy": "uuid"
}
```

---

## 4. Catálogo de Endpoints

### 4.1 Documentos de movimentação

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 1 | `POST` | `/api/v1/inventory/movements` | UC-IV-001/002 | IV-PERM-001 (`inventory.write`) | Cria documento em `DRAFT` (type=ENTRY/ISSUE, opcionalmente `reservationId`); `201` + `id`/`number` |
| 2 | `PATCH` | `/api/v1/inventory/movements/{id}` | UC-IV-001/002 | IV-PERM-001 (`inventory.write`) | Edita rascunho (linhas, locais); `If-Match` obrigatório; somente `DRAFT` |
| 3 | `POST` | `/api/v1/inventory/movements/{id}/confirm` | UC-IV-001/002 | IV-PERM-002 (`inventory.write`) | Confirma e efetiva saldo (SP-IV-01); `200` ou `422` com pendências por linha; publica EVT-IV-001/002 (+006) |
| 4 | `POST` | `/api/v1/inventory/movements/{id}/cancel` | UC-IV-001/002 | IV-PERM-001 (`inventory.write`) | Cancela rascunho (soft delete); corpo `{ reason }`; `200` |
| 5 | `POST` | `/api/v1/inventory/movements/{id}/reverse` | UC-IV-008 | IV-PERM-007 (`inventory.reverse`) | Estorna documento confirmado; corpo `{ reason }` (mín. 10 chars); `201` com o documento de estorno; publica EVT-IV-004 |
| 6 | `GET` | `/api/v1/inventory/movements?cursor=&type=&status=&originType=&itemId=&locationId=&requesterId=&costCenterId=&productGroup=&number=&from=&to=` | UC-IV-011 | IV-PERM-009 (`inventory.read`) | Lista de documentos com filtros da visão do almoxarifado (IV-BR-097); keyset por `(confirmed_at, id) DESC` |
| 7 | `GET` | `/api/v1/inventory/movements/{id}` | UC-IV-011 | IV-PERM-009 (`inventory.read`) | Detalhe com linhas e saldos anterior/posterior; `404` fora do escopo |

### 4.2 Transferências

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 8 | `POST` | `/api/v1/inventory/transfers` | UC-IV-005 | IV-PERM-001/002 (`inventory.write`) | Cria **e confirma** transferência atômica (saída origem + entrada destino); `201` ou `422` com pendências; publica EVT-IV-003 |

### 4.3 Reservas

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 9 | `POST` | `/api/v1/inventory/reservations` | UC-IV-003 | IV-PERM-003 (`inventory.reserve`) | Cria reserva sobre o disponível; `201` + `number`/`expiresAt`; parcial sinalizada no corpo; publica EVT-IV-005 |
| 10 | `GET` | `/api/v1/inventory/reservations?cursor=&status=&itemId=&locationId=&requisition=&expiring=` | UC-IV-004 | IV-PERM-009 (`inventory.read`) | Fila de reservas (visão do almoxarifado); `expiring=true` filtra a janela pré-vencimento (TMR-IV-002) |
| 11 | `POST` | `/api/v1/inventory/reservations/{id}/release` | UC-IV-004 | IV-PERM-003 (`inventory.reserve`) | Libera reserva ativa; corpo `{ reason? }` (obrigatório quando parametrizado); `200`; publica EVT-IV-007 |
| 12 | `POST` | `/api/v1/inventory/reservations/{id}/fulfill` | UC-IV-002 | IV-PERM-003 (`inventory.reserve` + `inventory.write`) | Atalho: gera e confirma a saída vinculada à reserva; corpo `{ lines: [{ quantity }] }`; `201`; publica EVT-IV-002/006 |

O vencimento (EVT-IV-008) é exclusivo do job TMR-IV-001 — **não existe endpoint** para vencer reserva.

### 4.4 Ajustes

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 13 | `POST` | `/api/v1/inventory/adjustments` | UC-IV-006 | IV-PERM-004 (`inventory.adjust`) | Registra ajuste em `PENDING`; corpo com motivo estruturado + justificativa; `201`; publica EVT-IV-009 |
| 14 | `GET` | `/api/v1/inventory/adjustments?cursor=&status=&registeredBy=&from=&to=` | UC-IV-006 | IV-PERM-009 (`inventory.read`) | Fila de aprovação e histórico |
| 15 | `POST` | `/api/v1/inventory/adjustments/{id}/approve` | UC-IV-006 | IV-PERM-005 (`inventory.adjust.approve`) | Aprova (SoD: `≠ registeredBy`), revalida saldo e aplica efeito; corpo `{ comment? }`; `200` ou `422 IV-ERR-084`; publica EVT-IV-010 |
| 16 | `POST` | `/api/v1/inventory/adjustments/{id}/reject` | UC-IV-006 | IV-PERM-005 (`inventory.adjust.approve`) | Rejeita; corpo `{ reason }` obrigatório; `200`; publica EVT-IV-011 |

### 4.5 Inventários (contagem física)

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 17 | `POST` | `/api/v1/inventory/counts` | UC-IV-007 | IV-PERM-006 (`inventory.count`) | Abre inventário (escopo geral/cíclico + locais + prazo); `201`; publica EVT-IV-012 |
| 18 | `POST` | `/api/v1/inventory/counts/{id}/start` | UC-IV-007 | IV-PERM-006 (`inventory.count`) | Inicia a contagem (OPEN → COUNTING); `200` |
| 19 | `POST` | `/api/v1/inventory/counts/{id}/entries` | UC-IV-007 | IV-PERM-001 (`inventory.write`) | Registra contagem (lista cega por padrão — `count.blind`); `201`; publica EVT-IV-013; nunca altera saldo |
| 20 | `GET` | `/api/v1/inventory/counts/{id}/divergences` | UC-IV-007 | IV-PERM-009 (`inventory.read`) | Apuração: contado × sistêmico por chave, com tolerância aplicada |
| 21 | `POST` | `/api/v1/inventory/counts/{id}/close` | UC-IV-007 | IV-PERM-006 (`inventory.count`) | Fecha (exige divergências tratadas ou justificadas — IV-BR-102); `200` com sumário + acuracidade; publica EVT-IV-014 |
| 22 | `POST` | `/api/v1/inventory/counts/{id}/cancel` | UC-IV-007 | IV-PERM-006 (`inventory.count`) | Cancela; corpo `{ reason }`; lançamentos preservados; `200` |
| 23 | `GET` | `/api/v1/inventory/counts?cursor=&status=` | UC-IV-007 | IV-PERM-009 (`inventory.read`) | Lista de inventários |

### 4.6 Locais de armazenagem

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 24 | `POST` | `/api/v1/inventory/locations` | UC-IV-009 | IV-PERM-008 (`inventory.locations`) | Cadastra local (tipo + pai conforme hierarquia); `201` |
| 25 | `PATCH` | `/api/v1/inventory/locations/{id}` | UC-IV-009 | IV-PERM-008 (`inventory.locations`) | Edita descrição/hierarquia; `If-Match`; valida ciclos (IV-ERR-062) |
| 26 | `POST` | `/api/v1/inventory/locations/{id}/inactivate` | UC-IV-009 | IV-PERM-008 (`inventory.locations`) | Inativa (somente sem saldo e sem reservas — IV-BR-063); `200` ou `422 IV-ERR-063` |
| 27 | `GET` | `/api/v1/inventory/locations?type=&active=` | UC-IV-009 | IV-PERM-009 (`inventory.read`) | Árvore de locais da empresa |

### 4.7 Posição, extrato e alertas

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 28 | `GET` | `/api/v1/inventory/balances?cursor=&itemId=&locationId=&sizeCode=&clientId=&contractId=` | UC-IV-011 | IV-PERM-009 (`inventory.read`) | Posição por chave (físico/reservado/disponível); cache Redis com fallback; segregação recortada por ABAC-IV-03 |
| 29 | `GET` | `/api/v1/inventory/balances/{itemId}/statement?cursor=&locationId=&from=&to=` | UC-IV-011 | IV-PERM-009 (`inventory.read`) | Extrato do item com saldos anterior/posterior e trilha até o documento de origem |
| 30 | `GET` | `/api/v1/inventory/alerts?cursor=&type=&status=&itemId=&locationId=` | UC-IV-010 | IV-PERM-009 (`inventory.read`) | Fila de alertas (mínimo/ruptura) com posição atual |
| 31 | `POST` | `/api/v1/inventory/alerts/{id}/acknowledge` | UC-IV-010 | IV-PERM-010 (`inventory.read` + tratamento) | Registra ciência (comentário opcional); alerta permanece `OPEN` até normalização de saldo; `200` |

### 4.8 Mapeamento de eventos publicados

| Endpoint | Eventos (MMS-004-05) | Efeitos colaterais |
|---|---|---|
| `POST …/movements/{id}/confirm` (entrada) | EVT-IV-001 — crítico | Saldo efetivado; alertas reavaliados/normalizados; MMS-003 retoma pendências; timeline + auditoria com saldos |
| `POST …/movements/{id}/confirm` (saída) / `POST …/reservations/{id}/fulfill` | EVT-IV-002 (+ EVT-IV-006 na baixa total) — críticos | Saldo baixado; reserva baixada; MMS-003 confirma entrega; EVT-IV-015/016 condicionais |
| `POST …/transfers` | EVT-IV-003 | Efeito atômico origem+destino; alertas em ambos os locais |
| `POST …/movements/{id}/reverse` | EVT-IV-004 — crítico | Efeito inverso; original → `REVERSED`; alertas reavaliados |
| `POST …/reservations` | EVT-IV-005 — crítico | Disponível bloqueado; MMS-003 inicia separação; POL-IV-04 reavalia alertas |
| `POST …/reservations/{id}/release` | EVT-IV-007 | Saldo devolvido ao disponível; MMS-003 reavalia |
| Job TMR-IV-001 (sem endpoint) | EVT-IV-008 — crítico | Vencimento; notificação POL-IV-05; MMS-003 reabre necessidade |
| `POST …/adjustments` | EVT-IV-009 | Notificação aos aprovadores (IV-NOT-005); TMR-IV-003 armado |
| `POST …/adjustments/{id}/approve` | EVT-IV-010 — crítico | Efeito no saldo; notificação ao registrador; alertas reavaliados |
| `POST …/adjustments/{id}/reject` | EVT-IV-011 | Sem efeito; notificação ao registrador |
| `POST …/counts` | EVT-IV-012 | Lista de contagem materializada; notificação ao escopo (IV-NOT-007) |
| `POST …/counts/{id}/entries` | EVT-IV-013 | Snapshot registrado; sem efeito de saldo |
| `POST …/counts/{id}/close` | EVT-IV-014 | Sumário + acuracidade (KPI ≥ 98%); ajustes vinculados via fluxo E |
| Todos os acima | — | Auditoria obrigatória (FD-001-06) com `correlationId` ponta a ponta; saldo anterior/posterior quando há efeito (IV-BR-095) |

---

## 5. Erros do Módulo

Catálogo consolidado (detalhes e mensagens em MMS-004-02/MMS-004-07; `Accept-Language` se aplica):

| Código | HTTP | Situação |
|---|---|---|
| `IV-ERR-900` | 403 | Sem permissão para a ação (RBAC/ABAC/delegação negou) |
| `IV-ERR-404` | 404 | Recurso inexistente ou fora do escopo (anti-enumeração/anti-IDOR) |
| `IV-ERR-400` | 400 | Cursor de paginação inválido ou expirado |
| `IV-ERR-429` | 429 | Rate limit excedido |
| `IV-ERR-409` | 409 | Conflito de versão (`If-Match` divergente) ou saldo em movimentação concorrente (retry orientado) |
| `IV-ERR-090` | 409 | Estado não permite a operação (transição inválida — MMS-004-03) ou operação bloqueada por configuração |
| `IV-ERR-010` | 422 | Item inativo no catálogo para nova operação (MMS-RG-08) |
| `IV-ERR-020` | 422 | Saldo disponível insuficiente (disponível × solicitado por linha — MMS-RG-04/09) |
| `IV-ERR-030` | 422 | Reserva inexistente, vencida ou não Ativa no momento da confirmação |
| `IV-ERR-034` | 422 | Quantidade entregue excede o saldo da reserva |
| `IV-ERR-050` | 400 | Transferência com origem igual ao destino |
| `IV-ERR-060` | 422 | Local inválido, inativo, de outra empresa ou incompatível com a granularidade |
| `IV-ERR-061` | 409 | Código de local duplicado na empresa |
| `IV-ERR-062` | 422 | Hierarquia de local inválida (ciclo ou tipo incompatível com o pai) |
| `IV-ERR-063` | 422 | Inativação de local com saldo ou reservas ativas |
| `IV-ERR-070` | 422 | Violação de segregação cliente/contrato (MMS-RG-10; sem vazamento de disponibilidade) |
| `IV-ERR-084` | 422 | Saldo insuficiente para ajuste negativo (revalidado na aprovação) |
| `IV-ERR-085` | 403 | Violação de SoD (aprovador = registrador; estornante = aprovador) — auditoria + alerta de segurança |
| `IV-ERR-086` | 400 | Rejeição de ajuste sem motivo |
| `IV-ERR-100` | 422 | Lançamento de contagem fora do escopo do inventário |
| `IV-ERR-102` | 422 | Fechamento de inventário com divergência sem ajuste concluído nem justificativa |
| `IV-ERR-110` | 409 | Estorno inviável: documento não Confirmado ou já estornado |
| `IV-ERR-112` | 400 | Motivo do estorno ausente ou insuficiente (mín. 10 caracteres) |
| `IV-ERR-113` | 422 | Efeito inverso do estorno geraria saldo negativo |
| `IV-ERR-120` | 422 | Tamanho ausente ou fora da grade para item com grade (EPI/Fardamento) |
| `IV-ERR-121` | 422 | Tamanho da entrega difere do tamanho reservado (troca exige nova reserva) |

---

## 6. Segurança (resumo operacional)

1. **Anti-enumeração:** ausência de escopo ⇒ `404` (nunca `403`); isolamento multi-tenant por `company_id` em todas as consultas (MMS-004-11); saldo segregado de outro contrato é tratado como inexistente (ABAC-IV-03).
2. **SoD server-side:** aprovação e estorno de ajuste comparam a identidade da pessoa (não o papel) contra `registeredBy`/`approvedBy` (IV-BR-085/114; DEL-IV-004); violação gera `403 IV-ERR-085`, auditoria e alerta de segurança.
3. **Visão do almoxarifado:** filtros com dados de solicitante (endpoint 6/10) exclusivos de Operator/Supervisor/Manager/Admin/Auditor (ABAC-IV-02); dados de consumo por colaborador recortados por perfil (ABAC-IV-04 — LGPD).
4. **Saldo inatacável:** não existe endpoint de escrita sobre `balances`; toda mutação passa por documento com validação e auditoria (INV-IV-01) — payloads bindados por allowlist; campos calculados (`status`, `number`, `balanceBefore/After`, `version`, timestamps) são somente leitura.
5. **Integrações:** credenciais de serviço (MMS-003/MMS-005) com scopes mínimos (`inventory.write` + `inventory.reserve`) restritos ao escopo da integração (least privilege).
6. **Exportação de extrato:** auditada (LGPD), com recorte de campos sensíveis por perfil.
7. **CSRF:** API stateless com Bearer — sem cookies de sessão; CSRF não se aplica (SEC-003).
8. **Auditoria:** toda mutação trilhada (FD-001-06) com saldo anterior/posterior quando há efeito; `correlationId` ponta a ponta até o outbox de eventos.

---

## 7. Versionamento e Compatibilidade

- **Aditivo permitido em v1:** novos campos opcionais em respostas, novos filtros, novos endpoints.
- **Proibido em v1:** remover/renomear campos, mudar tipos, alterar semântica de códigos de erro, mudar ordenação padrão.
- **Depreciação:** campo/endpoint marcado `deprecated: true` na OpenAPI por no mínimo 2 releases antes da remoção (que ocorre apenas em `v2`).
- **Contrato de eventos** segue MMS-004-05 (versionamento `v1` por evento, consumo tolerante, idempotência `(eventId, consumerName)`, ordenação por agregado e por chave de saldo).

---

## 8. Requisitos Não Funcionais da API

| Categoria | Requisito |
|---|---|
| **Desempenho** | Validação de disponibilidade e posição P95 ≤ 500 ms (NFR do módulo: < 2s ponta a ponta); confirmação de documento P95 ≤ 1s; extrato P95 ≤ 300 ms (índices MMS-004-11 §15.10). |
| **Concorrência** | Confirmações sobre a mesma chave de saldo serializadas (lock por linha da projeção); conflito responde `409 IV-ERR-409` com orientação de retry. |
| **Disponibilidade** | Alertas nunca bloqueiam a confirmação (IV-BR-092); ações de efeito fail-closed em indisponibilidade de auditoria (IV-BR-095). |
| **Observabilidade** | Métricas por endpoint (latência, taxa de erro, 403/404 ratio, lock waits), tracing por `correlationId`. |
| **Rate limit** | Conforme §2.8; resposta com `Retry-After`. |
| **Documentação** | OpenAPI 3.1 publicada no portal de desenvolvedores a cada release; exemplos pt-BR. |

---

## 9. Critérios de Conclusão

- [ ] Todos os 31 endpoints implementados conforme este catálogo, com testes de contrato (OpenAPI diff limpo).
- [ ] Fluxo de autorização escopo(404)→RBAC→ABAC→delegação verificado em testes por endpoint, incluindo SoD (IV-ERR-085) e negação do Requester.
- [ ] Prova de integridade: nenhum caminho da API altera `stock_balance` sem documento confirmado (verificação de privilégios + testes negativos).
- [ ] Keyset pagination com cursor assinado em todas as coleções (`IV-ERR-400` testado).
- [ ] Idempotency-Key + idempotência funcional de origem (FA-IV-005) testadas nos endpoints de escrita.
- [ ] Catálogo de erros IV-ERR implementado e localizado (pt-BR/en-US).
- [ ] Eventos publicados conforme §4.8 com outbox e idempotência de consumidores.
- [ ] Testes relacionados TC-IV-xxx-y (MMS-004-07) verdes na suíte de API.

---

## 10. Roadmap

| Versão | Escopo |
|---|---|
| **v1.0 (MVP)** | Este catálogo (31 endpoints), erros, segurança, keyset, idempotência, OpenAPI 3.1. |
| **v1.1** | Transferência com recebimento em trânsito (`POST /transfers/{id}/receive`), abertura automática de inventário cíclico (agenda ABC), exportação CSV do extrato (`GET /balances/{itemId}/statement/export.csv`), evento `LocationChanged`, relatórios de posição/curva ABC/acuracidade. |
| **v2.0** | Controle por lote/validade e número de série (payloads estendidos), quarentena (com MMS-005 v2.0), sugestão de rebalanceamento (`GET /rebalance-suggestions`), reposição automática por ponto de pedido (integração PR-001), operação por código de barras (endpoints mobile). |

---

## 11. Histórico de Versão

| Versão | Data | Descrição | Autor |
|---|---|---|---|
| 1.0.0 | 2026-08-23 | Versão inicial aprovada: convenções (envelope, keyset, idempotência dupla — chave de cliente + origem funcional, autorização com SoD, headers, HTTP, rate limit com classe M2M), modelos de recursos (StockMovement com saldos por linha, Reservation, Adjustment, InventoryCount, StockBalanceView somente leitura, StockAlertView, StatementEntryView), catálogo de 31 endpoints mapeados a UC/permissão/evento (sem endpoint de escrita de saldo nem de vencimento de reserva), catálogo de erros IV-ERR → HTTP, segurança operacional, versionamento, NFRs, critérios de conclusão (incluindo prova de integridade) e roadmap. | Arquitetura Trino |
