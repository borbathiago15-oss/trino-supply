Excelente. Agora chegamos ao documento que, na minha opinião, é onde a maioria dos projetos começa errado.

Normalmente as equipes fazem:

> Banco → API → Tela

Nós faremos:

> Negócio → Domínio → Dados

Isso muda completamente a qualidade da modelagem.

Além disso, quero propor outra decisão importante.

---

# 🏗️ ADR-009 — Banco orientado ao Domínio

O banco de dados **não será o modelo do sistema**.

O banco será **uma projeção persistente do modelo de domínio**.

Isso significa que:

* O domínio manda.
* O banco implementa.
* Nunca modelaremos tabelas pensando na tela.

---

# 📄 Arquivo

**Localização**

```text
docs/06-database/procurement/PR-001/11-database.md
```

**Documento:** PR-001-11 — Database Model
**Versão:** 1.0.0
**Status:** Approved

---

# PR-001-11 — Database Model

> Modelo de persistência do módulo Purchase Requisition.

---

# 1. Objetivo

Definir a estrutura de persistência responsável pelo armazenamento das Solicitações de Compra e seus componentes.

Este documento representa a implementação física do modelo de domínio descrito em **PR-001-04**.

---

# 2. Princípios

A modelagem deverá seguir os seguintes princípios:

* PostgreSQL como banco oficial.
* UUID como chave primária.
* Soft Delete.
* Auditoria completa.
* Multiempresa.
* Timezone UTC.
* Versionamento otimista.
* Integridade referencial.

---

# 3. Convenções

## Chaves Primárias

```sql
id UUID PRIMARY KEY
```

---

## Datas

```text
created_at
updated_at
deleted_at
```

---

## Auditoria

```text
created_by
updated_by
deleted_by
```

---

## Controle de Concorrência

```text
version INTEGER
```

Utilizado para **Optimistic Concurrency**.

---

# 4. Entidades

## purchase_requisition

### Chave

```text
id
```

### Campos

| Campo                 | Tipo           |
| --------------------- | -------------- |
| id                    | UUID           |
| company_id            | UUID           |
| business_unit_id      | UUID           |
| requester_id          | UUID           |
| number                | VARCHAR(30)    |
| status                | SMALLINT       |
| priority              | SMALLINT       |
| justification         | TEXT           |
| required_date         | DATE           |
| cost_center_id        | UUID           |
| project_id            | UUID           |
| total_estimated_value | NUMERIC(18,2)  |
| created_at            | TIMESTAMP      |
| updated_at            | TIMESTAMP      |
| deleted_at            | TIMESTAMP NULL |
| version               | INTEGER        |

---

## purchase_requisition_item

| Campo                   | Tipo          |
| ----------------------- | ------------- |
| id                      | UUID          |
| purchase_requisition_id | UUID          |
| sequence                | INTEGER       |
| item_type               | SMALLINT      |
| description             | TEXT          |
| quantity                | NUMERIC(18,4) |
| unit_of_measure         | VARCHAR(20)   |
| estimated_unit_price    | NUMERIC(18,4) |
| estimated_total_price   | NUMERIC(18,2) |
| category_id             | UUID          |
| project_id              | UUID          |
| cost_center_id          | UUID          |

---

## purchase_requisition_attachment

| Campo                   | Tipo    |
| ----------------------- | ------- |
| id                      | UUID    |
| purchase_requisition_id | UUID    |
| file_name               | VARCHAR |
| storage_key             | VARCHAR |
| mime_type               | VARCHAR |
| file_size               | BIGINT  |

---

## purchase_requisition_comment

| Campo                   | Tipo      |
| ----------------------- | --------- |
| id                      | UUID      |
| purchase_requisition_id | UUID      |
| author_id               | UUID      |
| message                 | TEXT      |
| internal                | BOOLEAN   |
| created_at              | TIMESTAMP |

---

## purchase_requisition_approval

| Campo                   | Tipo      |
| ----------------------- | --------- |
| id                      | UUID      |
| purchase_requisition_id | UUID      |
| approver_id             | UUID      |
| level                   | INTEGER   |
| status                  | SMALLINT  |
| decision                | SMALLINT  |
| comments                | TEXT      |
| decided_at              | TIMESTAMP |

---

# 5. Relacionamentos

```
Purchase Requisition

1

↓

N

Purchase Requisition Item

↓

N

Attachment

↓

N

Comment

↓

N

Approval
```

---

# 6. Índices

## purchase_requisition

```sql
idx_pr_number

idx_pr_status

idx_pr_company

idx_pr_requester

idx_pr_required_date

idx_pr_created_at

idx_pr_cost_center
```

---

## purchase_requisition_item

```sql
idx_pr_item_category

idx_pr_item_project
```

---

# 7. Constraints

Número único por empresa.

Quantidade > 0.

Status válido.

Versão >= 1.

---

# 8. Soft Delete

Todas as entidades utilizarão:

```text
deleted_at

deleted_by
```

Nunca exclusão física.

---

# 9. Multiempresa

Toda tabela principal deverá possuir:

```text
company_id
```

Nenhuma consulta poderá ignorar esse filtro.

---

# 10. Auditoria

A auditoria operacional ficará em um domínio compartilhado.

Não serão criadas tabelas de histórico por módulo.

---

# 11. Versionamento

Toda atualização incrementa:

```text
version
```

Utilizado para:

* concorrência
* APIs
* eventos

---

# 12. Performance

Consultas deverão ser sempre:

* paginadas
* indexadas
* filtráveis

Evitar:

OFFSET elevado.

Preferir:

Keyset Pagination.

---

# 13. Dependências

Foundation

Identity

Audit

Notification

Workflow

---

# 🚨 Revisão Arquitetural – O ponto onde eu mudaria completamente o projeto

Até agora nossa documentação está muito boa, mas identifiquei um problema estrutural que, se não corrigirmos agora, poderá gerar retrabalho quando o sistema crescer.

## O erro

Estamos modelando tudo dentro do módulo **Purchase Requisition**.

Na prática, várias entidades descritas aqui pertencem a outros domínios da plataforma.

Por exemplo:

### Não pertencem ao módulo

* **Attachment** → Foundation / Document Management
* **Comment** → Foundation / Collaboration
* **Approval** → Workflow Engine
* **Timeline** → Timeline Service
* **Audit** → Audit Service
* **Notification** → Notification Center

O módulo de Requisição deve apenas manter referências a esses componentes ou consumir seus serviços.

## Como eu remodelaria

A entidade central do módulo ficaria muito mais enxuta:

```text
PurchaseRequisition
├── PurchaseRequisitionItem
└── Referências para:
    ├── WorkflowInstance
    ├── DocumentCollection
    ├── ConversationThread
    ├── AuditTrail
    └── Timeline
```

### Benefícios

* Reutilização em todos os módulos.
* Menor acoplamento.
* Evolução independente de cada serviço.
* Facilidade para integrações.
* Melhor aderência ao DDD.
* Escalabilidade para novos produtos da plataforma Trino.

---
