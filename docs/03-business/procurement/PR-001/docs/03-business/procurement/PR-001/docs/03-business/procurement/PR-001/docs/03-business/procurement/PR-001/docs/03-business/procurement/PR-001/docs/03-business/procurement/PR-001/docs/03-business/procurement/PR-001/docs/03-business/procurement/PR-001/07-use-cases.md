# PR-001-07 — Use Cases

> Especificação dos Casos de Uso do módulo Purchase Requisition.

---

# UC-001 — Criar Solicitação de Compra

## Objetivo

Permitir que um solicitante registre uma nova necessidade de aquisição.

## Atores

- Solicitante

## Pré-condições

- Usuário autenticado.
- Usuário com permissão para criar requisições.
- Empresa e unidade válidas.

## Gatilho

O usuário identifica uma necessidade de compra.

## Fluxo Principal

1. Selecionar "Nova Solicitação".
2. Informar os dados gerais.
3. Salvar a requisição.

## Fluxos Alternativos

A1. Cancelar criação.

## Exceções

E1. Usuário sem permissão.

## Pós-condições

- Requisição criada em estado **Draft**.

## Eventos

- PurchaseRequisitionCreated

## Regras

- PR-BR-001
- PR-BR-004
- PR-BR-006

---

# UC-002 — Adicionar Item

## Objetivo

Adicionar um item à requisição.

## Fluxo Principal

1. Selecionar "Adicionar Item".
2. Informar descrição.
3. Informar quantidade.
4. Informar unidade.
5. Confirmar.

## Validações

- Quantidade > 0.
- Descrição obrigatória.
- Unidade obrigatória.

## Eventos

- ItemAdded

## Regras

- PR-BR-010
- PR-BR-011
- PR-BR-012

---

# UC-003 — Enviar para Aprovação

## Objetivo

Submeter a requisição para validação e workflow.

## Fluxo Principal

1. Usuário seleciona "Enviar".
2. Sistema valida os dados.
3. Sistema identifica o workflow.
4. Sistema altera o status.
5. Sistema inicia aprovações.

## Fluxos Alternativos

A1. Campos obrigatórios ausentes.

A2. Workflow inexistente.

## Pós-condições

Status:

Submitted

## Eventos

- PurchaseRequisitionSubmitted
- ApprovalStarted

## Regras

- PR-BR-020
- PR-BR-030

---

# UC-004 — Aprovar Solicitação

## Objetivo

Registrar a aprovação de uma requisição.

## Ator

- Gestor

## Fluxo Principal

1. Abrir aprovação pendente.
2. Revisar informações.
3. Aprovar.
4. Registrar parecer.

## Pós-condições

Status:

Approved

ou

Waiting Approval (caso existam outros níveis).

## Eventos

- PurchaseRequisitionApproved

---

# UC-005 — Rejeitar Solicitação

## Objetivo

Rejeitar a requisição.

## Fluxo Principal

1. Abrir requisição.
2. Informar motivo.
3. Confirmar rejeição.

## Validação

Motivo obrigatório.

## Eventos

- PurchaseRequisitionRejected

---

# UC-006 — Retornar para Ajustes

## Objetivo

Solicitar correções ao solicitante.

## Resultado

Status:

Returned for Adjustment

Evento:

PurchaseRequisitionReturned

---

# UC-007 — Cancelar Solicitação

## Objetivo

Cancelar uma requisição.

## Restrições

Não possuir Pedido de Compra.

## Eventos

PurchaseRequisitionCancelled

---

# UC-008 — Consultar Solicitações

## Objetivo

Pesquisar requisições.

## Filtros

- Número
- Status
- Empresa
- Unidade
- Solicitante
- Centro de custo
- Projeto
- Prioridade
- Data

---

# UC-009 — Visualizar Timeline

## Objetivo

Consultar todo histórico da requisição.

Inclui:

- Aprovações
- Comentários
- Alterações
- Eventos
- Auditoria

---

# UC-010 — Anexar Documento

## Objetivo

Adicionar documentos de apoio.

Tipos:

- PDF
- DOCX
- XLSX
- Imagens

---

# UC-011 — Inserir Comentário

Permitir comunicação durante o processo.

---

# UC-012 — Copiar Requisição (Roadmap)

Criar nova requisição baseada em uma existente.

---

# UC-013 — Criar a partir de Template (Roadmap)

Criar utilizando modelos pré-configurados.

---

# UC-014 — Exportar PDF (Roadmap)

Gerar relatório da requisição.

---

# Matriz de Casos de Uso

| Caso de Uso | Solicitante | Gestor | Comprador | Administrador |
|--------------|-------------|---------|------------|---------------|
| UC-001 | ✔ | | | |
| UC-002 | ✔ | | | |
| UC-003 | ✔ | | | |
| UC-004 | | ✔ | | |
| UC-005 | | ✔ | | |
| UC-006 | | ✔ | | |
| UC-007 | ✔ | ✔ | | |
| UC-008 | ✔ | ✔ | ✔ | ✔ |
| UC-009 | ✔ | ✔ | ✔ | ✔ |
| UC-010 | ✔ | ✔ | | |
| UC-011 | ✔ | ✔ | ✔ | |
