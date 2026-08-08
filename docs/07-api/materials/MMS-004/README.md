# MMS-004-13 — API do Inventory Management

| Campo | Valor |
|---|---|
| **Documento** | MMS-004-13 |
| **Módulo** | Materials — Inventory Management (MMS-004) |
| **Versão** | 1.0.0 |
| **Status** | 🟢 Approved |
| **Data** | 2026-08-08 |
| **Dependências** | MMS-004 (Visão) v1.1.0, MMS-004-02 (Business Rules) v1.1.0, MMS-004-03 (State Machine) v1.1.0, MMS-004-04 (Domain Model) v1.1.0, MMS-004-05 (Event Storming) v1.1.0, MMS-004-07 (Use Cases) v1.1.0, MMS-004-09 (Permissions), MMS-004-11 (Database) v1.0.0, ADR-009, ADR-010, ADR-013 (Conversão de UoM), ADR-014 (Motor de Regras de Reposição), FD-001-01, FD-001-05, FD-001-06, FD-001-07, SEC-001/003 |
| **Escopo** | API REST pública `v1` do módulo Inventory Management |

---

## 1. Objetivo

Especificação oficial da **API REST do módulo Inventory Management**. Consolida os endpoints referenciados pelos casos de uso (MMS-004-07) em um contrato único, com convenções, modelos de recursos, códigos de status, erros, segurança e versionamento.

**Regra-mestre:** este documento descreve; as regras permanecem em MMS-004-02, as transições em MMS-004-03, as permissões em MMS-004-09 e os eventos em MMS-004-05. Em conflito, prevalecem os documentos de negócio. **Nenhum endpoint escreve saldo diretamente** — o saldo é projeção derivada de documentos confirmados (ADR-009 / IV-BR-001).

---

## 2. Convenções Gerais

### 2.1 Base URL e versionamento
- Base: `/api/v1/inventory`; versionamento por path (`v1`); quebra exige `v2`.
- Contrato formal em OpenAPI 3.1 gerado a partir deste documento.

### 2.2 Envelope de resposta
Idêntico ao padrão da suíte (MMS-002-13 §2.2): `{ data, correlationId }` para recurso único; `{ data, page: { nextCursor, hasMore, pageSize }, correlationId }` para coleções; erro `{ error: { code, message, details, correlationId, traceId } }`.

### 2.3 Paginação (keyset)
- Extrato/movimentações: `(created_at, id) DESC`; cursor assinado, validade 15 min; `pageSize` padrão 50, máx. 100.
- `OFFSET` proibido; cursor inválido/expirado ⇒ `400 IV-ERR-111`.

### 2.4 Idempotência
- Escritas aceitam `Idempotency-Key` (UUID por operação); replay em 24h retorna a resposta original. Fundamental em **confirmações de movimentação** (evita efeito duplicado sobre o saldo).

### 2.5 Autenticação e autorização
- Bearer JWT (15 min) + refresh rotativo (FD-001-01, SEC-003).
- Scopes: `inventory.read`, `inventory.write`, `inventory.lifecycle`, `inventory.approve`, `inventory.admin`, `inventory.audit` (MMS-004-09).
- Fluxo por request: **escopo (404) → RBAC → ABAC → delegação (403)** — deny by default; segregação de funções (quem ajusta não aprova — IV-BR-041).

### 2.6 Cabeçalhos
`Authorization` (obrigatório), `X-Correlation-Id`, `Idempotency-Key`, `If-Match` (recursos versionados; `409 IV-ERR-012` em conflito), `Accept-Language`.

### 2.7 Códigos HTTP
`200/201/204` sucesso; `400` validação; `401` token; `403` permissão (`IV-ERR-100`); `404` inexistente/fora de escopo (anti-enumeração — `IV-ERR-404`); `409` conflito de versão/estado (`IV-ERR-012`/estado); `422` regra de negócio (saldo insuficiente, segregação, etc.); `429` rate limit; `500` interno.

### 2.8 Rate limit
Leitura 600/min; escrita 120/min; ciclo de vida/confirmações 60/min — por usuário; ajustável via `materials.inventory.rate-limit.*`.

---

## 3. Modelo de Recursos (contratos)

### 3.1 `StockBalance` (projeção — somente leitura)

```json
{
  "itemId": "uuid",
  "sizeCode": "M | null",
  "locationId": "uuid",
  "segregationKey": "cliente:contrato | null",
  "totalQty": 120,
  "reservedQty": 30,
  "availableQty": 90,
  "avgCostReference": 12.50,
  "lastMovementId": "uuid",
  "lastMovementAt": "2026-08-08T12:00:00Z"
}
```
- `availableQty = totalQty − reservedQty` (INV-IV-05); nunca editável por API (ADR-009).

### 3.2 `StockMovement`

```json
{
  "id": "uuid",
  "movementType": "ENTRY | ISSUE | TRANSFER | RESERVATION_RELEASE | REVERSAL",
  "originType": "RECEIVING | RETURN | FULFILLMENT | CONSUMPTION | ADJUSTMENT | INITIAL_LOAD | OPERATION",
  "originReference": "string",
  "status": "DRAFT | CONFIRMED | REVERSED | CANCELLED",
  "reason": "string | null",
  "reversesId": "uuid | null",
  "lines": [
    {
      "itemId": "uuid", "sizeCode": "M | null",
      "quantity": 10,
      "capturedQuantity": 1, "capturedUom": "CX", "appliedFactor": 12,
      "fromLocationId": "uuid | null", "toLocationId": "uuid | null",
      "segregationKey": "string | null"
    }
  ],
  "version": 1, "createdAt": "…", "confirmedAt": "… | null"
}
```
- `quantity` sempre na **unidade base** (IV-BR-009); quando a linha é capturada em unidade alternativa, `capturedQuantity`/`capturedUom`/`appliedFactor` registram a conversão (ADR-013).

### 3.3 `Reservation`

```json
{
  "id": "uuid", "requisitionRef": "string", "itemId": "uuid", "sizeCode": "M | null",
  "quantity": 10, "fulfilledQuantity": 0, "locationId": "uuid", "segregationKey": "string | null",
  "status": "ACTIVE | FULFILLED | RELEASED | EXPIRED", "expiresAt": "…", "version": 1
}
```

### 3.4 `Adjustment`

```json
{
  "id": "uuid", "adjustmentReason": "INVENTORY_DIVERGENCE | LOSS | DAMAGE | FOUND | ENTRY_ERROR",
  "justification": "string", "status": "PENDING | APPROVED | REJECTED | REVERSED",
  "inventoryId": "uuid | null", "registeredBy": "uuid", "approverId": "uuid | null",
  "lines": [ { "itemId": "uuid", "sizeCode": "M | null", "quantityDelta": -5, "locationId": "uuid" } ],
  "version": 1
}
```

### 3.5 `InventoryCount`

```json
{
  "id": "uuid", "scopeType": "ABC | GENERAL", "scopeDetail": { "classes": ["A"], "locationIds": ["uuid"] },
  "status": "OPEN | COUNTING | CLOSED | CANCELLED", "responsibleId": "uuid", "deadline": "…",
  "summary": { "counted": 100, "divergent": 8, "adjusted": 6, "withinTolerance": 2 }, "version": 1
}
```

### 3.6 `ReplenishmentSuggestion` (ADR-014)

```json
{
  "id": "uuid", "itemId": "uuid", "locationId": "uuid",
  "triggeredAvailable": 8, "reorderPoint": 10,
  "policy": "ATE_MAXIMO | MULTIPLO_EMBALAGEM | LOTE_FIXO", "suggestedQuantity": 92,
  "route": "TRANSFERENCIA | COMPRA | MANUAL",
  "status": "SUGGESTED | CONFIRMED | DISCARDED", "outcomeReference": "string | null", "version": 1
}
```

### 3.7 `Location`

```json
{ "id": "uuid", "locationType": "WAREHOUSE | DEPOSIT | ADDRESS", "code": "…", "description": "…", "parentId": "uuid | null", "active": true, "version": 1 }
```

---

## 4. Catálogo de Endpoints

### 4.1 Movimentações (entrada, saída, transferência, estorno)

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 1 | `POST` | `/api/v1/inventory/movements` | UC-IV-001/002 | IV-PERM-001/002 (`inventory.write`) | Cria documento em `DRAFT` (entrada/saída); linhas na unidade base ou capturadas (ADR-013) |
| 2 | `POST` | `/api/v1/inventory/movements/{id}/confirm` | UC-IV-001/002 | IV-PERM-002 (`inventory.lifecycle`) | **Confirma**: aplica efeito no saldo (serializado por chave); publica EVT-IV-001/002; `Idempotency-Key` |
| 3 | `POST` | `/api/v1/inventory/movements/{id}/cancel` | UC-IV-001 | IV-PERM-002 | Cancela rascunho; `204` |
| 4 | `POST` | `/api/v1/inventory/movements/{id}/reverse` | UC-IV-008 | IV-PERM-007 (`inventory.lifecycle`) | Estorna documento confirmado (IV-BR-003); publica EVT-IV-004 |
| 5 | `POST` | `/api/v1/inventory/transfers` | UC-IV-005 | IV-PERM-001/002 | Transferência atômica origem→destino (IV-BR-030/031); publica EVT-IV-003 |
| 6 | `GET` | `/api/v1/inventory/movements?cursor=&itemId=&originType=&status=` | UC-IV-011 | IV-PERM-009 (`inventory.read`) | Extrato paginado (keyset) |
| 7 | `GET` | `/api/v1/inventory/movements/{id}` | UC-IV-011 | IV-PERM-009 | Detalhe do documento (auditoria com saldo anterior/posterior) |

### 4.2 Reservas

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 8 | `POST` | `/api/v1/inventory/reservations` | UC-IV-003 | IV-PERM-003 (`inventory.write`) | Cria reserva sobre o **disponível** (IV-BR-005/020); publica EVT-IV-005 |
| 9 | `POST` | `/api/v1/inventory/reservations/{id}/fulfill` | UC-IV-002 | IV-PERM-002 | Baixa da reserva na saída por atendimento (IV-BR-023/121); publica EVT-IV-006 |
| 10 | `POST` | `/api/v1/inventory/reservations/{id}/release` | UC-IV-004 | IV-PERM-003 | Libera reserva ativa (IV-BR-022); publica EVT-IV-007 |
| 11 | `GET` | `/api/v1/inventory/reservations?expiring=true&cursor=` | UC-IV-004/010 | IV-PERM-009 | Lista reservas (com filtro de pré-vencimento — IV-BR-082) |

> `ExpireReservation` não é endpoint: é job do sistema (IV-BR-021) que emite documento de liberação e publica EVT-IV-008.

### 4.3 Ajustes

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 12 | `POST` | `/api/v1/inventory/adjustments` | UC-IV-006 | IV-PERM-004 (`inventory.write`) | Registra ajuste com justificativa (IV-BR-040); publica EVT-IV-009 |
| 13 | `POST` | `/api/v1/inventory/adjustments/{id}/approve` | UC-IV-006 | IV-PERM-005 (`inventory.approve`) | Aprova (≠ registrante — IV-BR-041) e confirma efeito; publica EVT-IV-010 |
| 14 | `POST` | `/api/v1/inventory/adjustments/{id}/reject` | UC-IV-006 | IV-PERM-005 | Rejeita com motivo; publica EVT-IV-011 |
| 15 | `GET` | `/api/v1/inventory/adjustments?status=&cursor=` | UC-IV-006 | IV-PERM-009 | Fila de ajustes (pendentes de aprovação) |

### 4.4 Inventário

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 16 | `POST` | `/api/v1/inventory/counts` | UC-IV-007 | IV-PERM-006 (`inventory.lifecycle`) | Abre inventário (ABC/geral — IV-BR-070); publica EVT-IV-012 |
| 17 | `POST` | `/api/v1/inventory/counts/{id}/entries` | UC-IV-007 | IV-PERM-001 | Registra contagem com snapshot (IV-BR-071/073); publica EVT-IV-013 |
| 18 | `GET` | `/api/v1/inventory/counts/{id}/divergences` | UC-IV-007 | IV-PERM-006 | Divergências acima da tolerância |
| 19 | `POST` | `/api/v1/inventory/counts/{id}/close` | UC-IV-007 | IV-PERM-006 | Fecha gerando propostas de ajuste (IV-BR-072); publica EVT-IV-014 |
| 20 | `POST` | `/api/v1/inventory/counts/{id}/cancel` | UC-IV-007 | IV-PERM-006 | Cancela inventário sem contagens |

### 4.5 Locais

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 21 | `POST` | `/api/v1/inventory/locations` | UC-IV-009 | IV-PERM-008 (`inventory.admin`) | Cria local (hierarquia — IV-BR-050) |
| 22 | `PATCH` | `/api/v1/inventory/locations/{id}` | UC-IV-009 | IV-PERM-008 | Edita local; `If-Match` |
| 23 | `POST` | `/api/v1/inventory/locations/{id}/inactivate` | UC-IV-009 | IV-PERM-008 | Inativa (exige saldo zero — IV-BR-052) |
| 24 | `GET` | `/api/v1/inventory/locations?type=&cursor=` | UC-IV-009 | IV-PERM-009 | Lista/estrutura de locais |

### 4.6 Posição, alertas e reposição

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 25 | `GET` | `/api/v1/inventory/balances?itemId=&locationId=&cursor=` | UC-IV-011 | IV-PERM-009 | Posição (projeção `stock_balance`); disponível para validação do MMS-003 |
| 26 | `GET` | `/api/v1/inventory/balances/{itemId}/statement?cursor=` | UC-IV-011 | IV-PERM-009 | Extrato de movimentações do item (keyset) |
| 27 | `GET` | `/api/v1/inventory/alerts?type=&cursor=` | UC-IV-010 | IV-PERM-009 | Alertas de mínimo/ruptura/reserva a vencer |
| 28 | `POST` | `/api/v1/inventory/alerts/{id}/acknowledge` | UC-IV-010 | IV-PERM-010 | Reconhece o alerta |
| 29 | `GET` | `/api/v1/inventory/replenishment-suggestions?status=&itemId=&cursor=` | UC-IV-012 | IV-PERM-011 (`inventory.write`) | Fila de sugestões de reposição (ADR-014) |
| 30 | `POST` | `/api/v1/inventory/replenishment-suggestions/{id}/confirm` | UC-IV-012 | IV-PERM-011 | Confirma: dispara rota (compra→PR-001 / transferência); publica EVT-IV-018 |
| 31 | `POST` | `/api/v1/inventory/replenishment-suggestions/{id}/discard` | UC-IV-012 | IV-PERM-011 | Descarta; publica EVT-IV-019 |

### 4.7 Mapeamento de eventos

Confirmações e transições publicam os eventos EVT-IV-001..019 (MMS-004-05) via Outbox na mesma transação; auditoria obrigatória (FD-001-06) com saldo anterior/posterior e `correlationId` ponta a ponta. `GenerateReplenishmentSuggestion` (EVT-IV-017) e alertas (EVT-IV-015/016) são reações automáticas, sem endpoint de comando.

---

## 5. Erros do Módulo

| Código | HTTP | Situação |
|---|---|---|
| `IV-ERR-100` | 403 | Sem permissão (RBAC/ABAC/delegação) |
| `IV-ERR-404` | 404 | Recurso inexistente ou fora do escopo (anti-enumeração) |
| `IV-ERR-012` | 409 | Conflito de versão (`If-Match`) ou de confirmação sequencial |
| `IV-ERR-001` | 422 | Tentativa de alterar saldo diretamente (bloqueada — ADR-009) |
| `IV-ERR-003` | 409 | Documento confirmado imutável (use estorno) |
| `IV-ERR-004` | 422 | Saldo insuficiente (saldo negativo bloqueado) |
| `IV-ERR-005` | 422 | Disponível insuficiente (há reservas ativas) |
| `IV-ERR-009` | 400 | Quantidade/unidade inválida para o item (conversão) |
| `IV-ERR-041` | 422 | Aprovador = registrante (segregação de funções) |
| `IV-ERR-052` | 422 | Local com saldo não pode ser inativado |
| `IV-ERR-060` | 422 | Saldo dedicado a outro cliente/contrato (segregação) |
| `IV-ERR-111` | 400 | Cursor de paginação inválido/expirado |
| `IV-ERR-120` | 400 | Tamanho ausente/fora da grade do item |
| `IV-ERR-131` | 422 | Rota de reposição incompatível com a classificação do item |

---

## 6. Segurança (resumo)

1. **Anti-enumeração:** fora de escopo ⇒ `404`; isolamento multi-tenant por `company_id` em toda consulta.
2. **Saldo protegido:** nenhum endpoint escreve saldo; a projeção só é atualizada pelo `StockBalanceService` na confirmação (defesa em profundidade — MMS-004-11 §8).
3. **Segregação de funções:** confirmação/aprovação avaliadas server-side; quem registra ajuste nunca aprova (IV-BR-041).
4. **Idempotência** obrigatória nas confirmações (evita efeito duplo no saldo).
5. **Mass assignment:** allowlist por endpoint; `status`, `version`, `availableQty`, timestamps são somente leitura.
6. **Auditoria:** toda confirmação/aprovação/estorno trilhada (FD-001-06) com saldo anterior/posterior.

---

## 7. Versionamento e NFRs

- Aditivo em v1 (novos campos opcionais, filtros, endpoints); proibido remover/renomear campos ou mudar semântica de erros.
- **Desempenho:** validação/consulta de saldo p95 < 2 s (IV-BR-110); extrato paginado p95 < 500 ms.
- **Disponibilidade:** confirmações fail-closed em indisponibilidade de auditoria; alertas nunca bloqueiam a movimentação (IV-BR-083).
- **Observabilidade:** métricas por endpoint, tracing por `correlationId`, profundidade de fila de eventos.

---

## 8. Critérios de Conclusão

- [ ] 31 endpoints implementados com testes de contrato (OpenAPI diff limpo).
- [ ] Fluxo escopo(404)→RBAC→ABAC→delegação por endpoint.
- [ ] Confirmação serializada por chave de saldo + `Idempotency-Key` (sem efeito duplo).
- [ ] Nenhum caminho de escrita direta de saldo (prova de INV-IV-01).
- [ ] Keyset em todas as coleções; catálogo IV-ERR localizado (pt-BR/en-US).
- [ ] Eventos EVT-IV-001..019 via outbox com idempotência de consumidores.

---

## 9. Roadmap

| Versão | Escopo |
|---|---|
| **v1.0 (MVP)** | Este catálogo (31 endpoints), conversão de UoM na captura (ADR-013), fila de sugestões de reposição (ADR-014, revisável), erros, segurança, keyset, idempotência, OpenAPI 3.1. |
| **v1.1** | Transferência com recebimento em trânsito; inventário cíclico automático; relatórios (posição, curva ABC, acuracidade). |
| **v2.0** | Execução automática de reposição (`auto-execute`); lote/validade/série; webhooks de movimentação; busca full-text de extrato. |

---

## 10. Histórico de Versão

| Versão | Data | Descrição | Autor |
|---|---|---|---|
| 1.0.0 | 2026-08-08 | Versão inicial aprovada: convenções (envelope, keyset, idempotência, autorização), modelos de recursos (StockBalance projeção, StockMovement com captura de conversão, Reservation, Adjustment, InventoryCount, ReplenishmentSuggestion, Location), catálogo de 31 endpoints `/api/v1/inventory` mapeados a UC/permissão/evento (UC-IV-001..012), catálogo de erros IV-ERR, segurança operacional (saldo protegido, segregação de funções, idempotência de confirmação), versionamento, NFRs, critérios de conclusão e roadmap — incorpora ADR-013 (conversão de UoM) e ADR-014 (sugestões de reposição); padrão MMS-002-13. | Arquitetura Trino |
