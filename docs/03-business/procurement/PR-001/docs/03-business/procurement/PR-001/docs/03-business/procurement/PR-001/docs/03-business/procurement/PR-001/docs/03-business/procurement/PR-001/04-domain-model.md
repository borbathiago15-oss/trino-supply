# PR-001-04 — Domain Model

> Este documento define o modelo de domínio do módulo Purchase Requisition.

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

# 3. Entidades

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

# 4. Value Objects

## Money

- Valor
- Moeda

Imutável.

---

## Quantity

- Quantidade
- Unidade

Imutável.

---

## Date Range

Utilizado em períodos.

---

## Address

Preparado para futuras integrações.

---

# 5. Enumerações

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

# 6. Relacionamentos

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

# 7. Invariantes

Uma requisição nunca poderá existir:

- sem solicitante
- sem empresa
- sem status
- sem data de criação

Uma requisição enviada deverá possuir:

- pelo menos um item

Uma requisição aprovada não poderá sofrer alterações estruturais.

---

# 8. Comportamentos

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

# 9. Domain Events Produzidos

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

# 10. Limites do Aggregate

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

# 11. Dependências

Foundation

Approval Engine

Notification Service

Audit Service

Timeline Service
