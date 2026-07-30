# PR-001-08 — User Stories

> Especificação das User Stories do módulo Purchase Requisition.

---

# Epic

EP-001 — Gestão de Solicitações de Compra

---

# Capability

BC-001 — Gerenciar Solicitações de Compra

---

# Feature

FT-001 — Criação de Solicitações

---

## US-001 — Criar Solicitação

### Persona

Solicitante

### História

Como um solicitante,

Quero registrar uma nova solicitação de compra,

Para iniciar formalmente o processo de aquisição.

### Valor de Negócio

Eliminar solicitações informais e garantir rastreabilidade.

### Critérios de Aceite

```gherkin
Scenario: Criar solicitação com sucesso

Given que o usuário possui permissão

When preencher os dados obrigatórios

Then a solicitação deverá ser criada em Draft
```

### Regras

PR-BR-001

PR-BR-004

PR-BR-006

### Eventos

PurchaseRequisitionCreated

### Caso de Uso

UC-001

---

## US-002 — Adicionar Item

Persona

Solicitante

História

Como solicitante,

Quero adicionar itens,

Para descrever o que precisa ser adquirido.

Critérios

```gherkin
Scenario: Inserir item válido

Given uma requisição em Draft

When adicionar um item válido

Then o item deverá ser salvo
```

Regras

PR-BR-010

PR-BR-011

PR-BR-012

Evento

ItemAdded

Caso de Uso

UC-002

---

## US-003 — Enviar Solicitação

Persona

Solicitante

História

Como solicitante,

Quero enviar minha requisição,

Para iniciar o fluxo de aprovação.

Critérios

```gherkin
Scenario: Enviar com sucesso

Given uma requisição válida

When selecionar Enviar

Then iniciar workflow
```

Evento

PurchaseRequisitionSubmitted

ApprovalStarted

---

## US-004 — Aprovar Solicitação

Persona

Gestor

História

Como gestor,

Quero aprovar solicitações,

Para autorizar aquisições.

Critérios

```gherkin
Scenario: Aprovação

Given uma aprovação pendente

When aprovar

Then atualizar status
```

Evento

PurchaseRequisitionApproved

---

## US-005 — Rejeitar Solicitação

Como gestor,

Quero rejeitar solicitações,

Para impedir compras inadequadas.

Evento

PurchaseRequisitionRejected

---

## US-006 — Retornar para Ajustes

Como gestor,

Quero devolver para correção,

Para que o solicitante complemente informações.

Evento

PurchaseRequisitionReturned

---

## US-007 — Cancelar Solicitação

Como solicitante,

Quero cancelar minha solicitação,

Para encerrar uma necessidade que não existe mais.

Evento

PurchaseRequisitionCancelled

---

## US-008 — Consultar Solicitações

Como usuário autorizado,

Quero pesquisar solicitações,

Para localizar rapidamente informações.

---

## US-009 — Visualizar Timeline

Como usuário,

Quero visualizar todo histórico,

Para acompanhar o processo.

---

## US-010 — Inserir Comentários

Como participante do processo,

Quero registrar comentários,

Para facilitar comunicação.

---

## US-011 — Anexar Arquivos

Como solicitante,

Quero anexar documentos,

Para complementar a solicitação.

---

# Matriz de Rastreabilidade

| Story | UC | Regra | Evento |
|---------|-----|--------|---------|
| US-001 | UC-001 | PR-BR-001 | PurchaseRequisitionCreated |
| US-002 | UC-002 | PR-BR-010 | ItemAdded |
| US-003 | UC-003 | PR-BR-020 | PurchaseRequisitionSubmitted |
| US-004 | UC-004 | PR-BR-030 | PurchaseRequisitionApproved |
| US-005 | UC-005 | PR-BR-030 | PurchaseRequisitionRejected |
| US-006 | UC-006 | PR-BR-030 | PurchaseRequisitionReturned |
| US-007 | UC-007 | PR-BR-050 | PurchaseRequisitionCancelled |
