# PR-001-04 — Domain Model

> Este documento define o modelo de domínio do módulo Purchase Requisition.

**Versão:** 1.1.0
**Status:** 🟢 Approved

---

# 1. Objetivo

Representar os conceitos de negócio do módulo de Solicitação de Compra, seus relacionamentos, responsabilidades e regras de consistência.

O modelo de domínio é independente de banco de dados, APIs e interface de usuário.

---

# 2. Aggregate Root

## Purchase Requisition

A Solicitação de Compra é o Aggregate Root do módulo.

Ela é responsável por garantir a consistência de todas as informações relacionadas à requisição.

Nenhuma entidade interna poderá ser alterada diretamente sem passar pelo Aggregate Root.

Responsabilidades:

- Criar requisição
- Alterar informações permitidas
- Adicionar itens
- Remover itens
- Enviar para aprovação
- Cancelar
- Registrar histórico
- Validar regras
- Gerar eventos de domínio

---

# 3. Aggregate Diagram

```text
┌─────────────────────────────────────────────────────────────┐
│                    AGGREGATE: PurchaseRequisition            │
│                         (Aggregate Root)                     │
│                                                              │
│  Id · Number · Company · Unit · Requester · Status           │
│  Priority · RequiredDate · Justification · CostCenter        │
│  Project · TotalEstimatedValue · Version                     │
│                                                              │
│  ├── 0..N  PurchaseRequisitionItem  (Entity)                 │
│  │         Sequence · ItemType · Description · Quantity      │
│  │         UnitOfMeasure · Category · EstimatedPrice         │
│  │         CostCenter · Project                              │
│  │                                                           │
│  ├── 0..N  Attachment  (Entity → referência FD-001-03)       │
│  ├── 0..N  Comment     (Entity → referência FD-001-08)       │
│  └── 1..N  Approval    (Entity → referência FD-001-04)       │
│                                                              │
│  Value Objects: Money · Quantity · DateRange · Address       │
│                                                              │
│  Referências externas (por Id, fora do Aggregate):           │
│  Company · BusinessUnit · CostCenter · Project · Requester   │
│  WorkflowInstance · DocumentCollection · ConversationThread  │
└─────────────────────────────────────────────────────────────┘
```

> Nota: a proposta de aggregate enxuto (Attachment/Comment/Approval como referências ao Foundation) está registrada em PR-001-11, seção 14, e aguarda ADR própria. Até lá, o modelo acima permanece vigente.

---

# 4. Relacionamentos UML

```text
Company            1 ────── N   PurchaseRequisition
BusinessUnit       1 ────── N   PurchaseRequisition
Requester (User)   1 ────── N   PurchaseRequisition
CostCenter         1 ────── N   PurchaseRequisition
Project            1 ────── N   PurchaseRequisition

PurchaseRequisition  1 ────── N   PurchaseRequisitionItem
PurchaseRequisition  1 ────── N   Attachment
PurchaseRequisition  1 ────── N   Comment
PurchaseRequisition  1 ────── N   Approval

PurchaseRequisitionItem  N ────── 1   Category (Master Data)
PurchaseRequisitionItem  N ────── 1   CostCenter (opcional)
PurchaseRequisitionItem  N ────── 1   Project (opcional)

PurchaseRequisition  1 ────── 1   WorkflowInstance (Workflow Engine, por Id)
PurchaseRequisition  1 ────── 1   DocumentCollection (Document Mgmt, por Id)
PurchaseRequisition  1 ────── 1   ConversationThread (Collaboration, por Id)
```

Cardinalidades de negócio:

* Requisição sem item é válida apenas em Draft; para submissão, 1..N itens (PR-BR-021).
* Approval: 1..N somente quando existe workflow iniciado.

---

# 5. Entidades

## Purchase Requisition

Representa a solicitação de compra.

Principais atributos:

- Id
- Número
- Empresa
- Unidade
- Solicitante
- Data de criação
- Status
- Prioridade
- Data de necessidade
- Justificativa
- Centro de custo
- Projeto

---

## Purchase Requisition Item

Representa um item solicitado.

Principais atributos:

- Id
- Sequência
- Tipo (Material ou Serviço)
- Descrição
- Quantidade
- Unidade de medida
- Categoria
- Valor estimado
- Centro de custo
- Projeto

---

## Attachment

Representa documentos anexados à requisição.

Exemplos:

- Especificações técnicas
- Orçamentos
- Desenhos
- Fotos
- Contratos
- Catálogos

---

## Comment

Representa comentários realizados durante o processo.

Características:

- Autor
- Data
- Texto
- Visível para fornecedor (futuro)
- Interno

---

## Approval

Representa cada etapa de aprovação.

Atributos:

- Aprovador
- Nível
- Status
- Data
- Parecer
- Observações

---

# 6. Value Objects

## Money

- Valor (NUMERIC(18,2))
- Moeda (ISO 4217)

Imutável. Operações: `Add`, `Subtract`, `Compare`, `IsZero`, `IsNegative` (proibido). Igualdade por valor + moeda.

---

## Quantity

- Quantidade (NUMERIC(18,4))
- Unidade (unidade de medida do Master Data)

Imutável. Invariante: quantidade > 0 (PR-BR-010). Igualdade por valor + unidade.

---

## Date Range

Utilizado em períodos.

- Início
- Fim

Imutável. Invariante: início <= fim. Operações: `Contains(date)`, `Overlaps(other)`.

---

## Address

Preparado para futuras integrações.

- Logradouro, Número, Complemento, Bairro, Cidade, UF, CEP, País

Imutável.

---

## RequisitionNumber

- Valor formatado conforme máscara da empresa (PR-BR-001)

Imutável. Gerado por serviço de numeração; único por empresa.

---

## Priority (Value Object de enumeração)

- Low, Normal, High, Urgent

Imutável. Regra: Urgent exige justificativa (PR-BR-022).

---

# 7. Enumerações

## Requisition Status

- Draft
- Submitted
- Under Validation
- Waiting Approval
- Approved
- Returned
- Rejected
- Ready for Procurement
- Cancelled
- Closed

---

## Priority

- Low
- Normal
- High
- Urgent

---

## Item Type

- Material
- Service

---

## Approval Status

- Pending
- Approved
- Rejected
- Delegated
- Expired

---

# 8. Relacionamentos

Purchase Requisition

possui

0..N Purchase Requisition Item

---

Purchase Requisition

possui

0..N Attachments

---

Purchase Requisition

possui

0..N Comments

---

Purchase Requisition

possui

1..N Approvals

---

# 9. Invariantes

Uma requisição nunca poderá existir:

- sem solicitante
- sem empresa
- sem status
- sem data de criação

Uma requisição enviada deverá possuir:

- pelo menos um item

Uma requisição aprovada não poderá sofrer alterações estruturais.

Invariantes adicionais (formalizadas):

| Código | Invariante | Origem |
|--------|-----------|--------|
| INV-01 | `items.Count >= 1` antes de Submit | PR-BR-021 |
| INV-02 | `quantity > 0` em todo item | PR-BR-010 |
| INV-03 | Estado inicial sempre Draft | PR-BR-006 |
| INV-04 | Transições somente pela matriz da State Machine | PR-001-03 |
| INV-05 | `number` único por empresa, nunca reutilizado | PR-BR-001 |
| INV-06 | `totalEstimatedValue` = soma dos totais estimados dos itens | Consistência |
| INV-07 | Centro de custo obrigatório conforme modo configurado | PR-BR-014 |
| INV-08 | Estado terminal (Cancelled/Closed) não aceita nenhum comando | PR-001-03 |
| INV-09 | `version` incrementa a cada alteração persistida | PR-001-11 |

---

# 10. Comportamentos

## Purchase Requisition

Métodos de domínio:

Create()

AddItem()

RemoveItem()

UpdateItem()

Submit()

Approve()

Reject()

Return()

Cancel()

Close()

---

## Approval

Approve()

Reject()

Delegate()

Expire()

---

## Factory Methods

Criação sempre via fábricas do Aggregate — construtores nunca expostos:

| Factory | Assinatura | Garantias |
|---------|-----------|-----------|
| `PurchaseRequisition.Create` | (companyId, unitId, requesterId, priority, requiredDate, justification, costCenterId, projectId) | Valida empresa/unidade ativas, define Draft, gera número, publica RequisitionCreated |
| `PurchaseRequisitionItem.Create` | (itemType, description, quantity, unitOfMeasure, categoryId, estimatedPrice) | Valida Quantity VO, descrição e unidade |
| `Approval.CreateFromWorkflow` | (workflowDefinition, requisition) | Gera etapas conforme níveis do workflow |

---

# 11. Repositories

Interfaces de persistência do módulo (implementação em Infrastructure; o domínio não conhece o banco — ADR-009):

| Repositório | Operações |
|-------------|-----------|
| `IPurchaseRequisitionRepository` | `GetById(id)`, `GetByNumber(companyId, number)`, `Add(requisition)`, `Update(requisition)`, `Search(specification, keyset)` |
| `IPurchaseRequisitionItemRepository` | (acesso somente via Aggregate; nunca direto) |

Regras:

* Todo repositório aplica filtro `company_id` obrigatório (PR-BR-071).
* `Update` verifica `version` (Optimistic Concurrency).
* `Search` usa Keyset Pagination (PR-BR-081).
* Não existe `Delete` físico; Soft Delete via `deleted_at`.

---

# 12. Specifications

Consultas e validações expressas como especificações combináveis:

| Specification | Propósito | Uso |
|---------------|-----------|-----|
| `ReadyForSubmission` | Avalia PR-BR-020/021/022/023 conforme configuração | Submit (UC-003) |
| `RequisitionByNumber` | Filtro por número | UC-008 |
| `RequisitionByStatus` | Filtro por status | UC-008 |
| `RequisitionByRequester` | Filtro por solicitante | UC-008 |
| `RequisitionByCostCenter` | Filtro por centro de custo | UC-008 |
| `RequisitionByProject` | Filtro por projeto | UC-008 |
| `RequisitionByPriority` | Filtro por prioridade | UC-008 |
| `RequisitionByDateRange` | Filtro por período (DateRange VO) | UC-008 |
| `RequisitionWithinCompanyScope` | Escopo organizacional do usuário | Transversal |
| `AutoApprovable` | Valor <= limite configurado | PR-BR-031 |

Composição: `And`, `Or`, `Not` — base da regra PR-BR-082 (filtros combináveis).

---

# 13. Domain Services

Operações de domínio que não pertencem naturalmente a uma entidade:

| Serviço | Responsabilidade |
|---------|------------------|
| `RequisitionNumberGenerator` | Gera número único por empresa conforme máscara |
| `RequisitionSubmissionService` | Orquestra Submit: validações, workflow, eventos |
| `RequisitionValidationService` | Executa validações automáticas do estado Under Validation |
| `WorkflowResolver` | Localiza workflow aplicável ao contexto (PR-BR-030) via Workflow Engine |
| `RequisitionCancellationService` | Valida e executa cancelamento (PR-BR-050/051) |
| `EstimatedTotalCalculator` | Recalcula `totalEstimatedValue` (INV-06) |

---

# 14. Policies

Políticas de negócio reativas a eventos (detalhadas em PR-001-05, seção 6):

| Política | Gatilho | Ação |
|----------|---------|------|
| POL-001 | PurchaseRequisitionSubmitted | Iniciar fluxo de aprovação |
| POL-002 | Qualquer evento de domínio | Registrar na Timeline |
| POL-003 | Qualquer evento que modifique o Aggregate | Registrar auditoria |
| POL-004 | PurchaseRequisitionSubmitted | Notificar aprovadores |
| POL-005 | PurchaseRequisitionApproved | Disponibilizar para Procurement |
| POL-006 (deriva de PR-BR-031) | ValidationCompleted + AutoApprovable | Aprovar automaticamente |

---

# 15. Domain Events Produzidos

PurchaseRequisitionCreated

PurchaseRequisitionSubmitted

PurchaseRequisitionReturned

PurchaseRequisitionApproved

PurchaseRequisitionRejected

PurchaseRequisitionCancelled

PurchaseRequisitionClosed

ItemAdded

ItemRemoved

AttachmentAdded

CommentAdded

ApprovalStarted

ApprovalCompleted

---

# 16. Limites do Aggregate

Fazem parte do Aggregate:

- Purchase Requisition
- Item
- Attachment
- Comment
- Approval

Não fazem parte:

Fornecedor

Pedido de Compra

Cotação

Recebimento

Contrato

Estoque

Esses pertencem a outros Bounded Contexts.

---

# 17. Dependências

Foundation

Approval Engine

Notification Service

Audit Service

Timeline Service
