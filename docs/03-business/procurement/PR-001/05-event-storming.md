**Documento:** PR-001-05 — Event Storming
**Versão:** 1.1.0
**Status:** Approved

> Decisão arquitetural associada: ADR-010 — Eventos de Negócio como linguagem oficial (ver `docs/17-adr/ADR-010-business-domain-events.md`).

# PR-001-05 — Event Storming

> Este documento identifica os eventos de negócio, comandos, políticas e atores envolvidos no ciclo de vida da Solicitação de Compra.

---

# 1. Objetivo

Mapear todos os eventos relevantes do domínio Procurement relacionados à Solicitação de Compra, permitindo compreender como o negócio reage às ações dos usuários e às políticas organizacionais.

O Event Storming servirá como base para:

* APIs
* Workflow
* Notificações
* Auditoria
* Integrações futuras
* Automações
* IA (versões futuras)

---

# 2. Fluxo de Alto Nível

```text
Necessidade Identificada
        │
        ▼
Criar Solicitação
        │
        ▼
Adicionar Itens
        │
        ▼
Enviar Solicitação
        │
        ▼
Validar
        │
        ▼
Aprovar
        │
        ▼
Disponibilizar para Compras
```

---

# 3. Atores

| Ator          | Papel                          |
| ------------- | ------------------------------ |
| Solicitante   | Inicia a requisição            |
| Gestor        | Aprova ou rejeita              |
| Comprador     | Recebe a demanda               |
| Sistema       | Executa validações automáticas |
| Administrador | Configura regras               |

---

# 4. Comandos (Commands)

Os comandos representam intenções explícitas de alterar o estado do domínio.

| Código  | Command                    |
| ------- | -------------------------- |
| CMD-001 | CreatePurchaseRequisition  |
| CMD-002 | UpdatePurchaseRequisition  |
| CMD-003 | AddItem                    |
| CMD-004 | RemoveItem                 |
| CMD-005 | SubmitPurchaseRequisition  |
| CMD-006 | ApprovePurchaseRequisition |
| CMD-007 | RejectPurchaseRequisition  |
| CMD-008 | ReturnForAdjustment        |
| CMD-009 | CancelPurchaseRequisition  |
| CMD-010 | ClosePurchaseRequisition   |
| CMD-011 | AddAttachment              |
| CMD-012 | AddComment                 |

---

# 5. Eventos de Negócio (Business Events)

Os eventos representam fatos consumados.

| Código  | Evento                       |
| ------- | ---------------------------- |
| EVT-001 | PurchaseRequisitionCreated   |
| EVT-002 | PurchaseRequisitionUpdated   |
| EVT-003 | ItemAdded                    |
| EVT-004 | ItemRemoved                  |
| EVT-005 | PurchaseRequisitionSubmitted |
| EVT-006 | ValidationStarted            |
| EVT-007 | ValidationCompleted          |
| EVT-008 | ApprovalStarted              |
| EVT-009 | PurchaseRequisitionApproved  |
| EVT-010 | PurchaseRequisitionRejected  |
| EVT-011 | PurchaseRequisitionReturned  |
| EVT-012 | PurchaseRequisitionCancelled |
| EVT-013 | PurchaseRequisitionClosed    |
| EVT-014 | AttachmentAdded              |
| EVT-015 | CommentAdded                 |

A especificação técnica completa de cada evento (payload, publisher, consumers, versionamento, retry, dead letter, idempotência, ordenação e garantias de entrega) está na **Seção 14 — Especificação Técnica dos Eventos**.

---

# 6. Políticas (Policies)

As políticas reagem aos eventos.

### POL-001 – Início do Workflow

**Quando**

PurchaseRequisitionSubmitted

**Então**

Iniciar fluxo de aprovação.

---

### POL-002 – Atualização da Timeline

**Quando**

Qualquer evento de domínio ocorrer

**Então**

Registrar na Timeline.

---

### POL-003 – Auditoria

**Quando**

Qualquer evento modificar o Aggregate

**Então**

Registrar auditoria.

---

### POL-004 – Notificação

**Quando**

PurchaseRequisitionSubmitted

**Então**

Notificar aprovadores.

---

### POL-005 – Encaminhamento para Compras

**Quando**

PurchaseRequisitionApproved

**Então**

Disponibilizar a requisição para o módulo Procurement.

---

# 7. Validações Automáticas

As validações são executadas após o comando `SubmitPurchaseRequisition`.

* Campos obrigatórios
* Quantidade válida
* Centro de custo
* Projeto
* Categoria
* Datas
* Permissões
* Workflow disponível

Caso alguma validação falhe:

Evento:

```text
PurchaseRequisitionReturned
```

---

# 8. Reações Esperadas

## Evento

PurchaseRequisitionApproved

Reações:

* Atualizar Status
* Registrar Timeline
* Registrar Auditoria
* Notificar Comprador
* Liberar para Compras

---

## Evento

PurchaseRequisitionRejected

Reações:

* Atualizar Status
* Registrar Motivo
* Notificar Solicitante
* Atualizar Timeline

---

## Evento

PurchaseRequisitionCancelled

Reações:

* Encerrar Workflow
* Cancelar Aprovações Pendentes
* Atualizar Timeline
* Registrar Auditoria

---

# 9. Eventos Externos (Futuro)

Na versão futura o módulo poderá publicar eventos para:

* ERP
* BI
* Data Lake
* Plataforma de IA
* Sistema Financeiro
* Gestão Orçamentária
* Portal do Fornecedor

---

# 10. Eventos Não Permitidos

Não devem existir eventos como:

* SaveButtonClicked
* ScreenOpened
* UserLogged
* RecordUpdated
* SQLExecuted

Esses são eventos técnicos e não representam fatos relevantes do negócio.

---

# 11. Matriz Evento × Reação

| Evento    | Workflow | Timeline | Auditoria | Notificação |
| --------- | -------- | -------- | --------- | ----------- |
| Created   | ✔        | ✔        | ✔         | ❌           |
| Submitted | ✔        | ✔        | ✔         | ✔           |
| Approved  | ✔        | ✔        | ✔         | ✔           |
| Rejected  | ✔        | ✔        | ✔         | ✔           |
| Cancelled | ✔        | ✔        | ✔         | ✔           |
| Closed    | ✔        | ✔        | ✔         | ❌           |

---

# 12. Dependências

Este documento é utilizado por:

* State Machine
* API
* Workflow Engine
* Notification Engine
* Timeline
* Audit Service
* Analytics
* Integrações

---

# 13. Evolução Planejada — Business Event Catalog

Proposta registrada: criar um catálogo corporativo de eventos de negócio reutilizáveis (`docs/03-business/foundation/event-catalog/`), onde cada evento terá especificação própria contendo nome, descrição, aggregate de origem, comando gerador, payload, consumidores, políticas acionadas, versão, compatibilidade e regras de publicação.

Status: proposta aprovada para roadmap, sem data definida. Registrada no Document Registry (GOV-002).

---

# 14. Especificação Técnica dos Eventos

> Esta seção define o contrato técnico de publicação e consumo de cada evento de negócio do módulo PR-001. Nenhum evento existente foi alterado; esta seção apenas formaliza os metadados operacionais de cada um.

## 14.1 Padrões Transversais (aplicáveis a todos os eventos)

| Aspecto | Padrão Oficial |
| ------- | -------------- |
| **Transporte** | RabbitMQ (exchange `trino.procurement`, tipo `topic`) |
| **Publicação** | Outbox Pattern — evento persistido na tabela `outbox_message` na **mesma transação** da alteração do aggregate; relay assíncrono publica no broker |
| **Garantia de entrega** | **At-least-once** — o consumidor deve ser idempotente |
| **CorrelationId** | UUID propagado do comando original (header HTTP `X-Correlation-Id`; se ausente, gerado no entry point). Todos os eventos derivados do mesmo comando compartilham o mesmo CorrelationId |
| **CausationId** | UUID do evento/comando que causou diretamente este evento |
| **Retry** | Exponencial com jitter: 5 tentativas (1s → 4s → 16s → 64s → 256s). Configuração: `messaging.retry.max-attempts=5` |
| **Dead Letter** | Após esgotar as tentativas, mensagem movida para DLQ `trino.procurement.dlq` com headers `x-original-event-id`, `x-failure-reason`, `x-failed-at`. Alerta operacional obrigatório |
| **Idempotência** | Consumidor deduplica por `(eventId, consumerName)` persistido em tabela de inbox (`processed_message`). Evento duplicado é descartado sem efeito colateral |
| **Ordenação** | Garantida **por aggregate** — routing key inclui `aggregateId`; fila particionada por aggregate. Não há garantia de ordem global entre aggregates distintos |
| **Serialização** | JSON UTF-8, `content-type: application/json` |
| **Envelope** | `{ eventId, eventType, version, aggregateId, aggregateType, occurredAt, correlationId, causationId, actorId, companyId, payload }` |
| **Versionamento** | Campo `version` no envelope. Mudanças de payload seguem compatibilidade backward (apenas campos aditivos na mesma versão major) |
| **Timestamp** | `occurredAt` em UTC, ISO-8601 |
| **Actor** | `actorId` = usuário que executou o comando; `SYSTEM` quando gerado por automação |

---

## 14.2 Fichas Técnicas

### EVT-001 — PurchaseRequisitionCreated

| Campo | Valor |
| ----- | ----- |
| **Payload** | `{ requisitionId, number, companyId, businessUnitId, requesterId, status: "Draft", priority, justification, requiredDate, costCenterId, projectId, totalEstimatedValue, createdAt }` |
| **Publisher** | PR-001 Service (módulo Procurement) |
| **Consumers** | Timeline Service, Audit Service, Numbering Service (confirmação de sequência) |
| **Version** | 1 |
| **CorrelationId** | Herdado de CMD-001 |
| **Retry** | Padrão transversal (5 tentativas exponenciais) |
| **Dead Letter** | DLQ `trino.procurement.dlq` |
| **Idempotência** | Obrigatória — deduplicação por `eventId` |
| **Ordem** | Primeiro evento do ciclo de vida do aggregate; sempre posição 1 na sequência do `aggregateId` |
| **Garantias de entrega** | At-least-once via Outbox |

---

### EVT-002 — PurchaseRequisitionUpdated

| Campo | Valor |
| ----- | ----- |
| **Payload** | `{ requisitionId, number, changedFields: [ { field, oldValue, newValue } ], updatedBy, updatedAt, aggregateVersion }` |
| **Publisher** | PR-001 Service |
| **Consumers** | Timeline Service, Audit Service |
| **Version** | 1 |
| **CorrelationId** | Herdado de CMD-002 |
| **Retry** | Padrão transversal |
| **Dead Letter** | DLQ `trino.procurement.dlq` |
| **Idempotência** | Obrigatória — `changedFields` deve refletir snapshot do momento da alteração para replay seguro |
| **Ordem** | Por `aggregateId`, conforme `aggregateVersion` incremental |
| **Garantias de entrega** | At-least-once via Outbox |

---

### EVT-003 — ItemAdded

| Campo | Valor |
| ----- | ----- |
| **Payload** | `{ requisitionId, itemId, sequence, itemType, description, quantity, unitOfMeasure, estimatedUnitPrice, estimatedTotalPrice, categoryId, costCenterId, projectId, addedBy, addedAt }` |
| **Publisher** | PR-001 Service |
| **Consumers** | Timeline Service, Audit Service |
| **Version** | 1 |
| **CorrelationId** | Herdado de CMD-003 |
| **Retry** | Padrão transversal |
| **Dead Letter** | DLQ `trino.procurement.dlq` |
| **Idempotência** | Obrigatória — consumer verifica existência do `itemId` antes de projetar |
| **Ordem** | Por `aggregateId` |
| **Garantias de entrega** | At-least-once via Outbox |

---

### EVT-004 — ItemRemoved

| Campo | Valor |
| ----- | ----- |
| **Payload** | `{ requisitionId, itemId, sequence, removedBy, removedAt, removalReason? }` |
| **Publisher** | PR-001 Service |
| **Consumers** | Timeline Service, Audit Service |
| **Version** | 1 |
| **CorrelationId** | Herdado de CMD-004 |
| **Retry** | Padrão transversal |
| **Dead Letter** | DLQ `trino.procurement.dlq` |
| **Idempotência** | Obrigatória — remoção já aplicada é ignorada |
| **Ordem** | Por `aggregateId`; deve respeitar ordem relativa ao EVT-003 do mesmo item |
| **Garantias de entrega** | At-least-once via Outbox |

---

### EVT-005 — PurchaseRequisitionSubmitted

| Campo | Valor |
| ----- | ----- |
| **Payload** | `{ requisitionId, number, companyId, businessUnitId, requesterId, submittedBy, submittedAt, itemCount, totalEstimatedValue, costCenterId, projectId, priority }` |
| **Publisher** | PR-001 Service |
| **Consumers** | Workflow Engine (POL-001), Timeline Service, Audit Service, Notification Service (POL-004) |
| **Version** | 1 |
| **CorrelationId** | Herdado de CMD-005 |
| **Retry** | Padrão transversal; Workflow Engine deve reprocessar com segurança (não duplicar instância de workflow — ver idempotência) |
| **Dead Letter** | DLQ `trino.procurement.dlq` + alerta operacional (evento crítico: bloqueia início de aprovação) |
| **Idempotência** | Obrigatória — Workflow Engine verifica se já existe instância ativa para `requisitionId` antes de criar |
| **Ordem** | Por `aggregateId`; deve ser precedido por EVT-001 e pelos EVT-003 dos itens |
| **Garantias de entrega** | At-least-once via Outbox |

---

### EVT-006 — ValidationStarted

| Campo | Valor |
| ----- | ----- |
| **Payload** | `{ requisitionId, validationId, startedAt, triggeredBy, checks: [ "RequiredFields", "Quantity", "CostCenter", "Project", "Category", "Dates", "Permissions", "Workflow" ] }` |
| **Publisher** | PR-001 Service (Validation Engine interno) |
| **Consumers** | Timeline Service, Audit Service |
| **Version** | 1 |
| **CorrelationId** | Herdado de CMD-005 (mesma cadeia causal do submit) |
| **Retry** | Padrão transversal |
| **Dead Letter** | DLQ `trino.procurement.dlq` |
| **Idempotência** | Obrigatória — `validationId` único por execução de validação |
| **Ordem** | Por `aggregateId`; precede EVT-007 da mesma validação |
| **Garantias de entrega** | At-least-once via Outbox |

---

### EVT-007 — ValidationCompleted

| Campo | Valor |
| ----- | ----- |
| **Payload** | `{ requisitionId, validationId, result: "Approved" | "Failed", completedAt, failures: [ { rule, code, message } ], durationMs }` |
| **Publisher** | PR-001 Service (Validation Engine interno) |
| **Consumers** | Timeline Service, Audit Service; indiretamente dispara EVT-011 (Return) quando `result = "Failed"` |
| **Version** | 1 |
| **CorrelationId** | Herdado de CMD-005 |
| **Retry** | Padrão transversal |
| **Dead Letter** | DLQ `trino.procurement.dlq` |
| **Idempotência** | Obrigatória — deduplicação por `validationId` |
| **Ordem** | Por `aggregateId`; segue EVT-006 do mesmo `validationId` |
| **Garantias de entrega** | At-least-once via Outbox |

---

### EVT-008 — ApprovalStarted

| Campo | Valor |
| ----- | ----- |
| **Payload** | `{ requisitionId, workflowInstanceId, startedAt, approvalChain: [ { level, approverId, status } ], currentLevel, slaDeadline }` |
| **Publisher** | Workflow Engine |
| **Consumers** | Timeline Service, Audit Service, Notification Service (primeiro aprovador) |
| **Version** | 1 |
| **CorrelationId** | Herdado da cadeia CMD-005 → EVT-005 |
| **Retry** | Padrão transversal |
| **Dead Letter** | DLQ `trino.procurement.dlq` + alerta (evento crítico) |
| **Idempotência** | Obrigatória — `workflowInstanceId` único; consumers verificam existência antes de projetar |
| **Ordem** | Por `aggregateId`; segue EVT-005 |
| **Garantias de entrega** | At-least-once via Outbox |

---

### EVT-009 — PurchaseRequisitionApproved

| Campo | Valor |
| ----- | ----- |
| **Payload** | `{ requisitionId, number, approvedBy, approvedAt, finalLevel, comments?, companyId, businessUnitId, requesterId, totalEstimatedValue, costCenterId, projectId }` |
| **Publisher** | Workflow Engine (decisão final) / PR-001 Service |
| **Consumers** | PR-001 Service (atualiza status — POL-005), Timeline Service, Audit Service, Notification Service (comprador e solicitante), Procurement Module (liberação para compras) |
| **Version** | 1 |
| **CorrelationId** | Herdado de CMD-006 |
| **Retry** | Padrão transversal |
| **Dead Letter** | DLQ `trino.procurement.dlq` + alerta operacional (evento crítico: bloqueia liberação para compras) |
| **Idempotência** | Obrigatória — transição de estado aplicada uma única vez; consumer de liberação verifica status atual |
| **Ordem** | Por `aggregateId`; marca o fim da cadeia de aprovação |
| **Garantias de entrega** | At-least-once via Outbox |

---

### EVT-010 — PurchaseRequisitionRejected

| Campo | Valor |
| ----- | ----- |
| **Payload** | `{ requisitionId, number, rejectedBy, rejectedAt, level, reason (obrigatório), comments?, requesterId }` |
| **Publisher** | Workflow Engine / PR-001 Service |
| **Consumers** | Timeline Service, Audit Service, Notification Service (solicitante) |
| **Version** | 1 |
| **CorrelationId** | Herdado de CMD-007 |
| **Retry** | Padrão transversal |
| **Dead Letter** | DLQ `trino.procurement.dlq` |
| **Idempotência** | Obrigatória — estado final; reaplicação sem efeito |
| **Ordem** | Por `aggregateId`; evento terminal do fluxo de aprovação |
| **Garantias de entrega** | At-least-once via Outbox |

---

### EVT-011 — PurchaseRequisitionReturned

| Campo | Valor |
| ----- | ----- |
| **Payload** | `{ requisitionId, returnedBy, returnedAt, origin: "Validation" | "Approver", reason (obrigatório), failedChecks?, level? }` |
| **Publisher** | PR-001 Service (validação) ou Workflow Engine (aprovador) |
| **Consumers** | Timeline Service, Audit Service, Notification Service (solicitante) |
| **Version** | 1 |
| **CorrelationId** | Herdado de CMD-008 ou da validação automática (CMD-005) |
| **Retry** | Padrão transversal |
| **Dead Letter** | DLQ `trino.procurement.dlq` |
| **Idempotência** | Obrigatória — deduplicação por `eventId`; retorno já aplicado é ignorado |
| **Ordem** | Por `aggregateId` |
| **Garantias de entrega** | At-least-once via Outbox |

---

### EVT-012 — PurchaseRequisitionCancelled

| Campo | Valor |
| ----- | ----- |
| **Payload** | `{ requisitionId, number, cancelledBy, cancelledAt, reason (obrigatório), previousStatus, pendingApprovalsCancelled }` |
| **Publisher** | PR-001 Service |
| **Consumers** | Workflow Engine (encerrar instância e cancelar aprovações pendentes), Timeline Service, Audit Service, Notification Service (aprovadores pendentes, se houver) |
| **Version** | 1 |
| **CorrelationId** | Herdado de CMD-009 |
| **Retry** | Padrão transversal |
| **Dead Letter** | DLQ `trino.procurement.dlq` + alerta operacional |
| **Idempotência** | Obrigatória — estado final; reaplicação sem efeito |
| **Ordem** | Por `aggregateId`; evento terminal |
| **Garantias de entrega** | At-least-once via Outbox |

---

### EVT-013 — PurchaseRequisitionClosed

| Campo | Valor |
| ----- | ----- |
| **Payload** | `{ requisitionId, number, closedBy, closedAt, reason, finalStatus }` |
| **Publisher** | PR-001 Service |
| **Consumers** | Timeline Service, Audit Service, Analytics (futuro) |
| **Version** | 1 |
| **CorrelationId** | Herdado de CMD-010 |
| **Retry** | Padrão transversal |
| **Dead Letter** | DLQ `trino.procurement.dlq` |
| **Idempotência** | Obrigatória — estado final; reaplicação sem efeito |
| **Ordem** | Por `aggregateId`; último evento do ciclo de vida |
| **Garantias de entrega** | At-least-once via Outbox |

---

### EVT-014 — AttachmentAdded

| Campo | Valor |
| ----- | ----- |
| **Payload** | `{ requisitionId, attachmentId, fileName, storageKey, mimeType, fileSize, uploadedBy, uploadedAt }` |
| **Publisher** | PR-001 Service (após confirmação do upload no MinIO via FD-001-03) |
| **Consumers** | Timeline Service, Audit Service |
| **Version** | 1 |
| **CorrelationId** | Herdado de CMD-011 |
| **Retry** | Padrão transversal |
| **Dead Letter** | DLQ `trino.procurement.dlq` |
| **Idempotência** | Obrigatória — deduplicação por `attachmentId`; `storageKey` nunca expõe URL pública (FD-001-03) |
| **Ordem** | Por `aggregateId` |
| **Garantias de entrega** | At-least-once via Outbox |

---

### EVT-015 — CommentAdded

| Campo | Valor |
| ----- | ----- |
| **Payload** | `{ requisitionId, commentId, authorId, message, internal, createdAt }` |
| **Publisher** | PR-001 Service |
| **Consumers** | Timeline Service, Audit Service, Notification Service (participantes relevantes, exceto o autor) |
| **Version** | 1 |
| **CorrelationId** | Herdado de CMD-012 |
| **Retry** | Padrão transversal |
| **Dead Letter** | DLQ `trino.procurement.dlq` |
| **Idempotência** | Obrigatória — deduplicação por `commentId` |
| **Ordem** | Por `aggregateId` |
| **Garantias de entrega** | At-least-once via Outbox |

---

## 14.3 Matriz Resumo de Garantias

| Evento | Crítico | Retry | DLQ | Idempotência | Ordenação | Entrega |
| ------ | ------- | ----- | --- | ------------ | --------- | ------- |
| EVT-001 Created | Não | 5x exponencial | ✔ | ✔ | Por aggregate | At-least-once |
| EVT-002 Updated | Não | 5x exponencial | ✔ | ✔ | Por aggregate | At-least-once |
| EVT-003 ItemAdded | Não | 5x exponencial | ✔ | ✔ | Por aggregate | At-least-once |
| EVT-004 ItemRemoved | Não | 5x exponencial | ✔ | ✔ | Por aggregate | At-least-once |
| EVT-005 Submitted | **Sim** | 5x exponencial | ✔ + alerta | ✔ | Por aggregate | At-least-once |
| EVT-006 ValidationStarted | Não | 5x exponencial | ✔ | ✔ | Por aggregate | At-least-once |
| EVT-007 ValidationCompleted | Não | 5x exponencial | ✔ | ✔ | Por aggregate | At-least-once |
| EVT-008 ApprovalStarted | **Sim** | 5x exponencial | ✔ + alerta | ✔ | Por aggregate | At-least-once |
| EVT-009 Approved | **Sim** | 5x exponencial | ✔ + alerta | ✔ | Por aggregate | At-least-once |
| EVT-010 Rejected | Não | 5x exponencial | ✔ | ✔ | Por aggregate | At-least-once |
| EVT-011 Returned | Não | 5x exponencial | ✔ | ✔ | Por aggregate | At-least-once |
| EVT-012 Cancelled | **Sim** | 5x exponencial | ✔ + alerta | ✔ | Por aggregate | At-least-once |
| EVT-013 Closed | Não | 5x exponencial | ✔ | ✔ | Por aggregate | At-least-once |
| EVT-014 AttachmentAdded | Não | 5x exponencial | ✔ | ✔ | Por aggregate | At-least-once |
| EVT-015 CommentAdded | Não | 5x exponencial | ✔ | ✔ | Por aggregate | At-least-once |

---

## 14.4 Rastreabilidade Comando → Evento

| Comando | Evento(s) resultante(s) |
| ------- | ----------------------- |
| CMD-001 CreatePurchaseRequisition | EVT-001 |
| CMD-002 UpdatePurchaseRequisition | EVT-002 |
| CMD-003 AddItem | EVT-003 |
| CMD-004 RemoveItem | EVT-004 |
| CMD-005 SubmitPurchaseRequisition | EVT-005 → EVT-006 → EVT-007 → (EVT-008 | EVT-011) |
| CMD-006 ApprovePurchaseRequisition | EVT-009 (decisão final) |
| CMD-007 RejectPurchaseRequisition | EVT-010 |
| CMD-008 ReturnForAdjustment | EVT-011 |
| CMD-009 CancelPurchaseRequisition | EVT-012 |
| CMD-010 ClosePurchaseRequisition | EVT-013 |
| CMD-011 AddAttachment | EVT-014 |
| CMD-012 AddComment | EVT-015 |

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | — | Arquitetura Trino | Versão inicial aprovada |
| 1.1.0 | 2026-07-30 | Arquitetura Trino | Adicionada Seção 14 — Especificação Técnica dos Eventos (payload, publisher, consumers, version, CorrelationId, retry, dead letter, idempotência, ordem, garantias de entrega) para os 15 eventos, padrões transversais de mensageria, matriz de garantias e rastreabilidade comando→evento. Nenhum evento, comando, política ou fluxo existente foi alterado |
