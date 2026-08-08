# MMS-005-05 — Event Storming

**Documento:** MMS-005-05 — Event Storming
**Módulo:** MMS-005 — Receiving
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-005-02/03/04, ADR-010, MMS-004, MMS-003, PR-001, FD-001-05/06/07
**Referências:** MMS-004-05 / MMS-003-05 (padrão), GOV-001

# 1. Objetivo
Comandos, eventos, políticas e reações do Receiving; entrega confiável (ADR-010).

# 2. Fluxo
`Criar (origem) → Iniciar conferência → Registrar linhas/divergências → Concluir → Entrada MMS-004 (+ atendimento MMS-003)`

# 3. Comandos
| Código | Comando | Ator | Evento |
|---|---|---|---|
| CMD-RC-001 | CreateReceiving | Sistema (PR-001)/Almoxarife | EVT-RC-001 |
| CMD-RC-002 | StartInspection | Almoxarife | EVT-RC-002 |
| CMD-RC-003 | RegisterLine | Almoxarife | — (progresso) |
| CMD-RC-004 | RegisterDivergence | Almoxarife | EVT-RC-003 |
| CMD-RC-005 | ApproveDivergence | Supervisor | EVT-RC-004 |
| CMD-RC-006 | CompleteReceiving | Almoxarife | EVT-RC-005 (+ EVT-RC-006 se divergência) + EVT-RC-007 |
| CMD-RC-007 | CancelReceiving | Almoxarife | EVT-RC-008 |

# 4. Eventos
| Código | Evento | Momento |
|---|---|---|
| EVT-RC-001 | ReceivingRegistered | Recebimento criado (Aguardando) |
| EVT-RC-002 | ReceivingInInspection | Início da conferência |
| EVT-RC-003 | DivergenceRegistered | Divergência com destino |
| EVT-RC-004 | DivergenceApproved | Destino aprovado |
| EVT-RC-005 | ReceivingCompleted | Conclusão sem divergência |
| EVT-RC-006 | ReceivingCompletedWithDivergence | Conclusão com divergência |
| EVT-RC-007 | StockEntryGenerated | Entrada gerada no MMS-004 |
| EVT-RC-008 | ReceivingCancelled | Cancelamento |

# 5. Políticas
POL-RC-01 Timeline/Auditoria (RC-BR-050) · POL-RC-02 Conclusão → **StockEntryGateway** dispara entrada no MMS-004 (RC-BR-020) · POL-RC-03 Compra dedicada → atendimento retomado (MMS-003, RC-BR-021) · POL-RC-04 Pedido confirmado (PR-001) → cria Receiving Aguardando.

# 6. Matriz Evento × Reação
| Evento | Timeline | Auditoria | Notificação | Módulos |
|---|---|---|---|---|
| EVT-RC-003 Divergence | ✅ | ✅ | ✅ (supervisor) | — |
| EVT-RC-005/006 Completed | ✅ | ✅ | ◐ | MMS-004 (entrada) |
| EVT-RC-007 StockEntry | ✅ | ✅ | — | MMS-004; MMS-003 (retomada) |

# 7. Eventos Consumidos
| Evento | Origem | Reação |
|---|---|---|
| Pedido confirmado / previsão de entrega | PR-001 | Cria Receiving Aguardando (POL-RC-04) |
| Transferência expedida | MMS-004 | Cria expectativa de recebimento (v1.1) |
| Devolução registrada | MMS-003 | Cria recebimento de devolução |
| Item inativado | MMS-002 | Alerta em recebimentos pendentes do item |

# 8. Especificação Técnica (ADR-010)
Exchange `trino.materials`, routing keys `evt.receiving.*`, Outbox na mesma transação, at-least-once, retry 5× → DLQ, idempotência por `eventId`, ordenação por `aggregateId`. **EVT-RC-007** é o gatilho da entrada no MMS-004 (consumido pelo Inventory como origem de EVT-IV-001).

# 9. Histórico de Versão
| Versão | Data | Descrição |
|--------|------|-----------|
| 1.0.0 | 2026-08-08 | Event Storming do Receiving: 7 comandos CMD-RC, 8 eventos EVT-RC-001..008, 4 políticas (incl. entrada no MMS-004 e atendimento retomado), matriz evento×reação, 4 eventos consumidos (PR-001/MMS-004/003/002) e especificação técnica ADR-010 — padrão MMS-004-05. |
