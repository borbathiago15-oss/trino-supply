# MMS-005-13 — API do Receiving

| Campo | Valor |
|---|---|
| **Documento** | MMS-005-13 |
| **Módulo** | Materials — Receiving (MMS-005) |
| **Versão** | 1.0.0 |
| **Status** | 🟢 Approved |
| **Data** | 2026-08-08 |
| **Dependências** | MMS-005 v1.1.0, MMS-005-02/03/04, MMS-005-11, ADR-009/010/013, MMS-004 (entrada), PR-001 (origem), FD-001-01/03/04/05/06/07, SEC-001/003 |
| **Escopo** | API REST pública `v1` do módulo Receiving |

# 1. Objetivo
Contrato REST do Receiving. Descreve; regras em MMS-005-02, transições em MMS-005-03, permissões em MMS-005-09, eventos em MMS-005-05. **Nenhum endpoint escreve saldo** (RC-BR-020): a entrada é gerada no MMS-004 na conclusão.

# 2. Convenções
Base `/api/v1/receivings`; envelope/keyset/idempotência/headers/HTTP conforme padrão da suíte (MMS-002-13 §2). Scopes: `receiving.read`, `receiving.write`, `receiving.approve`, `receiving.admin`. Autorização escopo(404)→RBAC→ABAC→delegação; SoD (quem confere não aprova o próprio destino restrito). Idempotência obrigatória em `complete` (evita entrada duplicada).

# 3. Modelo de Recursos
### `Receiving`
```json
{
  "id": "uuid", "number": "RC-2026-000088",
  "originType": "PURCHASE_ORDER | TRANSFER | RETURN", "originReference": "PO-2026-000045",
  "warehouseId": "uuid",
  "status": "AWAITING | INSPECTING | COMPLETED | COMPLETED_WITH_DIVERGENCE | CANCELLED",
  "lines": [ { "itemId": "uuid", "sizeCode": "M|null", "expectedQty": 120,
              "receivedQty": 10, "capturedUom": "CX", "appliedFactor": 12, "baseReceivedQty": 120 } ],
  "divergences": [ { "type": "SHORTAGE | SURPLUS | DAMAGE", "qty": 5, "destination": "…", "approvalStatus": "PENDING|APPROVED|null" } ],
  "stockEntryMovementId": "uuid | null", "version": 2
}
```
- `receivedQty` pode ser em unidade de compra; `baseReceivedQty` é a convertida (ADR-013).

# 4. Endpoints

| # | Método | Endpoint | UC | Permissão | Descrição |
|---|---|---|---|---|---|
| 1 | `GET` | `/api/v1/receivings?cursor=&status=&originType=` | UC-RC-004 | receiving.read | Lista/fila de recebimentos (keyset) |
| 2 | `GET` | `/api/v1/receivings/{id}` | UC-RC-004 | receiving.read | Detalhe (linhas + divergências) |
| 3 | `POST` | `/api/v1/receivings` | UC-RC-001 | receiving.write | Cria recebimento contra documento de origem; `201`; EVT recebimento registrado |
| 4 | `POST` | `/api/v1/receivings/{id}/start` | UC-RC-002 | receiving.write | Inicia conferência (Aguardando→Em Conferência) |
| 5 | `POST` | `/api/v1/receivings/{id}/lines` | UC-RC-002 | receiving.write | Registra quantidade recebida (unidade de compra convertida — RC-BR-010) |
| 6 | `POST` | `/api/v1/receivings/{id}/divergences` | UC-RC-002 | receiving.write | Registra divergência com destino (RC-BR-011) |
| 7 | `POST` | `/api/v1/receivings/{id}/divergences/{divId}/approve` | UC-RC-002 | receiving.approve | Aprova destino de divergência (RC-BR-040) |
| 8 | `POST` | `/api/v1/receivings/{id}/complete` | UC-RC-003 | receiving.write | Conclui; gera **entrada no MMS-004** (RC-BR-020); compra dedicada reservada (RC-BR-021); `Idempotency-Key` |
| 9 | `POST` | `/api/v1/receivings/{id}/cancel` | UC-RC-001 | receiving.write | Cancela (antes de gerar entrada) |
| 10 | `GET` | `/api/v1/receivings/{id}/timeline?cursor=` | UC-RC-004 | receiving.read | Timeline do recebimento |

# 5. Erros
| Código | HTTP | Situação |
|---|---|---|
| `RC-ERR-100` | 403 | Sem permissão |
| `RC-ERR-404` | 404 | Inexistente/fora do escopo |
| `RC-ERR-001` | 422 | Documento de origem ausente/ inválido |
| `RC-ERR-010` | 422 | Item fora do documento de origem / quantidade inválida |
| `RC-ERR-011` | 422 | Divergência sem destino |
| `RC-ERR-012` | 422 | Divergência acima da tolerância sem tratativa |
| `RC-ERR-020` | 409 | Conclusão inválida (estado) / entrada já gerada |
| `RC-ERR-022` | 422 | Recebimento parcial não permitido |
| `RC-ERR-030` | 409 | Transição de estado inválida / conflito de versão |
| `RC-ERR-040` | 422 | Destino de divergência requer aprovação |

# 6. Segurança
Anti-enumeração (404); isolamento `company_id`; SoD; saldo protegido (nenhum endpoint escreve saldo); idempotência de conclusão; auditoria de toda conferência/divergência/entrada (FD-001-06).

# 7. NFRs
Listagens keyset; conferência scanner-friendly; entrada nunca perdida (outbox na mesma transação da conclusão); alertas não bloqueiam.

# 8. Critérios de Conclusão
- [ ] 10 endpoints com testes de contrato.
- [ ] Escopo(404)→RBAC→ABAC→SoD por endpoint.
- [ ] Conclusão idempotente gera exatamente uma entrada no MMS-004 (prova INV-RC-04).
- [ ] Catálogo RC-ERR localizado; eventos via outbox.

# 9. Roadmap
| Versão | Escopo |
|---|---|
| v1.0 (MVP) | Este catálogo (10 endpoints), conferência com conversão de UoM, divergências com destino, entrada no MMS-004, compra dedicada. |
| v1.1 | Recebimento de transferência em trânsito; devolução; recebimento parcial múltiplo; relatório de divergências por fornecedor. |
| v2.0 | Quarentena/inspeção de qualidade; lote/validade/série; agendamento de docas. |

# 10. Histórico de Versão
| Versão | Data | Descrição | Autor |
|---|---|---|---|
| 1.0.0 | 2026-08-08 | API do Receiving: modelo de recurso (Receiving com linhas/divergências e conversão de UoM), 10 endpoints `/api/v1/receivings` (criar, conferir, divergência+aprovação, concluir com entrada no MMS-004, cancelar, timeline) mapeados a UC-RC/permissão/evento, catálogo RC-ERR, segurança (saldo protegido, SoD, idempotência de conclusão), NFRs e roadmap — padrão MMS-004-13/MMS-003-13. | Arquitetura Trino |
