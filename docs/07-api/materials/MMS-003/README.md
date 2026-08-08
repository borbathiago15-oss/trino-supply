# MMS-003-13 — API do Material Requisition

| Campo | Valor |
|---|---|
| **Documento** | MMS-003-13 |
| **Módulo** | Materials — Material Requisition (MMS-003) |
| **Versão** | 1.0.0 |
| **Status** | 🟢 Approved |
| **Data** | 2026-08-08 |
| **Dependências** | MMS-003 v1.1.0, MMS-003-02..05, MMS-003-11 (Database), ADR-009, ADR-010, ADR-013, FD-001-01/03/04/05/06/07, SEC-001/003, MMS-002-13 / MMS-004-13 (padrão) |
| **Escopo** | API REST pública `v1` do módulo Material Requisition |

---

## 1. Objetivo
Especificação oficial da **API REST** do Material Requisition, consolidando os endpoints dos casos de uso (MMS-003-07) em contrato único. Descreve; as regras ficam em MMS-003-02, transições em MMS-003-03, permissões em MMS-003-09, eventos em MMS-003-05. **Nenhum endpoint movimenta saldo** (MR-BR-050) — reserva/entrega são do MMS-004.

## 2. Convenções
- Base `/api/v1/material-requisitions`; versionamento por path; OpenAPI 3.1.
- Envelope, keyset, idempotência, headers (`Authorization`, `X-Correlation-Id`, `Idempotency-Key`, `If-Match`, `Accept-Language`) e códigos HTTP conforme padrão da suíte (MMS-002-13 §2).
- Scopes: `requisitions.read`, `requisitions.write`, `requisitions.approve`, `requisitions.warehouse`, `requisitions.admin` (MMS-003-09).
- Autorização por request: escopo (404) → RBAC → ABAC → delegação (403); deny by default; SoD (solicitante ≠ aprovador — MR-BR-032).
- Rate limit: leitura 600/min, escrita 120/min, aprovação 60/min (por usuário).

## 3. Modelo de Recursos

### 3.1 `MaterialRequisition`
```json
{
  "id": "uuid", "number": "MR-2026-000123",
  "requesterId": "uuid", "costCenterId": "uuid", "deliveryLocationId": "uuid",
  "justification": "…", "reason": { "typeCode": "REQ_REASON", "code": "DAMAGED" },
  "neededDate": "2026-08-20",
  "status": "DRAFT | SUBMITTED | IN_APPROVAL | APPROVED | IN_FULFILMENT | AWAITING_PURCHASE | COMPLETED | REJECTED | CANCELLED",
  "items": [
    { "id": "uuid", "itemId": "uuid", "sizeCode": "M | null",
      "requestedQty": 5, "approvedQty": 5,
      "itemStatus": "PENDING | RESERVED | PICKED | DELIVERED | IN_PURCHASE | RECEIVED | REJECTED",
      "route": "STOCK | PURCHASE | null",
      "reservationRef": "uuid | null", "purchaseRef": "PR-2026-000045 | null",
      "decisionReason": "string | null" }
  ],
  "attachments": [ { "id": "uuid", "documentId": "uuid", "kind": "EVIDENCE" } ],
  "approvalDecision": { "approverId": "uuid", "decidedAt": "…", "remarks": "…" },
  "version": 3, "createdAt": "…"
}
```
- `requestedQty` na **unidade base** do item (ADR-013); conversão só no estoque/compra.
- `route` definida após a validação de estoque (MR-BR-042); `purchaseRef` sustenta a rastreabilidade bidirecional (MR-BR-043).

### 3.2 `DeliveryLocation`
```json
{ "id": "uuid", "code": "DL-000007", "description": "Almox. Central - Doca 2", "orgUnitId": "uuid", "active": true, "version": 1 }
```
- `code` **gerado pelo sistema** (MR-BR-060); somente leitura.

## 4. Catálogo de Endpoints

### 4.1 Solicitação (ciclo de vida)
| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 1 | `POST` | `/api/v1/material-requisitions` | UC-MR-001 | requisitions.write | Cria em `DRAFT`; `201`; publica EVT-MR-001 |
| 2 | `GET` | `/api/v1/material-requisitions?cursor=&status=&mine=true` | UC-MR-005 | requisitions.read | Lista as próprias solicitações (keyset) |
| 3 | `GET` | `/api/v1/material-requisitions/{id}` | UC-MR-005 | requisitions.read | Detalhe consolidado (itens + rotas + status) |
| 4 | `PATCH` | `/api/v1/material-requisitions/{id}` | UC-MR-001 | requisitions.write | Edita rascunho; `If-Match`; publica EVT-MR (sem número) |
| 5 | `POST` | `/api/v1/material-requisitions/{id}/items` | UC-MR-001 | requisitions.write | Adiciona item (Ativo; tamanho por grade) |
| 6 | `DELETE` | `/api/v1/material-requisitions/{id}/items/{itemId}` | UC-MR-001 | requisitions.write | Remove item (só em Rascunho) |
| 7 | `POST` | `/api/v1/material-requisitions/{id}/attachments` | UC-MR-001 | requisitions.write | Anexa evidência (FD-001-03); exigível por motivo (MR-BR-013) |
| 8 | `POST` | `/api/v1/material-requisitions/{id}/submit` | UC-MR-002 | requisitions.write | Submete (MR-BR-021); publica EVT-MR-002 |
| 9 | `POST` | `/api/v1/material-requisitions/{id}/cancel` | UC-MR-006 | requisitions.write | Cancela nos estados permitidos; libera reservas; EVT-MR-008 |
| 10 | `POST` | `/api/v1/material-requisitions/{id}/confirm-receipt` | UC-MR-005 | requisitions.write | Confirma recebimento; conclui (MR-BR-052); EVT-MR-011 |
| 11 | `GET` | `/api/v1/material-requisitions/{id}/timeline?cursor=` | UC-MR-005 | requisitions.read | Timeline (inclui vínculos PR-001) |

### 4.2 Aprovação
| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 12 | `GET` | `/api/v1/material-requisitions/approvals?cursor=` | UC-MR-003 | requisitions.approve | Fila de aprovações do usuário |
| 13 | `POST` | `/api/v1/material-requisitions/{id}/approve` | UC-MR-003 | requisitions.approve | Aprova total/parcial (ajustes por item — MR-BR-031); SoD (MR-BR-032); EVT-MR-004/005 |
| 14 | `POST` | `/api/v1/material-requisitions/{id}/reject` | UC-MR-003 | requisitions.approve | Recusa integral com parecer; EVT-MR-006 |
| 15 | `POST` | `/api/v1/material-requisitions/{id}/return` | UC-MR-003 | requisitions.approve | Devolve ao solicitante; EVT-MR-007 |

### 4.3 Visão do almoxarifado
| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 16 | `GET` | `/api/v1/material-requisitions/warehouse-queue?cursor=&requesterId=&from=&to=&status=&costCenterId=&category=&number=` | UC-MR-008 | requisitions.warehouse | Fila de solicitações aprovadas (MR-BR-070) — exclusiva do almoxarifado |

### 4.4 Locais de entrega
| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 17 | `GET` | `/api/v1/material-requisitions/delivery-locations?active=true&cursor=` | UC-MR-007 | requisitions.read | Lista locais ativos |
| 18 | `POST` | `/api/v1/material-requisitions/delivery-locations` | UC-MR-007 | requisitions.admin | Cria local (código gerado — MR-BR-060) |
| 19 | `PATCH` | `/api/v1/material-requisitions/delivery-locations/{id}` | UC-MR-007 | requisitions.admin | Edita/inativa (exige zero uso em aberto) |

### 4.5 Eventos
Transições publicam EVT-MR-001..012 (MMS-003-05) via Outbox na mesma transação; auditoria obrigatória (FD-001-06) com `correlationId`. Validação de estoque (EVT-MR-009) e roteamento (EVT-MR-010/012) são reações automáticas pós-aprovação, sem endpoint de comando.

## 5. Erros do Módulo

| Código | HTTP | Situação |
|---|---|---|
| `MR-ERR-100` | 403 | Sem permissão |
| `MR-ERR-404` | 404 | Inexistente ou fora do escopo |
| `MR-ERR-409` | 409 | Conflito de versão (`If-Match`) |
| `MR-ERR-002` | 403 | Operação fora do escopo organizacional |
| `MR-ERR-003` | 400 | Justificativa ausente |
| `MR-ERR-004` | 422 | Motivo obrigatório/ inválido |
| `MR-ERR-010` | 422 | Item inativo/inexistente |
| `MR-ERR-011` | 400 | Quantidade inválida |
| `MR-ERR-012` | 422 | Tamanho ausente/inválido para item com grade |
| `MR-ERR-013` | 422 | Motivo exige anexo |
| `MR-ERR-021` | 422 | Solicitação incompleta para submissão |
| `MR-ERR-022` | 409 | Cancelamento não permitido no estado |
| `MR-ERR-031` | 422 | Ação de aprovação inválida por item |
| `MR-ERR-032` | 422 | Solicitante não pode aprovar a própria solicitação |
| `MR-ERR-052` | 422 | Há itens pendentes (conclusão bloqueada) |
| `MR-ERR-060` | 422 | Local de entrega inválido/em uso |
| `MR-ERR-070` | 403 | Sem acesso à visão do almoxarifado |

## 6. Segurança
Anti-enumeração (404 fora de escopo); isolamento por `company_id`; SoD server-side (MR-BR-032); allowlist de campos (status/version/route/refs somente leitura); anexos via FD-001-03 (whitelist de content-type, URL assinada ≤ 5 min); auditoria de toda decisão (FD-001-06).

## 7. NFRs
Listagens paginadas keyset; validação de estoque assíncrona para solicitações com muitos itens; demanda de compra nunca perdida (outbox na mesma transação do roteamento); alertas nunca bloqueiam o fluxo.

## 8. Critérios de Conclusão
- [ ] 19 endpoints com testes de contrato (OpenAPI diff limpo).
- [ ] Escopo(404)→RBAC→ABAC→delegação por endpoint; SoD testada.
- [ ] Rastreabilidade bidirecional (purchaseRef) verificável na timeline.
- [ ] Keyset em todas as coleções; catálogo MR-ERR localizado.
- [ ] Eventos EVT-MR-001..012 via outbox com idempotência de consumidores.

## 9. Roadmap
| Versão | Escopo |
|---|---|
| v1.0 (MVP) | Este catálogo (19 endpoints), aprovação parcial, roteamento por item, visão do almoxarifado, locais de entrega. |
| v1.1 | Templates/solicitações recorrentes; sugestão de quantidade por histórico; exportações/relatório de atendimento. |
| v2.0 | Reposição automática gerando solicitação; aprovação mobile; previsão de demanda (IA). |

## 10. Histórico de Versão
| Versão | Data | Descrição | Autor |
|---|---|---|---|
| 1.0.0 | 2026-08-08 | Versão inicial aprovada: convenções, modelos de recursos (MaterialRequisition com itens/rotas/refs, DeliveryLocation), catálogo de 19 endpoints `/api/v1/material-requisitions` (ciclo de vida, aprovação com SoD e aprovação parcial, visão do almoxarifado, locais de entrega) mapeados a UC-MR/permissão/evento, catálogo MR-ERR, segurança, NFRs, critérios de conclusão e roadmap — padrão MMS-002-13/MMS-004-13. | Arquitetura Trino |
