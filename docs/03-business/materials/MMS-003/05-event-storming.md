# MMS-003-05 — Event Storming

**Documento:** MMS-003-05 — Event Storming
**Módulo:** MMS-003 — Material Requisition (Solicitação de Material)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-003 (Visão v1.1.0), MMS-003-02 (Business Rules), MMS-003-03 (State Machine), MMS-003-04 (Domain Model), ADR-010, FD-001-04/05/06/07
**Referências:** MMS-004-05 / MMS-002-05 (padrão de formato), MMS-002, MMS-004, MMS-005, PR-001, GOV-001

---

## 1. Objetivo

Registrar o Event Storming do Material Requisition: comandos que provocam mudanças de estado, eventos de domínio resultantes, políticas reativas e reações dos serviços/módulos dependentes. Responde a: **o que pode acontecer** no ciclo da solicitação, **quem reage** e **como os eventos são entregues** de forma confiável (ADR-010).

Fontes de verdade complementares: comandos/invariantes em MMS-003-04; transições em MMS-003-03; regras em MMS-003-02.

## 2. Fluxo de Alto Nível

```
Criar → Submeter → [Workflow] → Aprovar (total/parcial)
   → Validar Estoque (MMS-004) → Rotear por item
        ├─ com saldo → Reserva/Entrega (MMS-004)
        └─ sem saldo → Demanda de Compra (PR-001) → Recebimento (MMS-005) → retoma
   → Confirmar Recebimento → Concluída
```
Estados (MMS-003-03): ST-MR-001..009.

## 3. Atores

| Ator | Tipo | Papel |
|---|---|---|
| Solicitante | Humano | Cria, submete, acompanha, confirma recebimento, cancela |
| Aprovador | Humano | Aprova/rejeita/retorna com parecer (total/parcial) |
| Almoxarife | Humano | Atende via MMS-004 (fila do Warehouse Workspace) |
| Administrador/Gerente | Humano | Cadastra locais de entrega, parametriza |
| Sistema (MMS-003) | Sistema | Validação de estoque, roteamento, projeção de status |
| MMS-004 Inventory | Módulo | Valida disponível, reserva, entrega (eventos consumidos) |
| PR-001 Compras | Módulo | Recebe demanda; informa status (eventos consumidos) |
| MMS-005 Receiving | Módulo | Sinaliza material recebido (evento consumido) |

## 4. Comandos

| Código | Comando | Ator | Descrição | Evento(s) |
|---|---|---|---|---|
| CMD-MR-001 | CreateRequisition | Solicitante | Cria solicitação em Rascunho (MR-BR-020) | EVT-MR-001 |
| CMD-MR-002 | UpdateRequisition | Solicitante | Edita rascunho (itens, motivo, anexos) | — |
| CMD-MR-003 | AddAttachment | Solicitante | Anexa evidência (MR-BR-013) | — |
| CMD-MR-004 | SubmitRequisition | Solicitante | Submete (MR-BR-021) | EVT-MR-002 |
| CMD-MR-005 | ApproveRequisition | Aprovador | Aprova total/parcial (MR-BR-031/032) | EVT-MR-004 (+ EVT-MR-005 se parcial) |
| CMD-MR-006 | RejectRequisition | Aprovador | Recusa integral com parecer | EVT-MR-006 |
| CMD-MR-007 | ReturnRequisition | Aprovador | Devolve ao solicitante | EVT-MR-007 |
| CMD-MR-008 | CancelRequisition | Solicitante/Gestão | Cancela nos estados permitidos (MR-BR-022) | EVT-MR-008 |
| CMD-MR-009 | ConfirmReceipt | Solicitante | Confirma recebimento; conclui (MR-BR-052) | EVT-MR-011 |
| CMD-MR-010 | ManageDeliveryLocation | Admin/Gerente | Cria/edita/inativa local de entrega (MR-BR-060) | (administrativo; timeline) |

**Reações automáticas (sem comando de ator):**
- `ValidateStock` — após aprovação, o `StockValidationGateway` consulta o MMS-004 e publica EVT-MR-009 (POL-MR-03).
- `RouteItems` — o `RoutingService` define rota e publica EVT-MR-010 / demanda de compra (POL-MR-04).

## 5. Eventos de Domínio

| Código | Evento | Nome funcional | Origem | Momento |
|---|---|---|---|---|
| EVT-MR-001 | RequisitionCreated | Solicitação criada | CMD-MR-001 | Rascunho persistido |
| EVT-MR-002 | RequisitionSubmitted | Solicitação submetida | CMD-MR-004 | Submissão persistida |
| EVT-MR-003 | RequisitionInApproval | Solicitação em aprovação | roteamento ao workflow | Instância de workflow criada |
| EVT-MR-004 | RequisitionApproved | Solicitação aprovada | CMD-MR-005 | Decisão persistida |
| EVT-MR-005 | PartialApprovalRegistered | Aprovação parcial registrada | CMD-MR-005 | Ajustes por item persistidos |
| EVT-MR-006 | RequisitionRejected | Solicitação rejeitada | CMD-MR-006 | Recusa persistida |
| EVT-MR-007 | RequisitionReturned | Solicitação retornada | CMD-MR-007 | Devolução persistida |
| EVT-MR-008 | RequisitionCancelled | Solicitação cancelada | CMD-MR-008 | Cancelamento persistido |
| EVT-MR-009 | StockValidated | Estoque validado para a solicitação | ValidateStock (auto) | Resultado por item obtido |
| EVT-MR-010 | ItemRouted | Item roteado (estoque × compra) | RouteItems (auto) | Rota definida por item |
| EVT-MR-011 | RequisitionCompleted | Solicitação concluída | CMD-MR-009 | Todos os itens concluídos |
| EVT-MR-012 | PurchaseDemandGenerated | Demanda de compra gerada | RouteItems (auto) | Demanda enviada ao PR-001 (com origem) |

**Regras gerais:** todo evento é emitido após a persistência (Outbox); exchange `trino.materials`, routing key `evt.requisition.<evento>`; at-least-once, DLQ, idempotência por `eventId` (ADR-010).

## 6. Políticas

| Política | Gatilho | Ação |
|---|---|---|
| POL-MR-01 | Qualquer evento | Timeline (MR-BR-081) |
| POL-MR-02 | Alteração do aggregate | Auditoria (MR-BR-080) |
| POL-MR-03 | RequisitionApproved | Disparar validação de estoque (MR-BR-040) |
| POL-MR-04 | StockValidated | Rotear itens; gerar demanda PR-001 p/ itens sem saldo (MR-BR-042/043) |
| POL-MR-05 | Evento consumido de MMS-004/PR-001/MMS-005 | Atualizar status do item e do cabeçalho (MR-BR-051) |
| POL-MR-06 | RequisitionCancelled | Liberar reservas ativas vinculadas (IV-BR-022) |

## 7. Matriz Evento × Reação

| Evento | Timeline | Auditoria | Notificação | Módulos |
|---|---|---|---|---|
| EVT-MR-002 Submitted | ✅ | ✅ | ✅ (aprovador) | Workflow (FD-001-04) |
| EVT-MR-004 Approved | ✅ | ✅ | ✅ (solicitante) | dispara validação (MMS-004) |
| EVT-MR-006 Rejected | ✅ | ✅ | ✅ (solicitante) | — |
| EVT-MR-009 StockValidated | ✅ | ✅ | — | MMS-004 |
| EVT-MR-010 ItemRouted | ✅ | ✅ | — | MMS-004 / PR-001 |
| EVT-MR-012 PurchaseDemand | ✅ | ✅ | ◐ | ✅ PR-001 |
| EVT-MR-011 Completed | ✅ | ✅ | ✅ (solicitante) | ◐ Analytics |

## 8. Eventos Consumidos

| Evento consumido | Publicador | Reação | Política |
|---|---|---|---|
| Reserva criada/liberada/vencida (EVT-IV-005/007/008) | MMS-004 | Atualiza status do item; notifica | POL-MR-05 |
| Saída/Entrega registrada (EVT-IV-002/006) | MMS-004 | Marca item entregue; habilita confirmação | POL-MR-05 |
| Compra aprovada / pedido confirmado / previsão | PR-001 | Atualiza rota de compra na visão consolidada | POL-MR-05 |
| Material recebido (rota de compra) | MMS-005 | Retoma atendimento pendente (MR-BR-044) | POL-MR-05 |
| Item inativado (EVT-IC-004) | MMS-002 | Sinaliza itens em Rascunho (MR-BR-010) | POL-MR-07 |

Regras de consumo: idempotência por `(eventId, consumerName)`; nenhuma reação escreve saldo (fronteira MR-BR-050 — a reação apenas atualiza a projeção de status da solicitação).

## 9. Especificação Técnica (ADR-010)

- Exchange `trino.materials` (topic, durável); routing keys `evt.requisition.*`; filas por consumidor `q.<consumer>.requisition`; DLX/DLQ por contexto.
- Outbox na mesma transação da mudança de estado; publisher confirms; at-least-once; retry com backoff (5×) → DLQ.
- Ordenação por `aggregateId` (solicitação). Envelope conforme ARC-005 §8 (eventId, aggregateId, companyId, correlationId, causationId).

## 10. Rastreabilidade Comando → Evento

| Comando | Evento | Regra |
|---|---|---|
| CMD-MR-001 | EVT-MR-001 | MR-BR-020 |
| CMD-MR-004 | EVT-MR-002 | MR-BR-021 |
| CMD-MR-005 | EVT-MR-004 (+005) | MR-BR-031/032 |
| CMD-MR-006 | EVT-MR-006 | MR-BR-030 |
| CMD-MR-007 | EVT-MR-007 | FA-MR-01 |
| CMD-MR-008 | EVT-MR-008 | MR-BR-022 |
| CMD-MR-009 | EVT-MR-011 | MR-BR-052 |
| (auto) ValidateStock | EVT-MR-009 | MR-BR-040/041 |
| (auto) RouteItems | EVT-MR-010 / EVT-MR-012 | MR-BR-042/043 |

## 11. Histórico de Versão

| Versão | Data | Autor | Descrição |
|---|---|---|---|
| 1.0.0 | 2026-08-08 | Arquiteto Principal | Event Storming do Material Requisition: 10 comandos (CMD-MR-001..010) + 2 reações automáticas (validação/roteamento), 12 eventos de domínio (EVT-MR-001..012), 6 políticas, matriz evento×reação, 5 eventos consumidos (MMS-002/004, PR-001, MMS-005) com idempotência por origem, e especificação técnica ADR-010 (exchange `trino.materials`, routing keys `evt.requisition.*`, Outbox, at-least-once, DLQ, ordenação por agregado) — padrão MMS-004-05. |
