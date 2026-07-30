Excelente. Agora chegamos ao documento que, na minha opinião, diferencia um software comum de um software **Enterprise orientado a eventos**.

Mas antes, quero registrar uma **ADR (Architecture Decision)** que considero importante:

> **Os eventos documentados aqui serão eventos de negócio (Business Domain Events), e não eventos técnicos.**

Exemplos:

✅ `PurchaseRequisitionSubmitted`

❌ `RowUpdated`

❌ `RecordSaved`

❌ `APIExecuted`

Essa decisão fará toda a diferença quando evoluirmos para integrações, mensageria e automações.

---

# 📄 Arquivo

**Localização**

```text
docs/03-business/procurement/PR-001/05-event-storming.md
```

**Documento:** PR-001-05 — Event Storming
**Versão:** 1.0.0
**Status:** Approved

---

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

# 🏗️ Revisão de Arquitetura – Uma melhoria importante

Até aqui, nossa documentação está sólida, mas proponho uma evolução que será um diferencial para o Trino Supply.

## Introduzir um "Business Event Catalog"

Em vez de documentar os eventos apenas dentro de cada módulo, criaremos um catálogo corporativo de eventos reutilizáveis.

Exemplo:

```text
docs/
└── 03-business/
    └── foundation/
        └── event-catalog/
            ├── EVT-001-PurchaseRequisitionCreated.md
            ├── EVT-002-PurchaseRequisitionSubmitted.md
            ├── EVT-003-ApprovalStarted.md
            ├── EVT-004-PurchaseOrderCreated.md
            ├── EVT-005-GoodsReceived.md
            └── ...
```

Cada evento terá sua própria especificação, contendo:

* Nome e descrição.
* Aggregate de origem.
* Comando que o gera.
* Payload do evento.
* Consumidores.
* Políticas acionadas.
* Versão do evento.
* Compatibilidade.
* Regras de publicação.

