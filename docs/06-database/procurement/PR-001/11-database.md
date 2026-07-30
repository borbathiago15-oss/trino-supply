**Documento:** PR-001-11 — Database Model
**Versão:** 1.1.0
**Status:** Approved

> Decisão arquitetural associada: ADR-009 — Banco de dados como projeção do domínio (ver `docs/17-adr/ADR-009-database-as-domain-projection.md`).

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

# 14. Nota Arquitetural — Aggregate enxuto (proposta registrada)

Proposta de evolução para a próxima revisão deste modelo: manter no módulo apenas as entidades do próprio aggregate (`PurchaseRequisition` e `PurchaseRequisitionItem`) e substituir as tabelas `attachment`, `comment` e `approval` por referências aos serviços compartilhados do Foundation:

* Attachment → Document Management (FD-BC-003)
* Comment → Collaboration (FD-BC-008)
* Approval → Workflow Engine (FD-BC-004)
* Timeline / Audit / Notification → serviços transversais do Foundation

Modelo-alvo:

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

Benefícios esperados: reutilização entre módulos, menor acoplamento, evolução independente dos serviços e melhor aderência ao DDD.

Status: proposta em análise. Até decisão formal, o modelo físico das seções 4–7 permanece a referência vigente. A decisão será registrada em ADR própria.

---

# 15. DDL PostgreSQL (implementação física)

> Implementação física oficial do modelo das Seções 4–7. Schema: `procurement`. Todas as datas em UTC (`TIMESTAMPTZ` na implementação física, equivalente ao `TIMESTAMP` do modelo conceitual). FKs para entidades do Foundation (empresa, unidade, usuário, centro de custo, projeto, categoria) são lógicas/referenciais por serviço — ver §15.3.

## 15.1 Tabelas

```sql
CREATE SCHEMA IF NOT EXISTS procurement;

CREATE TABLE procurement.purchase_requisition (
    id                    UUID          NOT NULL DEFAULT gen_random_uuid(),
    company_id            UUID          NOT NULL,
    business_unit_id      UUID          NOT NULL,
    requester_id          UUID          NOT NULL,
    number                VARCHAR(30)   NOT NULL,
    status                SMALLINT      NOT NULL DEFAULT 1,
    priority              SMALLINT      NOT NULL DEFAULT 2,
    justification         TEXT          NOT NULL,
    required_date         DATE          NOT NULL,
    cost_center_id        UUID          NOT NULL,
    project_id            UUID          NULL,
    total_estimated_value NUMERIC(18,2) NOT NULL DEFAULT 0,
    created_at            TIMESTAMPTZ   NOT NULL DEFAULT now(),
    updated_at            TIMESTAMPTZ   NOT NULL DEFAULT now(),
    deleted_at            TIMESTAMPTZ   NULL,
    created_by            UUID          NOT NULL,
    updated_by            UUID          NOT NULL,
    deleted_by            UUID          NULL,
    version               INTEGER       NOT NULL DEFAULT 1,
    CONSTRAINT pk_purchase_requisition PRIMARY KEY (id),
    CONSTRAINT uq_pr_number_company UNIQUE (company_id, number),
    CONSTRAINT ck_pr_status CHECK (status BETWEEN 1 AND 10),
    CONSTRAINT ck_pr_priority CHECK (priority BETWEEN 1 AND 4),
    CONSTRAINT ck_pr_version CHECK (version >= 1),
    CONSTRAINT ck_pr_total CHECK (total_estimated_value >= 0),
    CONSTRAINT ck_pr_justification CHECK (char_length(justification) BETWEEN 10 AND 2000),
    CONSTRAINT ck_pr_soft_delete CHECK (
        (deleted_at IS NULL AND deleted_by IS NULL) OR
        (deleted_at IS NOT NULL AND deleted_by IS NOT NULL)
    )
);

CREATE TABLE procurement.purchase_requisition_item (
    id                      UUID          NOT NULL DEFAULT gen_random_uuid(),
    purchase_requisition_id UUID          NOT NULL,
    sequence                INTEGER       NOT NULL,
    item_type               SMALLINT      NOT NULL,
    description             TEXT          NOT NULL,
    quantity                NUMERIC(18,4) NOT NULL,
    unit_of_measure         VARCHAR(20)   NOT NULL,
    estimated_unit_price    NUMERIC(18,4) NULL,
    estimated_total_price   NUMERIC(18,2) NULL,
    category_id             UUID          NULL,
    project_id              UUID          NULL,
    cost_center_id          UUID          NULL,
    created_at              TIMESTAMPTZ   NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ   NOT NULL DEFAULT now(),
    deleted_at              TIMESTAMPTZ   NULL,
    created_by              UUID          NOT NULL,
    updated_by              UUID          NOT NULL,
    deleted_by              UUID          NULL,
    CONSTRAINT pk_pr_item PRIMARY KEY (id),
    CONSTRAINT fk_pr_item_requisition
        FOREIGN KEY (purchase_requisition_id)
        REFERENCES procurement.purchase_requisition (id),
    CONSTRAINT uq_pr_item_sequence UNIQUE (purchase_requisition_id, sequence),
    CONSTRAINT ck_pr_item_type CHECK (item_type IN (1, 2)), -- 1=Material, 2=Service
    CONSTRAINT ck_pr_item_quantity CHECK (quantity > 0),
    CONSTRAINT ck_pr_item_unit_price CHECK (estimated_unit_price IS NULL OR estimated_unit_price >= 0),
    CONSTRAINT ck_pr_item_total_price CHECK (estimated_total_price IS NULL OR estimated_total_price >= 0),
    CONSTRAINT ck_pr_item_sequence CHECK (sequence >= 1),
    CONSTRAINT ck_pr_item_description CHECK (char_length(description) BETWEEN 1 AND 500)
);

CREATE TABLE procurement.purchase_requisition_attachment (
    id                      UUID        NOT NULL DEFAULT gen_random_uuid(),
    purchase_requisition_id UUID        NOT NULL,
    file_name               VARCHAR(255) NOT NULL,
    storage_key             VARCHAR(512) NOT NULL,
    mime_type               VARCHAR(100) NOT NULL,
    file_size               BIGINT      NOT NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at              TIMESTAMPTZ NULL,
    created_by              UUID        NOT NULL,
    deleted_by              UUID        NULL,
    CONSTRAINT pk_pr_attachment PRIMARY KEY (id),
    CONSTRAINT fk_pr_attachment_requisition
        FOREIGN KEY (purchase_requisition_id)
        REFERENCES procurement.purchase_requisition (id),
    CONSTRAINT uq_pr_attachment_storage_key UNIQUE (storage_key),
    CONSTRAINT ck_pr_attachment_size CHECK (file_size > 0),
    CONSTRAINT ck_pr_attachment_mime CHECK (mime_type IN (
        'application/pdf',
        'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
        'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        'image/png', 'image/jpeg', 'image/webp'
    ))
);

CREATE TABLE procurement.purchase_requisition_comment (
    id                      UUID        NOT NULL DEFAULT gen_random_uuid(),
    purchase_requisition_id UUID        NOT NULL,
    author_id               UUID        NOT NULL,
    message                 TEXT        NOT NULL,
    internal                BOOLEAN     NOT NULL DEFAULT false,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at              TIMESTAMPTZ NULL,
    created_by              UUID        NOT NULL,
    deleted_by              UUID        NULL,
    CONSTRAINT pk_pr_comment PRIMARY KEY (id),
    CONSTRAINT fk_pr_comment_requisition
        FOREIGN KEY (purchase_requisition_id)
        REFERENCES procurement.purchase_requisition (id),
    CONSTRAINT ck_pr_comment_message CHECK (char_length(message) BETWEEN 1 AND 2000)
);

CREATE TABLE procurement.purchase_requisition_approval (
    id                      UUID        NOT NULL DEFAULT gen_random_uuid(),
    purchase_requisition_id UUID        NOT NULL,
    approver_id             UUID        NOT NULL,
    level                   INTEGER     NOT NULL,
    status                  SMALLINT    NOT NULL DEFAULT 1,
    decision                SMALLINT    NULL,
    comments                TEXT        NULL,
    decided_at              TIMESTAMPTZ NULL,
    delegated_by            UUID        NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID        NOT NULL,
    updated_by              UUID        NOT NULL,
    CONSTRAINT pk_pr_approval PRIMARY KEY (id),
    CONSTRAINT fk_pr_approval_requisition
        FOREIGN KEY (purchase_requisition_id)
        REFERENCES procurement.purchase_requisition (id),
    CONSTRAINT uq_pr_approval_level UNIQUE (purchase_requisition_id, level, approver_id),
    CONSTRAINT ck_pr_approval_status CHECK (status IN (1, 2, 3, 4)), -- 1=Pending, 2=Decided, 3=Cancelled, 4=Escalated
    CONSTRAINT ck_pr_approval_decision CHECK (decision IS NULL OR decision IN (1, 2, 3)), -- 1=Approved, 2=Rejected, 3=Returned
    CONSTRAINT ck_pr_approval_level CHECK (level >= 1),
    CONSTRAINT ck_pr_approval_decided CHECK (
        (decision IS NULL AND decided_at IS NULL) OR
        (decision IS NOT NULL AND decided_at IS NOT NULL)
    )
);
```

## 15.2 Índices (implementação física)

```sql
-- purchase_requisition (modelo conceitual Seção 6, expandido com padrão company_id-first)
CREATE INDEX idx_pr_number        ON procurement.purchase_requisition (company_id, number)        WHERE deleted_at IS NULL;
CREATE INDEX idx_pr_status        ON procurement.purchase_requisition (company_id, status)        WHERE deleted_at IS NULL;
CREATE INDEX idx_pr_company       ON procurement.purchase_requisition (company_id)                WHERE deleted_at IS NULL;
CREATE INDEX idx_pr_requester     ON procurement.purchase_requisition (company_id, requester_id)  WHERE deleted_at IS NULL;
CREATE INDEX idx_pr_required_date ON procurement.purchase_requisition (company_id, required_date) WHERE deleted_at IS NULL;
CREATE INDEX idx_pr_created_at    ON procurement.purchase_requisition (company_id, created_at DESC, id DESC) WHERE deleted_at IS NULL;
CREATE INDEX idx_pr_cost_center   ON procurement.purchase_requisition (company_id, cost_center_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_pr_project       ON procurement.purchase_requisition (company_id, project_id)    WHERE deleted_at IS NULL AND project_id IS NOT NULL;

-- purchase_requisition_item
CREATE INDEX idx_pr_item_requisition ON procurement.purchase_requisition_item (purchase_requisition_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_pr_item_category    ON procurement.purchase_requisition_item (category_id)             WHERE deleted_at IS NULL AND category_id IS NOT NULL;
CREATE INDEX idx_pr_item_project     ON procurement.purchase_requisition_item (project_id)              WHERE deleted_at IS NULL AND project_id IS NOT NULL;

-- attachment / comment / approval
CREATE INDEX idx_pr_attachment_requisition ON procurement.purchase_requisition_attachment (purchase_requisition_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_pr_comment_requisition    ON procurement.purchase_requisition_comment (purchase_requisition_id, created_at DESC, id DESC) WHERE deleted_at IS NULL;
CREATE INDEX idx_pr_approval_requisition   ON procurement.purchase_requisition_approval (purchase_requisition_id, level);
CREATE INDEX idx_pr_approval_pending       ON procurement.purchase_requisition_approval (approver_id, status) WHERE status = 1;
```

**Padrão:** todos os índices de consulta iniciam por `company_id` (isolamento multiempresa, Seção 9) e são parciais (`WHERE deleted_at IS NULL`) para não carregar lixo de soft delete.

## 15.3 Foreign Keys

| FK | Origem → Destino | Tipo | Justificativa |
| -- | ---------------- | ---- | ------------- |
| fk_pr_item_requisition | item → purchase_requisition | Física (dentro do módulo) | Integridade do aggregate |
| fk_pr_attachment_requisition | attachment → purchase_requisition | Física | Integridade do aggregate |
| fk_pr_comment_requisition | comment → purchase_requisition | Física | Integridade do aggregate |
| fk_pr_approval_requisition | approval → purchase_requisition | Física | Integridade do aggregate |
| company_id, business_unit_id, requester_id, cost_center_id, project_id, category_id | → Foundation (FD-001-01/02) | **Lógica** (sem constraint física) | Baixo acoplamento entre bounded contexts; validação na camada de aplicação/domínio; ADR-009 |

## 15.4 Triggers

```sql
-- TRG-001: manter updated_at
CREATE OR REPLACE FUNCTION procurement.fn_set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_pr_updated_at
    BEFORE UPDATE ON procurement.purchase_requisition
    FOR EACH ROW EXECUTE FUNCTION procurement.fn_set_updated_at();

CREATE TRIGGER trg_pr_item_updated_at
    BEFORE UPDATE ON procurement.purchase_requisition_item
    FOR EACH ROW EXECUTE FUNCTION procurement.fn_set_updated_at();

CREATE TRIGGER trg_pr_approval_updated_at
    BEFORE UPDATE ON procurement.purchase_requisition_approval
    FOR EACH ROW EXECUTE FUNCTION procurement.fn_set_updated_at();

-- TRG-002: incrementar version (optimistic concurrency) e impedir seu retrocesso
CREATE OR REPLACE FUNCTION procurement.fn_increment_version()
RETURNS TRIGGER AS $$
BEGIN
    IF NEW.version <> OLD.version + 1 THEN
        RAISE EXCEPTION 'PR-ERR-409: version must increment by 1 (expected %, got %)', OLD.version + 1, NEW.version
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_pr_version
    BEFORE UPDATE ON procurement.purchase_requisition
    FOR EACH ROW EXECUTE FUNCTION procurement.fn_increment_version();

-- TRG-003: imutabilidade de registros de decisão (approval decidida não pode ser alterada)
CREATE OR REPLACE FUNCTION procurement.fn_approval_immutable()
RETURNS TRIGGER AS $$
BEGIN
    IF OLD.status = 2 AND (NEW.decision IS DISTINCT FROM OLD.decision
                        OR NEW.decided_at IS DISTINCT FROM OLD.decided_at
                        OR NEW.approver_id IS DISTINCT FROM OLD.approver_id) THEN
        RAISE EXCEPTION 'Decided approval records are immutable'
            USING ERRCODE = 'raise_exception';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_pr_approval_immutable
    BEFORE UPDATE ON procurement.purchase_requisition_approval
    FOR EACH ROW EXECUTE FUNCTION procurement.fn_approval_immutable();
```

**Nota:** a auditoria de alterações (quem/o quê/quando) é registrada pelo domínio compartilhado (Seção 10); os triggers acima protegem apenas integridade técnica, não substituem a auditoria de negócio.

## 15.5 Views

```sql
-- VW-001: listagem operacional (sem colunas de soft delete)
CREATE VIEW procurement.vw_pr_list AS
SELECT
    pr.id,
    pr.company_id,
    pr.business_unit_id,
    pr.number,
    pr.status,
    pr.priority,
    pr.requester_id,
    pr.cost_center_id,
    pr.project_id,
    pr.required_date,
    pr.total_estimated_value,
    pr.created_at,
    pr.updated_at,
    (SELECT count(*) FROM procurement.purchase_requisition_item i
      WHERE i.purchase_requisition_id = pr.id AND i.deleted_at IS NULL) AS item_count,
    (SELECT count(*) FROM procurement.purchase_requisition_attachment a
      WHERE a.purchase_requisition_id = pr.id AND a.deleted_at IS NULL) AS attachment_count
FROM procurement.purchase_requisition pr
WHERE pr.deleted_at IS NULL;

-- VW-002: fila de aprovação por aprovador
CREATE VIEW procurement.vw_approval_queue AS
SELECT
    ap.id            AS approval_id,
    ap.approver_id,
    ap.level,
    pr.id            AS requisition_id,
    pr.company_id,
    pr.number,
    pr.priority,
    pr.requester_id,
    pr.total_estimated_value,
    pr.required_date,
    ap.created_at    AS pending_since
FROM procurement.purchase_requisition_approval ap
JOIN procurement.purchase_requisition pr
  ON pr.id = ap.purchase_requisition_id AND pr.deleted_at IS NULL
WHERE ap.status = 1;
```

## 15.6 Materialized Views

```sql
-- MV-001: painel gerencial por empresa/status (analytics leve; fonte: Timeline para KPIs oficiais)
CREATE MATERIALIZED VIEW procurement.mv_pr_status_summary AS
SELECT
    company_id,
    status,
    count(*)                AS total,
    sum(total_estimated_value) AS total_value,
    avg(EXTRACT(EPOCH FROM (updated_at - created_at))/3600) AS avg_hours_in_flow
FROM procurement.purchase_requisition
WHERE deleted_at IS NULL
GROUP BY company_id, status;

CREATE UNIQUE INDEX uq_mv_pr_status_summary
    ON procurement.mv_pr_status_summary (company_id, status);
```

**Estratégia de refresh:** `REFRESH MATERIALIZED VIEW CONCURRENTLY procurement.mv_pr_status_summary` a cada 15 minutos (job agendado) ou sob demanda. Indicadores oficiais do processo (PR-001-06 §10) são derivados da Timeline imutável, não desta view.

## 15.7 Stored Procedures / Functions de negócio

A regra do projeto é que **a lógica de negócio vive no domínio (.NET)**, não no banco (ADR-009). Funções em banco são admitidas apenas para:

| Função | Propósito | Justificativa |
| ------ | --------- | ------------- |
| `procurement.fn_next_pr_number(p_company_id UUID) RETURNS VARCHAR(30)` | Geração atômica do número sequencial por empresa (tabela de sequência com `SELECT ... FOR UPDATE`) | Numeração única e sem gaps concorrentes (UC-001) |
| Triggers §15.4 | Integridade técnica (updated_at, version, imutabilidade) | Proteção de última linha de defesa |

Qualquer nova procedure exige justificativa arquitetural registrada em ADR.

```sql
-- Sequência por empresa para o número da requisição
CREATE TABLE procurement.pr_number_sequence (
    company_id UUID        NOT NULL,
    next_value BIGINT      NOT NULL DEFAULT 1,
    CONSTRAINT pk_pr_number_sequence PRIMARY KEY (company_id)
);

CREATE OR REPLACE FUNCTION procurement.fn_next_pr_number(p_company_id UUID)
RETURNS VARCHAR(30) AS $$
DECLARE
    v_next BIGINT;
BEGIN
    INSERT INTO procurement.pr_number_sequence (company_id, next_value)
    VALUES (p_company_id, 2)
    ON CONFLICT (company_id)
    DO UPDATE SET next_value = procurement.pr_number_sequence.next_value + 1
    RETURNING next_value - 1 INTO v_next;

    RETURN 'PR-' || to_char(now() AT TIME ZONE 'UTC', 'YYYY') || '-' || lpad(v_next::text, 8, '0');
END;
$$ LANGUAGE plpgsql;
```

## 15.8 Estratégia de Particionamento

**Decisão para o MVP:** sem particionamento físico. Os volumes projetados (PR-001-01 §KPIs) não justificam a complexidade operacional.

**Gatilhos para ativar particionamento (revisão semestral):**

| Tabela | Gatilho | Estratégia-alvo |
| ------ | ------- | --------------- |
| purchase_requisition | > 10 milhões de linhas por empresa ou > 50 GB | `PARTITION BY LIST (company_id)` para grandes tenants; `PARTITION BY RANGE (created_at)` mensal dentro do tenant, quando aplicável |
| purchase_requisition_comment / approval | > 50 milhões de linhas | `PARTITION BY RANGE (created_at)` mensal com retenção por partição |
| pr_number_sequence | Nunca | Tabela de controle, volume desprezível |

**Regras:** particionamento nunca por chave de negócio editável; partição destacável para arquivamento (`DETACH PARTITION`) antes de qualquer purge legal; soft delete continua sendo a regra de exclusão lógica independentemente do particionamento.

## 15.9 Keyset Pagination (padrão de consulta)

Toda listagem da API usa keyset pagination (Seção 12), com cursor opaco e assinado.

**Chave de paginação oficial:** `(created_at DESC, id DESC)` — determinística, alinhada ao índice `idx_pr_created_at`.

```sql
-- Primeira página
SELECT id, number, status, priority, required_date, total_estimated_value, created_at
FROM procurement.purchase_requisition
WHERE company_id = :company_id
  AND deleted_at IS NULL
  AND (:status IS NULL OR status = :status)
ORDER BY created_at DESC, id DESC
LIMIT :page_size;

-- Página seguinte (cursor = created_at + id do último item)
SELECT id, number, status, priority, required_date, total_estimated_value, created_at
FROM procurement.purchase_requisition
WHERE company_id = :company_id
  AND deleted_at IS NULL
  AND (:status IS NULL OR status = :status)
  AND (created_at, id) < (:cursor_created_at, :cursor_id)
ORDER BY created_at DESC, id DESC
LIMIT :page_size;
```

**Regras:**

- `page_size` entre 1 e 100 (padrão 20).
- Cursor opaco, assinado (HMAC) e com expiração de 15 minutos; inválido/expirado → `PR-ERR-400`.
- Proibido `OFFSET` em qualquer endpoint (UC-008, UC-009).
- Filtros opcionais devem ser compatíveis com os índices §15.2 (filtro sem índice exige revisão do plano de indexação antes do deploy).

## 15.10 Plano de Indexação

| Consulta crítica | Índice de suporte | Cobertura |
| ---------------- | ----------------- | --------- |
| Listagem por empresa + ordenação temporal (UC-008) | `idx_pr_created_at` | Scan parcial por empresa, sem sort adicional |
| Filtro por status | `idx_pr_status` | Prefixo `company_id` garante seletividade |
| Busca por número | `uq_pr_number_company` / `idx_pr_number` | Lookup direto |
| Minhas requisições (solicitante) | `idx_pr_requester` | company + requester |
| Fila de aprovação (UC-004) | `idx_pr_approval_pending` (parcial `status = 1`) | Pequeno e quente |
| Detalhe com itens | `idx_pr_item_requisition` | FK indexada |
| Timeline de comentários | `idx_pr_comment_requisition` | Ordenação nativa por created_at |
| Relatório por centro de custo/projeto | `idx_pr_cost_center`, `idx_pr_project`, `idx_pr_item_category`, `idx_pr_item_project` | Filtros secundários |

**Governança de índices:**

- Todo novo índice exige: consulta-alvo documentada, validação com `EXPLAIN ANALYZE` em volume representativo e registro nesta tabela.
- Índices não utilizados são revistos trimestralmente (`pg_stat_user_indexes.idx_scan = 0` por 90 dias → candidato a remoção).
- `ANALYZE` automático (autovacuum) habilitado; `default_statistics_target = 100` nas colunas de filtro frequente.

## 15.11 Plano de Performance

| Tema | Diretriz |
| ---- | -------- |
| **Consultas** | Sempre paginadas (§15.9), filtradas por `company_id` primeiro, projeção apenas das colunas necessárias (sem `SELECT *` em listagens) |
| **N+1** | Detalhe da requisição carrega itens em uma única query por `purchase_requisition_id`; proibido loop de queries |
| **Transações** | Curtas; alteração do aggregate + outbox na mesma transação (PR-001-05 §14.1); nunca esperar I/O externo dentro de transação |
| **Locks** | Concorrência otimista via `version` (TRG-002); locks pessimistas apenas na geração de número (`fn_next_pr_number`) |
| **Isolation level** | `READ COMMITTED` padrão; `SERIALIZABLE` não utilizado |
| **Pool** | PgBouncer (transaction pooling); máx. de conexões por serviço configurado (`db.pool.max`, padrão 20) |
| **Timeouts** | `statement_timeout = 30s` em OLTP; `lock_timeout = 5s`; `idle_in_transaction_session_timeout = 60s` |
| **Metas** | Listagem paginada p95 < 200 ms; detalhe p95 < 150 ms; escrita p95 < 300 ms (alinhado aos RNFs do PR-001 README) |
| **Monitoramento** | `pg_stat_statements` habilitado; alerta para queries p95 acima da meta ou seq scans em tabelas > 100k linhas |
| **Manutenção** | Autovacuum ajustado para tabelas quentes (`purchase_requisition`: `autovacuum_vacuum_scale_factor = 0.05`); `REINDEX CONCURRENTLY` em janela de manutenção quando bloat > 30% |

## 15.12 Estratégia de Migração

| Aspecto | Diretriz |
| ------- | -------- |
| **Ferramenta** | Migrations versionadas via EF Core Migrations (assembly do módulo Procurement), aplicadas por pipeline CI/CD — nunca manual em produção |
| **Formato** | Uma migration por alteração lógica; nome `YYYYMMDDHHMM_<descricao>`; SQL gerado revisado em PR |
| **Ordem** | Expand-and-contract: (1) adicionar nova estrutura compatível; (2) migrar dados; (3) remover estrutura antiga em release posterior |
| **Backward compatibility** | Toda migration deve ser compatível com a versão N-1 da aplicação (deploy blue/green sem downtime) |
| **Operações online** | Índices com `CREATE INDEX CONCURRENTLY`; alterações de tipo de coluna via nova coluna + backfill + swap |
| **Backfill** | Em lotes (`batch_size = 10.000`), com `statement_timeout` elevado apenas no job, monitorado e pausável |
| **Rollback** | Toda migration possui `Down()` testado; rollback de dados irreversível exige aprovação explícita |
| **Seeds** | Dados de referência (sequências, configurações padrão) em migrations idempotentes |
| **Ambientes** | dev → staging (réplica de produção anonimizada) → produção; nunca pular staging |

## 15.13 Política de Backup

| Aspecto | Diretriz |
| ------- | -------- |
| **Backup completo** | Diário, `pg_dump` em formato custom ou snapshot de volume (conforme plataforma), retenção 30 dias |
| **WAL archiving (PITR)** | Arquivamento contínuo de WAL; Point-in-Time Recovery com granularidade de 5 minutos, retenção 7 dias |
| **Backup semanal** | Retenção de 12 semanas |
| **Backup mensal** | Retenção de 12 meses (conformidade e auditoria) |
| **Criptografia** | Backups criptografados em repouso (AES-256) e em trânsito (TLS); chaves gerenciadas fora do banco (KMS/cofre) |
| **Teste de restore** | Restore completo em ambiente isolado **mensal**, com evidência registrada; RPO ≤ 5 min, RTO ≤ 4 h |
| **Soft delete como proteção** | Exclusões lógicas recuperáveis pela aplicação; backup é última linha, não ferramenta de undo operacional |
| **Localidade** | Réplica em região/zona distinta da primária |

## 15.14 Versionamento de Schema

| Aspecto | Diretriz |
| ------- | -------- |
| **Versão do schema** | Derivada da última migration aplicada (`__EFMigrationsHistory`); exposta no health check do serviço (`/health` inclui `schemaVersion`) |
| **Compatibilidade aplicação × schema** | O serviço valida na inicialização que o schema mínimo exigido está aplicado; incompatibilidade impede o start (fail-fast) |
| **Rastreabilidade** | Toda migration referencia o documento de origem (este documento ou ADR); alterações de modelo exigem atualização deste documento **antes** da migration |
| **Multiempresa** | Um único schema lógico por módulo; isolamento por `company_id` (Seção 9), nunca schema por tenant |
| **Evolução registrada** | Mudanças de modelo físico seguem o fluxo de governança (GOV-002): proposta → revisão arquitetural → ADR (se estrutural) → atualização deste documento → migration |

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | — | Arquitetura Trino | Versão inicial aprovada |
| 1.1.0 | 2026-07-30 | Arquitetura Trino | Adicionada Seção 15 — Implementação física: DDL PostgreSQL completo das 5 tabelas + tabela de sequência, constraints e check constraints, FKs físicas e lógicas, índices (padrão company_id-first, parciais), triggers (updated_at, version, imutabilidade de decisões), views, materialized views, stored procedures admitidas, estratégia de particionamento, keyset pagination com SQL de referência, plano de indexação, plano de performance, estratégia de migração (expand-and-contract), política de backup (PITR, retenções, teste de restore) e versionamento de schema. Modelo conceitual das Seções 1–14 integralmente preservado |
