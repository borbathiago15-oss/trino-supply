# MMS-004-11 — Database Model

**Documento:** MMS-004-11 — Database Model
**Módulo:** MMS-004 — Inventory Management (Estoque / Almoxarifado)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-23
**Dependências:** MMS-004 v1.0.0, MMS-004-02 (Business Rules), MMS-004-03 (State Machine), MMS-004-04 (Domain Model), MMS-004-05 (Event Storming), MMS-004-07 (Use Cases), ADR-009, ADR-010, FD-001-02, FD-001-09
**Referências:** MMS-002-11 e PR-001-11 (padrão de formato Enterprise), GOV-001

> Decisão arquitetural associada: ADR-009 — Banco de dados como projeção do domínio.
> Modelo de persistência do módulo Inventory Management.

---

# 1. Objetivo

Definir a estrutura de persistência responsável pelo armazenamento dos documentos de movimentação, reservas, ajustes, inventários, locais de armazenagem, alertas e da **projeção de saldo** do estoque.

Este documento representa a implementação física do modelo de domínio descrito em **MMS-004-04**. A decisão estrutural central do módulo vale integralmente no banco: **`stock_balance` é projeção derivada** — nenhum caminho físico (API, job, procedure ou acesso direto) altera saldo sem documento confirmado (MMS-P-08; INV-IV-01; prova de integridade do DoD do módulo).

---

# 2. Princípios

A modelagem deverá seguir os seguintes princípios:

* PostgreSQL como banco oficial.
* UUID como chave primária.
* Soft Delete (apenas para rascunhos cancelados e locais; documentos confirmados são imutáveis, nunca excluídos).
* Auditoria completa (com saldo anterior/posterior por linha — IV-BR-095).
* Multiempresa.
* Timezone UTC.
* Versionamento otimista.
* Integridade referencial.
* Aggregate enxuto: item, empresa, motivo de ajuste e solicitação são referências lógicas (MMS-002, FD-001-02, FD-001-09, MMS-003); auditoria/timeline via Foundation — nunca tabelas próprias.
* Saldo nunca negativo e sempre derivado: constraints de última linha de defesa no banco (INV-IV-04/05).

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

Utilizado para **Optimistic Concurrency** (IV-BR-090; INV-IV-15). A serialização de confirmações sobre a mesma chave de saldo usa lock por linha da projeção (`SELECT ... FOR UPDATE` em `stock_balance`) dentro da transação de confirmação.

---

# 4. Entidades

## stock_movement

Documento de movimentação — a única porta de alteração de saldo (MMS-P-07/08).

| Campo | Tipo |
| ----- | ---- |
| id | UUID |
| company_id | UUID |
| number | VARCHAR(30) |
| movement_type | SMALLINT |
| origin_type | SMALLINT |
| origin_reference | VARCHAR(100) |
| reservation_id | UUID NULL |
| adjustment_id | UUID NULL |
| reverses_id | UUID NULL |
| reversed_by_id | UUID NULL |
| reason | VARCHAR(500) NULL |
| status | SMALLINT |
| confirmed_at | TIMESTAMP NULL |
| confirmed_by | UUID NULL |
| created_at / updated_at / deleted_at | TIMESTAMP |
| created_by / updated_by / deleted_by | UUID |
| version | INTEGER |

---

## stock_movement_line

Linha de efeito do documento, com snapshot de saldo anterior/posterior gravado na confirmação (IV-BR-095 — o extrato reconstrói a trilha sem consultar o Audit Service).

| Campo | Tipo |
| ----- | ---- |
| id | UUID |
| movement_id | UUID |
| item_id | UUID |
| size_code | VARCHAR(20) NULL |
| quantity | NUMERIC(18,4) |
| from_location_id | UUID NULL |
| to_location_id | UUID NULL |
| client_id | UUID NULL |
| contract_id | UUID NULL |
| balance_before_total / balance_before_reserved | NUMERIC(18,4) NULL |
| balance_after_total / balance_after_reserved | NUMERIC(18,4) NULL |
| created_at | TIMESTAMP |

---

## reservation

| Campo | Tipo |
| ----- | ---- |
| id | UUID |
| company_id | UUID |
| number | VARCHAR(30) |
| requisition_reference | VARCHAR(100) NULL |
| item_id | UUID |
| size_code | VARCHAR(20) NULL |
| quantity | NUMERIC(18,4) |
| fulfilled_quantity | NUMERIC(18,4) |
| location_id | UUID |
| client_id / contract_id | UUID NULL |
| status | SMALLINT |
| expires_at | TIMESTAMP |
| released_reason | VARCHAR(500) NULL |
| created_at / updated_at | TIMESTAMP |
| created_by / updated_by | UUID |
| version | INTEGER |

---

## adjustment

| Campo | Tipo |
| ----- | ---- |
| id | UUID |
| company_id | UUID |
| number | VARCHAR(30) |
| reason_type_id | UUID |
| justification | VARCHAR(1000) |
| status | SMALLINT |
| inventory_count_id | UUID NULL |
| registered_by | UUID |
| approved_by | UUID NULL |
| decided_at | TIMESTAMP NULL |
| rejection_reason | VARCHAR(500) NULL |
| movement_id | UUID NULL |
| created_at / updated_at | TIMESTAMP |
| created_by / updated_by | UUID |
| version | INTEGER |

---

## adjustment_line

| Campo | Tipo |
| ----- | ---- |
| id | UUID |
| adjustment_id | UUID |
| item_id | UUID |
| size_code | VARCHAR(20) NULL |
| quantity_delta | NUMERIC(18,4) |
| location_id | UUID |
| client_id / contract_id | UUID NULL |
| created_at | TIMESTAMP |

---

## inventory_count

| Campo | Tipo |
| ----- | ---- |
| id | UUID |
| company_id | UUID |
| number | VARCHAR(30) |
| scope_type | SMALLINT |
| scope_classes | VARCHAR(10) NULL |
| status | SMALLINT |
| responsible_id | UUID |
| deadline | DATE |
| started_at / closed_at | TIMESTAMP NULL |
| counted_keys / divergent_keys / adjusted_keys / within_tolerance_keys | INTEGER NULL |
| accuracy_rate | NUMERIC(5,2) NULL |
| cancel_reason | VARCHAR(500) NULL |
| created_at / updated_at / deleted_at | TIMESTAMP |
| created_by / updated_by / deleted_by | UUID |
| version | INTEGER |

---

## inventory_count_location

Escopo de locais do inventário (N:N).

| Campo | Tipo |
| ----- | ---- |
| inventory_count_id | UUID (PK/FK) |
| location_id | UUID (PK/FK) |

---

## count_entry

Lançamento de contagem — nunca altera saldo (IV-BR-100; INV-IV-11).

| Campo | Tipo |
| ----- | ---- |
| id | UUID |
| inventory_count_id | UUID |
| item_id | UUID |
| size_code | VARCHAR(20) NULL |
| location_id | UUID |
| counted_qty | NUMERIC(18,4) |
| system_qty_snapshot | NUMERIC(18,4) |
| counted_by | UUID |
| counted_at | TIMESTAMP |

---

## location

| Campo | Tipo |
| ----- | ---- |
| id | UUID |
| company_id | UUID |
| location_type | SMALLINT |
| code | VARCHAR(30) |
| description | VARCHAR(200) |
| parent_id | UUID NULL |
| active | BOOLEAN |
| created_at / updated_at / deleted_at | TIMESTAMP |
| created_by / updated_by / deleted_by | UUID |
| version | INTEGER |

---

## stock_balance (projeção — não editável)

| Campo | Tipo |
| ----- | ---- |
| id | UUID |
| company_id | UUID |
| item_id | UUID |
| size_code | VARCHAR(20) NULL |
| location_id | UUID |
| client_id / contract_id | UUID NULL |
| total_qty | NUMERIC(18,4) |
| reserved_qty | NUMERIC(18,4) |
| available_qty | NUMERIC(18,4) GENERATED |
| avg_cost_reference | NUMERIC(18,4) NULL |
| last_movement_id | UUID |
| last_movement_at | TIMESTAMP |
| version | INTEGER |

---

## stock_alert

| Campo | Tipo |
| ----- | ---- |
| id | UUID |
| company_id | UUID |
| alert_type | SMALLINT |
| item_id | UUID |
| size_code | VARCHAR(20) NULL |
| location_id | UUID |
| status | SMALLINT |
| triggered_by_movement_id | UUID NULL |
| available_qty_at_trigger / min_stock_at_trigger | NUMERIC(18,4) NULL |
| acknowledged_by | UUID NULL |
| acknowledged_at | TIMESTAMP NULL |
| acknowledge_comment | VARCHAR(500) NULL |
| opened_at / normalized_at | TIMESTAMP |

---

## document_sequence

Numeração sequencial por empresa e tipo de documento (padrão PR-001: número sequencial único por empresa).

| Campo | Tipo |
| ----- | ---- |
| company_id | UUID (PK) |
| document_kind | VARCHAR(10) (PK) |
| last_value | BIGINT |

---

# 5. Relacionamentos

```
StockMovement 1 ─── N StockMovementLine
StockMovement 1 ─── 0..1 StockMovement   (estorno: reverses_id / reversed_by_id)
StockMovement N ─── 0..1 Reservation      (saída por atendimento baixa reserva)
StockMovement 0..1 ─ 0..1 Adjustment      (documento de efeito do ajuste aprovado)

Adjustment 1 ─── N AdjustmentLine
Adjustment N ─── 0..1 InventoryCount      (divergência de inventário)

InventoryCount 1 ─── N InventoryCountLocation ─── N Location
InventoryCount 1 ─── N CountEntry

Location 1 ─── N Location                 (hierarquia por parent_id, sem ciclos)
Location 1 ─── N StockBalance

Referências lógicas (sem FK física):

*.item_id → MMS-002 Item Catalog (item; estado e grade validados no domínio)
*.size_code → FD-001-09 Master Data (grade de tamanhos do item)
*.company_id → FD-001-01/02 Foundation (empresa)
*.client_id / contract_id → FD-001-02 / cadastros comerciais (segregação MMS-RG-10)
adjustment.reason_type_id → FD-001-09 Master Data (motivos estruturados)
reservation.requisition_reference → MMS-003 Material Requisition
stock_movement.origin_reference → MMS-005 / MMS-003 / documento externo
```

---

# 6. Índices

## stock_movement

```sql
idx_movement_number
idx_movement_status
idx_movement_type
idx_movement_origin
idx_movement_confirmed_at
idx_movement_reservation
```

## stock_movement_line

```sql
idx_movement_line_movement
idx_movement_line_item
idx_movement_line_locations
```

## reservation

```sql
idx_reservation_status
idx_reservation_item
idx_reservation_requisition
idx_reservation_expiring
```

## adjustment / inventory_count / count_entry / location / stock_balance / stock_alert

```sql
idx_adjustment_status
idx_adjustment_inventory
idx_count_status
idx_count_entry_count
idx_location_parent
idx_location_code
uq_stock_balance_key
idx_stock_balance_item
idx_stock_alert_open
```

---

# 7. Constraints

Número sequencial único por empresa e tipo de documento.

Status válidos por entidade (matrizes MMS-004-03; transições protegidas por trigger).

Quantidade > 0 em linhas de movimentação; contagem ≥ 0; delta de ajuste ≠ 0.

Saldo nunca negativo: `total_qty ≥ 0`, `reserved_qty ≥ 0`, `reserved_qty ≤ total_qty` (INV-IV-04/05).

`available_qty` é coluna gerada (`total_qty − reserved_qty`) — jamais escrita (MMS-RG-09).

`fulfilled_quantity ≤ quantity` na reserva (INV-IV-07).

Transferência: `from_location_id ≠ to_location_id` e ambos informados (IV-BR-050).

Locais: código único por empresa; `parent_id` obrigatório para depósito/endereço e proibido para almoxarifado; hierarquia sem ciclos (validada no domínio + trigger de profundidade).

Documento confirmado imutável; correção somente por estorno (trigger TRG-IV-003).

Versão >= 1.

---

# 8. Soft Delete

```text
deleted_at

deleted_by
```

Aplica-se **somente** a: rascunhos cancelados de documento (ST-IV-004), inventários cancelados (ST-IV-033) e locais. Documentos **Confirmados e Estornados nunca** recebem soft delete — são imutáveis e permanecem consultáveis no extrato para sempre (IV-BR-095; MMS-P-08). Reservas e ajustes nunca são excluídos (estados terminais preservam histórico).

---

# 9. Multiempresa

Toda tabela principal deverá possuir:

```text
company_id
```

Nenhuma consulta poderá ignorar esse filtro. O saldo nunca é compartilhado entre empresas (INH-IV-004); acesso a recurso de outra empresa responde 404 (anti-enumeração — IV-ERR-404).

---

# 10. Auditoria

A auditoria operacional ficará em um domínio compartilhado (FD-001-06), **incluindo o saldo anterior/posterior de toda confirmação** (IV-BR-095).

Não serão criadas tabelas de histórico por módulo. O snapshot de saldo gravado em `stock_movement_line` serve ao extrato operacional (UC-IV-011); a trilha probatória é a do Audit Service.

---

# 11. Versionamento

Toda atualização incrementa:

```text
version
```

Utilizado para:

* concorrência (IV-BR-090)
* APIs (`If-Match`)
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

A leitura de posição usa a projeção `stock_balance` + cache Redis (`materials.inventory.cache.ttl`), nunca agregação sobre as linhas de movimentação em tempo de requisição (IV-BR-096: a projeção nunca é fonte de verdade — a fonte são os documentos confirmados; divergência dispara COMP-IV-004).

---

# 13. Dependências

Foundation

Identity (FD-001-01)

Organization (FD-001-02 — empresa/unidade dos almoxarifados)

Master Data (FD-001-09 — motivos de ajuste, grades de tamanho, unidades)

Audit (FD-001-06 — saldo anterior/posterior)

Timeline (FD-001-07)

Notification (FD-001-05)

MMS-002 Item Catalog (item_id, parâmetros de reposição para alertas)

---

# 14. Nota Arquitetural — Saldo como projeção derivada (adotada)

O Inventory Management aplica a ADR-009 na sua forma mais estrita:

* `stock_balance` **não é tabela de negócio editável** — é projeção materializada dos documentos confirmados, atualizada exclusivamente pelo `StockBalanceService` na mesma transação da confirmação (INV-IV-01).
* Não existe endpoint, procedure, job ou rota administrativa que escreva saldo. A carga inicial entra por documento de entrada auditado (MMS-004-01 §11.6).
* O papel de aplicação do banco (`trino_inventory_app`) possui `INSERT/UPDATE` em `stock_balance`; **nenhum outro papel** possui escrita — a prova de integridade do DoD verifica os privilégios em pipeline.
* Reconciliação: a projeção pode ser **reconstruída** a qualquer momento a partir de `stock_movement_line` confirmadas (COMP-IV-004); divergência detectada é auditada.

Modelo:

```text
StockMovement (aggregate root)
├── StockMovementLine (entidade interna, com snapshot de saldo)
Reservation (aggregate root)
Adjustment (aggregate root)
├── AdjustmentLine
InventoryCount (aggregate root)
├── CountEntry
Location (aggregate root, hierarquia)
StockBalance (projeção de leitura — sem aggregate, sem comandos)
StockAlert (estado operacional de alerta — BO-IV-008)
└── Referências lógicas para:
    ├── Item (MMS-002) · Requisition (MMS-003) · Receiving (MMS-005)
    ├── MasterDataValue (motivos, grades) · Company (FD-001-02)
    ├── AuditTrail (FD-001-06) e Timeline (FD-001-07)
```

---

# 15. DDL PostgreSQL (implementação física)

> Implementação física oficial do modelo das Seções 4–7. Schema: `materials` (compartilhado com MMS-002 — ADR-012). Todas as datas em UTC (`TIMESTAMPTZ`). FKs para entidades de outros bounded contexts são lógicas — ver §15.3.

## 15.1 Tabelas

```sql
CREATE SCHEMA IF NOT EXISTS materials;

CREATE TABLE materials.stock_movement (
    id               UUID          NOT NULL DEFAULT gen_random_uuid(),
    company_id       UUID          NOT NULL,
    number           VARCHAR(30)   NOT NULL,
    movement_type    SMALLINT      NOT NULL, -- 1=Entrada, 2=Saída, 3=Transferência, 4=Liberação de Reserva, 5=Estorno
    origin_type      SMALLINT      NOT NULL, -- 1=Recebimento, 2=Devolução, 3=Ajuste, 4=Carga inicial, 5=Atendimento, 6=Consumo, 7=Operação, 8=Inventário, 9=Estorno, 10=Vencimento de reserva
    origin_reference VARCHAR(100)  NOT NULL, -- IV-BR-002/013 (documento de origem referenciável)
    reservation_id   UUID          NULL,     -- saída por atendimento (IV-BR-033)
    adjustment_id    UUID          NULL,     -- efeito de ajuste aprovado (IV-BR-080..086)
    reverses_id      UUID          NULL,     -- este documento estorna... (IV-BR-110)
    reversed_by_id   UUID          NULL,     -- ...este documento foi estornado por
    reason           VARCHAR(500)  NULL,     -- obrigatório em estorno (IV-BR-112, mín. 10 chars no domínio)
    status           SMALLINT      NOT NULL DEFAULT 1, -- 1=Rascunho, 2=Confirmado, 3=Estornado, 4=Cancelado (ST-IV-001..004)
    confirmed_at     TIMESTAMPTZ   NULL,
    confirmed_by     UUID          NULL,
    created_at       TIMESTAMPTZ   NOT NULL DEFAULT now(),
    updated_at       TIMESTAMPTZ   NOT NULL DEFAULT now(),
    deleted_at       TIMESTAMPTZ   NULL,
    created_by       UUID          NOT NULL,
    updated_by       UUID          NOT NULL,
    deleted_by       UUID          NULL,
    version          INTEGER       NOT NULL DEFAULT 1,
    CONSTRAINT pk_stock_movement PRIMARY KEY (id),
    CONSTRAINT fk_movement_reverses      FOREIGN KEY (reverses_id)    REFERENCES materials.stock_movement (id),
    CONSTRAINT fk_movement_reversed_by   FOREIGN KEY (reversed_by_id) REFERENCES materials.stock_movement (id),
    CONSTRAINT ck_movement_type   CHECK (movement_type BETWEEN 1 AND 5),
    CONSTRAINT ck_movement_origin CHECK (origin_type BETWEEN 1 AND 10),
    CONSTRAINT ck_movement_status CHECK (status IN (1, 2, 3, 4)),
    CONSTRAINT ck_movement_version CHECK (version >= 1),
    CONSTRAINT ck_movement_confirmed CHECK (
        (status IN (2, 3) AND confirmed_at IS NOT NULL AND confirmed_by IS NOT NULL) OR
        (status IN (1, 4))
    ),
    CONSTRAINT ck_movement_reversal CHECK (
        (movement_type = 5 AND reverses_id IS NOT NULL AND reason IS NOT NULL) OR
        (movement_type <> 5 AND reverses_id IS NULL)
    ), -- IV-BR-110/112
    CONSTRAINT ck_movement_soft_delete CHECK (
        (deleted_at IS NULL AND deleted_by IS NULL) OR
        (deleted_at IS NOT NULL AND deleted_by IS NOT NULL AND status = 4)
    ) -- soft delete apenas de rascunho cancelado (Seção 8)
);

-- Número sequencial único por empresa (padrão PR-001)
CREATE UNIQUE INDEX uq_movement_number_company
    ON materials.stock_movement (company_id, number);

-- Idempotência de origem: um documento por origem funcional (IV-BR-090; FA-IV-005)
CREATE UNIQUE INDEX uq_movement_origin
    ON materials.stock_movement (company_id, origin_type, origin_reference)
    WHERE deleted_at IS NULL AND origin_type IN (1, 4, 8, 10); -- origens sistêmicas de documento único

CREATE TABLE materials.stock_movement_line (
    id                      UUID          NOT NULL DEFAULT gen_random_uuid(),
    movement_id             UUID          NOT NULL,
    item_id                 UUID          NOT NULL,
    size_code               VARCHAR(20)   NULL,     -- obrigatório quando item com grade (IV-BR-120; validação no domínio)
    quantity                NUMERIC(18,4) NOT NULL,
    from_location_id        UUID          NULL,
    to_location_id          UUID          NULL,
    client_id               UUID          NULL,     -- segregação (IV-BR-070)
    contract_id             UUID          NULL,
    balance_before_total    NUMERIC(18,4) NULL,     -- snapshots gravados na confirmação (IV-BR-095)
    balance_before_reserved NUMERIC(18,4) NULL,
    balance_after_total     NUMERIC(18,4) NULL,
    balance_after_reserved  NUMERIC(18,4) NULL,
    created_at              TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT pk_stock_movement_line PRIMARY KEY (id),
    CONSTRAINT fk_movement_line_movement FOREIGN KEY (movement_id) REFERENCES materials.stock_movement (id),
    CONSTRAINT fk_movement_line_from     FOREIGN KEY (from_location_id) REFERENCES materials.location (id),
    CONSTRAINT fk_movement_line_to       FOREIGN KEY (to_location_id)   REFERENCES materials.location (id),
    CONSTRAINT ck_movement_line_qty CHECK (quantity > 0), -- IV-BR-011
    CONSTRAINT ck_movement_line_locations CHECK (
        from_location_id IS NOT NULL OR to_location_id IS NOT NULL
    ),
    CONSTRAINT ck_movement_line_transfer CHECK (
        from_location_id IS NULL OR to_location_id IS NULL OR from_location_id <> to_location_id
    ) -- IV-BR-050 (origem ≠ destino)
);

CREATE TABLE materials.reservation (
    id                    UUID          NOT NULL DEFAULT gen_random_uuid(),
    company_id            UUID          NOT NULL,
    number                VARCHAR(30)   NOT NULL,
    requisition_reference VARCHAR(100)  NULL,     -- MMS-003; NULL apenas em reserva manual (IV-BR-032)
    item_id               UUID          NOT NULL,
    size_code             VARCHAR(20)   NULL,
    quantity              NUMERIC(18,4) NOT NULL,
    fulfilled_quantity    NUMERIC(18,4) NOT NULL DEFAULT 0,
    location_id           UUID          NOT NULL,
    client_id             UUID          NULL,
    contract_id           UUID          NULL,
    status                SMALLINT      NOT NULL DEFAULT 1, -- 1=Ativa, 2=Atendida, 3=Liberada, 4=Vencida (ST-IV-010..013)
    expires_at            TIMESTAMPTZ   NOT NULL, -- criação + reservation.ttl (IV-BR-036)
    released_reason       VARCHAR(500)  NULL,
    created_at            TIMESTAMPTZ   NOT NULL DEFAULT now(),
    updated_at            TIMESTAMPTZ   NOT NULL DEFAULT now(),
    created_by            UUID          NOT NULL,
    updated_by            UUID          NOT NULL,
    version               INTEGER       NOT NULL DEFAULT 1,
    CONSTRAINT pk_reservation PRIMARY KEY (id),
    CONSTRAINT fk_reservation_location FOREIGN KEY (location_id) REFERENCES materials.location (id),
    CONSTRAINT ck_reservation_status CHECK (status IN (1, 2, 3, 4)),
    CONSTRAINT ck_reservation_qty CHECK (quantity > 0),
    CONSTRAINT ck_reservation_fulfilled CHECK (fulfilled_quantity >= 0 AND fulfilled_quantity <= quantity), -- INV-IV-07 / IV-BR-034
    CONSTRAINT ck_reservation_version CHECK (version >= 1)
);

CREATE UNIQUE INDEX uq_reservation_number_company
    ON materials.reservation (company_id, number);

-- Reserva ativa única por chave funcional (INV-IV-06)
CREATE UNIQUE INDEX uq_reservation_active_key
    ON materials.reservation (company_id, requisition_reference, item_id,
                              COALESCE(size_code, ''), location_id)
    WHERE status = 1 AND requisition_reference IS NOT NULL;

CREATE TABLE materials.adjustment (
    id                 UUID          NOT NULL DEFAULT gen_random_uuid(),
    company_id         UUID          NOT NULL,
    number             VARCHAR(30)   NOT NULL,
    reason_type_id     UUID          NOT NULL, -- FD-001-09 (motivos estruturados — IV-BR-081)
    justification      VARCHAR(1000) NOT NULL, -- IV-BR-080
    status             SMALLINT      NOT NULL DEFAULT 1, -- 1=Pendente, 2=Aprovado, 3=Rejeitado, 4=Estornado (ST-IV-020..023)
    inventory_count_id UUID          NULL,     -- divergência de inventário (IV-BR-101)
    registered_by      UUID          NOT NULL,
    approved_by        UUID          NULL,
    decided_at         TIMESTAMPTZ   NULL,
    rejection_reason   VARCHAR(500)  NULL,
    movement_id        UUID          NULL,     -- documento de efeito, após aprovação
    created_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    updated_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    created_by         UUID          NOT NULL,
    updated_by         UUID          NOT NULL,
    version            INTEGER       NOT NULL DEFAULT 1,
    CONSTRAINT pk_adjustment PRIMARY KEY (id),
    CONSTRAINT fk_adjustment_count    FOREIGN KEY (inventory_count_id) REFERENCES materials.inventory_count (id),
    CONSTRAINT fk_adjustment_movement FOREIGN KEY (movement_id) REFERENCES materials.stock_movement (id),
    CONSTRAINT ck_adjustment_status CHECK (status IN (1, 2, 3, 4)),
    CONSTRAINT ck_adjustment_justification CHECK (char_length(justification) >= 10),
    CONSTRAINT ck_adjustment_sod CHECK (approved_by IS NULL OR approved_by <> registered_by), -- IV-BR-085 (última linha de defesa; SoD completo no domínio)
    CONSTRAINT ck_adjustment_decision CHECK (
        (status = 1 AND approved_by IS NULL) OR
        (status = 2 AND approved_by IS NOT NULL AND decided_at IS NOT NULL AND movement_id IS NOT NULL) OR
        (status = 3 AND approved_by IS NOT NULL AND decided_at IS NOT NULL AND rejection_reason IS NOT NULL AND movement_id IS NULL) OR -- IV-BR-086
        (status = 4)
    ),
    CONSTRAINT ck_adjustment_version CHECK (version >= 1)
);

CREATE UNIQUE INDEX uq_adjustment_number_company
    ON materials.adjustment (company_id, number);

CREATE TABLE materials.adjustment_line (
    id             UUID          NOT NULL DEFAULT gen_random_uuid(),
    adjustment_id  UUID          NOT NULL,
    item_id        UUID          NOT NULL,
    size_code      VARCHAR(20)   NULL,
    quantity_delta NUMERIC(18,4) NOT NULL, -- positivo aumenta, negativo reduz (IV-BR-084 no domínio)
    location_id    UUID          NOT NULL,
    client_id      UUID          NULL,
    contract_id    UUID          NULL,
    created_at     TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT pk_adjustment_line PRIMARY KEY (id),
    CONSTRAINT fk_adjustment_line_adjustment FOREIGN KEY (adjustment_id) REFERENCES materials.adjustment (id),
    CONSTRAINT fk_adjustment_line_location   FOREIGN KEY (location_id) REFERENCES materials.location (id),
    CONSTRAINT ck_adjustment_line_delta CHECK (quantity_delta <> 0)
);

CREATE TABLE materials.inventory_count (
    id                    UUID         NOT NULL DEFAULT gen_random_uuid(),
    company_id            UUID         NOT NULL,
    number                VARCHAR(30)  NOT NULL,
    scope_type            SMALLINT     NOT NULL, -- 1=Geral, 2=Cíclico por classe ABC (IV-BR-100)
    scope_classes         VARCHAR(10)  NULL,     -- ex.: 'A', 'A,B' (obrigatório quando cíclico)
    status                SMALLINT     NOT NULL DEFAULT 1, -- 1=Aberto, 2=Em Contagem, 3=Fechado, 4=Cancelado (ST-IV-030..033)
    responsible_id        UUID         NOT NULL,
    deadline              DATE         NOT NULL,
    started_at            TIMESTAMPTZ  NULL,
    closed_at             TIMESTAMPTZ  NULL,
    counted_keys          INTEGER      NULL,
    divergent_keys        INTEGER      NULL,
    adjusted_keys         INTEGER      NULL,
    within_tolerance_keys INTEGER      NULL,
    accuracy_rate         NUMERIC(5,2) NULL, -- KPI acuracidade ≥ 98% (IV-BR-103)
    cancel_reason         VARCHAR(500) NULL,
    created_at            TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at            TIMESTAMPTZ  NOT NULL DEFAULT now(),
    deleted_at            TIMESTAMPTZ  NULL,
    created_by            UUID         NOT NULL,
    updated_by            UUID         NOT NULL,
    deleted_by            UUID         NULL,
    version               INTEGER      NOT NULL DEFAULT 1,
    CONSTRAINT pk_inventory_count PRIMARY KEY (id),
    CONSTRAINT ck_count_status CHECK (status IN (1, 2, 3, 4)),
    CONSTRAINT ck_count_scope CHECK (
        (scope_type = 1) OR (scope_type = 2 AND scope_classes IS NOT NULL)
    ),
    CONSTRAINT ck_count_closed CHECK (status <> 3 OR (closed_at IS NOT NULL AND accuracy_rate IS NOT NULL)),
    CONSTRAINT ck_count_cancelled CHECK (status <> 4 OR cancel_reason IS NOT NULL),
    CONSTRAINT ck_count_version CHECK (version >= 1)
);

CREATE UNIQUE INDEX uq_count_number_company
    ON materials.inventory_count (company_id, number);

CREATE TABLE materials.inventory_count_location (
    inventory_count_id UUID NOT NULL,
    location_id        UUID NOT NULL,
    CONSTRAINT pk_inventory_count_location PRIMARY KEY (inventory_count_id, location_id),
    CONSTRAINT fk_icl_count    FOREIGN KEY (inventory_count_id) REFERENCES materials.inventory_count (id),
    CONSTRAINT fk_icl_location FOREIGN KEY (location_id) REFERENCES materials.location (id)
);

CREATE TABLE materials.count_entry (
    id                  UUID          NOT NULL DEFAULT gen_random_uuid(),
    inventory_count_id  UUID          NOT NULL,
    item_id             UUID          NOT NULL,
    size_code           VARCHAR(20)   NULL,
    location_id         UUID          NOT NULL,
    counted_qty         NUMERIC(18,4) NOT NULL,
    system_qty_snapshot NUMERIC(18,4) NOT NULL, -- saldo sistêmico no instante da contagem (IV-BR-100)
    counted_by          UUID          NOT NULL,
    counted_at          TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT pk_count_entry PRIMARY KEY (id),
    CONSTRAINT fk_count_entry_count    FOREIGN KEY (inventory_count_id) REFERENCES materials.inventory_count (id),
    CONSTRAINT fk_count_entry_location FOREIGN KEY (location_id) REFERENCES materials.location (id),
    CONSTRAINT ck_count_entry_qty CHECK (counted_qty >= 0)
);

-- Uma contagem vigente por chave de saldo no inventário (recontagem substitui via nova linha; a última prevalece no domínio)
CREATE INDEX idx_count_entry_key
    ON materials.count_entry (inventory_count_id, item_id, COALESCE(size_code, ''), location_id, counted_at DESC);

CREATE TABLE materials.location (
    id            UUID         NOT NULL DEFAULT gen_random_uuid(),
    company_id    UUID         NOT NULL,
    location_type SMALLINT     NOT NULL, -- 1=Almoxarifado, 2=Depósito, 3=Endereço (IV-BR-060)
    code          VARCHAR(30)  NOT NULL,
    description   VARCHAR(200) NOT NULL,
    parent_id     UUID         NULL,
    active        BOOLEAN      NOT NULL DEFAULT true,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    deleted_at    TIMESTAMPTZ  NULL,
    created_by    UUID         NOT NULL,
    updated_by    UUID         NOT NULL,
    deleted_by    UUID         NULL,
    version       INTEGER      NOT NULL DEFAULT 1,
    CONSTRAINT pk_location PRIMARY KEY (id),
    CONSTRAINT fk_location_parent FOREIGN KEY (parent_id) REFERENCES materials.location (id),
    CONSTRAINT ck_location_type CHECK (location_type IN (1, 2, 3)),
    CONSTRAINT ck_location_hierarchy CHECK (
        (location_type = 1 AND parent_id IS NULL) OR
        (location_type IN (2, 3) AND parent_id IS NOT NULL)
    ), -- IV-BR-062 (tipo do pai validado no domínio + trigger)
    CONSTRAINT ck_location_version CHECK (version >= 1)
);

-- Código único por empresa (IV-BR-061), soft-delete-ciente
CREATE UNIQUE INDEX uq_location_code_company
    ON materials.location (company_id, code)
    WHERE deleted_at IS NULL;

CREATE TABLE materials.stock_balance (
    id                 UUID          NOT NULL DEFAULT gen_random_uuid(),
    company_id         UUID          NOT NULL,
    item_id            UUID          NOT NULL,
    size_code          VARCHAR(20)   NULL,
    location_id        UUID          NOT NULL,
    client_id          UUID          NULL,
    contract_id        UUID          NULL,
    total_qty          NUMERIC(18,4) NOT NULL DEFAULT 0,
    reserved_qty       NUMERIC(18,4) NOT NULL DEFAULT 0,
    available_qty      NUMERIC(18,4) GENERATED ALWAYS AS (total_qty - reserved_qty) STORED, -- MMS-RG-09 / INV-IV-05
    avg_cost_reference NUMERIC(18,4) NULL, -- referência gerencial (MMS-001 §8.3); não é valorização fiscal
    last_movement_id   UUID          NOT NULL,
    last_movement_at   TIMESTAMPTZ   NOT NULL,
    version            INTEGER       NOT NULL DEFAULT 1,
    CONSTRAINT pk_stock_balance PRIMARY KEY (id),
    CONSTRAINT fk_stock_balance_location FOREIGN KEY (location_id) REFERENCES materials.location (id),
    CONSTRAINT fk_stock_balance_movement FOREIGN KEY (last_movement_id) REFERENCES materials.stock_movement (id),
    CONSTRAINT ck_balance_total_nonneg    CHECK (total_qty >= 0),    -- MMS-RG-04 / INV-IV-04
    CONSTRAINT ck_balance_reserved_nonneg CHECK (reserved_qty >= 0),
    CONSTRAINT ck_balance_reserved_bound  CHECK (reserved_qty <= total_qty),
    CONSTRAINT ck_balance_version CHECK (version >= 1)
);

-- Uma projeção por chave de saldo (INV — chave: empresa, item, tamanho?, local, segregação?)
CREATE UNIQUE INDEX uq_stock_balance_key
    ON materials.stock_balance (company_id, item_id, COALESCE(size_code, ''),
                                location_id,
                                COALESCE(client_id,  '00000000-0000-0000-0000-000000000000'::uuid),
                                COALESCE(contract_id,'00000000-0000-0000-0000-000000000000'::uuid));

CREATE TABLE materials.stock_alert (
    id                       UUID          NOT NULL DEFAULT gen_random_uuid(),
    company_id               UUID          NOT NULL,
    alert_type               SMALLINT      NOT NULL, -- 1=Mínimo, 2=Ruptura (IV-BR-090..091)
    item_id                  UUID          NOT NULL,
    size_code                VARCHAR(20)   NULL,
    location_id              UUID          NOT NULL,
    status                   SMALLINT      NOT NULL DEFAULT 1, -- 1=Aberto, 2=Normalizado (BO-IV-008)
    triggered_by_movement_id UUID          NULL,
    available_qty_at_trigger NUMERIC(18,4) NULL,
    min_stock_at_trigger     NUMERIC(18,4) NULL,
    acknowledged_by          UUID          NULL,
    acknowledged_at          TIMESTAMPTZ   NULL,
    acknowledge_comment      VARCHAR(500)  NULL,
    opened_at                TIMESTAMPTZ   NOT NULL DEFAULT now(),
    normalized_at            TIMESTAMPTZ   NULL,
    CONSTRAINT pk_stock_alert PRIMARY KEY (id),
    CONSTRAINT fk_alert_location FOREIGN KEY (location_id) REFERENCES materials.location (id),
    CONSTRAINT fk_alert_movement FOREIGN KEY (triggered_by_movement_id) REFERENCES materials.stock_movement (id),
    CONSTRAINT ck_alert_type CHECK (alert_type IN (1, 2)),
    CONSTRAINT ck_alert_status CHECK (status IN (1, 2)),
    CONSTRAINT ck_alert_normalized CHECK (status <> 2 OR normalized_at IS NOT NULL)
);

-- Deduplicação: um alerta Aberto por tipo × chave de saldo (IV-BR-091)
CREATE UNIQUE INDEX uq_stock_alert_open
    ON materials.stock_alert (company_id, alert_type, item_id, COALESCE(size_code, ''), location_id)
    WHERE status = 1;

CREATE TABLE materials.document_sequence (
    company_id    UUID        NOT NULL,
    document_kind VARCHAR(10) NOT NULL, -- 'MOV', 'RSV', 'ADJ', 'INV'
    last_value    BIGINT      NOT NULL DEFAULT 0,
    CONSTRAINT pk_document_sequence PRIMARY KEY (company_id, document_kind)
);
```

## 15.2 Índices (implementação física)

```sql
-- stock_movement (padrão company_id-first; parciais onde o recorte é frequente)
CREATE INDEX idx_movement_number       ON materials.stock_movement (company_id, number);
CREATE INDEX idx_movement_status       ON materials.stock_movement (company_id, status)        WHERE deleted_at IS NULL;
CREATE INDEX idx_movement_type         ON materials.stock_movement (company_id, movement_type) WHERE deleted_at IS NULL;
CREATE INDEX idx_movement_origin       ON materials.stock_movement (company_id, origin_type, origin_reference);
CREATE INDEX idx_movement_confirmed_at ON materials.stock_movement (company_id, confirmed_at DESC, id DESC) WHERE status IN (2, 3);
CREATE INDEX idx_movement_reservation  ON materials.stock_movement (reservation_id) WHERE reservation_id IS NOT NULL;

-- stock_movement_line (extrato por item — UC-IV-011)
CREATE INDEX idx_movement_line_movement  ON materials.stock_movement_line (movement_id);
CREATE INDEX idx_movement_line_item      ON materials.stock_movement_line (item_id, created_at DESC, id DESC);
CREATE INDEX idx_movement_line_from      ON materials.stock_movement_line (from_location_id) WHERE from_location_id IS NOT NULL;
CREATE INDEX idx_movement_line_to        ON materials.stock_movement_line (to_location_id)   WHERE to_location_id IS NOT NULL;

-- reservation (fila do almoxarifado, job de vencimento, janela de alerta)
CREATE INDEX idx_reservation_status      ON materials.reservation (company_id, status);
CREATE INDEX idx_reservation_item        ON materials.reservation (company_id, item_id) WHERE status = 1;
CREATE INDEX idx_reservation_requisition ON materials.reservation (company_id, requisition_reference) WHERE requisition_reference IS NOT NULL;
CREATE INDEX idx_reservation_expiring    ON materials.reservation (expires_at) WHERE status = 1; -- TMR-IV-001/002

-- adjustment (fila de aprovação)
CREATE INDEX idx_adjustment_status    ON materials.adjustment (company_id, status, created_at DESC);
CREATE INDEX idx_adjustment_inventory ON materials.adjustment (inventory_count_id) WHERE inventory_count_id IS NOT NULL;

-- inventory_count / count_entry
CREATE INDEX idx_count_status      ON materials.inventory_count (company_id, status) WHERE deleted_at IS NULL;
CREATE INDEX idx_count_entry_count ON materials.count_entry (inventory_count_id);

-- location
CREATE INDEX idx_location_parent ON materials.location (parent_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_location_active ON materials.location (company_id, location_type) WHERE deleted_at IS NULL AND active;

-- stock_balance (posição e validação de disponibilidade — IV-BR-020; p95 < 2s)
CREATE INDEX idx_stock_balance_item     ON materials.stock_balance (company_id, item_id);
CREATE INDEX idx_stock_balance_location ON materials.stock_balance (company_id, location_id);
CREATE INDEX idx_stock_balance_below_zero_available ON materials.stock_balance (company_id, item_id) WHERE available_qty <= 0;

-- stock_alert (fila de alertas — UC-IV-010)
CREATE INDEX idx_stock_alert_open ON materials.stock_alert (company_id, status, alert_type, opened_at DESC);
```

**Padrão:** índices de consulta iniciam por `company_id` (isolamento multiempresa, Seção 9); parciais onde o subconjunto quente é pequeno (reservas ativas, alertas abertos, documentos confirmados).

## 15.3 Foreign Keys

| FK | Origem → Destino | Tipo | Justificativa |
| -- | ---------------- | ---- | ------------- |
| fk_movement_line_movement, fk_adjustment_line_adjustment, fk_count_entry_count, fk_icl_* | linhas → aggregate root | Física (dentro do módulo) | Integridade do aggregate |
| fk_movement_reverses / fk_movement_reversed_by | stock_movement → stock_movement | Física | Vínculo de estorno (IV-BR-110) |
| fk_adjustment_movement, fk_movement_line_from/to, fk_reservation_location, fk_stock_balance_* , fk_alert_* | entre aggregates do módulo | Física | Consistência interna do bounded context |
| item_id (todas) | → MMS-002 Item Catalog | **Lógica** | Bounded context distinto; estado do item e grade validados no domínio (IV-BR-010/120); ADR-009 |
| company_id, client_id, contract_id | → Foundation (FD-001-01/02) | **Lógica** | Baixo acoplamento; validação na aplicação |
| reason_type_id | → Master Data (FD-001-09) | **Lógica** | Motivos estruturados (IV-BR-081) |
| requisition_reference, origin_reference | → MMS-003 / MMS-005 / externo | **Lógica** | Referência de documento entre módulos (IV-BR-002/013) |

## 15.4 Triggers

```sql
-- TRG-IV-001: manter updated_at (mesma função do módulo — materials.fn_set_updated_at, criada no MMS-002-11)
CREATE TRIGGER trg_movement_updated_at
    BEFORE UPDATE ON materials.stock_movement
    FOR EACH ROW EXECUTE FUNCTION materials.fn_set_updated_at();
CREATE TRIGGER trg_reservation_updated_at
    BEFORE UPDATE ON materials.reservation
    FOR EACH ROW EXECUTE FUNCTION materials.fn_set_updated_at();
CREATE TRIGGER trg_adjustment_updated_at
    BEFORE UPDATE ON materials.adjustment
    FOR EACH ROW EXECUTE FUNCTION materials.fn_set_updated_at();
CREATE TRIGGER trg_count_updated_at
    BEFORE UPDATE ON materials.inventory_count
    FOR EACH ROW EXECUTE FUNCTION materials.fn_set_updated_at();
CREATE TRIGGER trg_location_updated_at
    BEFORE UPDATE ON materials.location
    FOR EACH ROW EXECUTE FUNCTION materials.fn_set_updated_at();

-- TRG-IV-002: incrementar version (optimistic concurrency — materials.fn_increment_version do MMS-002-11)
CREATE TRIGGER trg_movement_version
    BEFORE UPDATE ON materials.stock_movement
    FOR EACH ROW EXECUTE FUNCTION materials.fn_increment_version();
CREATE TRIGGER trg_reservation_version
    BEFORE UPDATE ON materials.reservation
    FOR EACH ROW EXECUTE FUNCTION materials.fn_increment_version();
CREATE TRIGGER trg_adjustment_version
    BEFORE UPDATE ON materials.adjustment
    FOR EACH ROW EXECUTE FUNCTION materials.fn_increment_version();
CREATE TRIGGER trg_count_version
    BEFORE UPDATE ON materials.inventory_count
    FOR EACH ROW EXECUTE FUNCTION materials.fn_increment_version();
CREATE TRIGGER trg_location_version
    BEFORE UPDATE ON materials.location
    FOR EACH ROW EXECUTE FUNCTION materials.fn_increment_version();

-- TRG-IV-003: documento confirmado é imutável (MMS-P-08; IV-BR-110)
-- Única transição permitida a partir de Confirmado: receber o vínculo de estorno (status 2→3 + reversed_by_id)
CREATE OR REPLACE FUNCTION materials.fn_movement_immutable()
RETURNS TRIGGER AS $$
BEGIN
    IF OLD.status IN (3, 4) THEN
        RAISE EXCEPTION 'IV-ERR-110: reversed/cancelled movements are immutable (terminal state)'
            USING ERRCODE = 'raise_exception';
    END IF;
    IF OLD.status = 2 THEN
        IF NEW.status = 3 AND NEW.reversed_by_id IS NOT NULL
           AND NEW.movement_type = OLD.movement_type
           AND NEW.origin_reference = OLD.origin_reference THEN
            RETURN NEW; -- transição de estorno
        END IF;
        RAISE EXCEPTION 'IV-ERR-110: confirmed movements are immutable; use reversal'
            USING ERRCODE = 'raise_exception';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_movement_immutable
    BEFORE UPDATE ON materials.stock_movement
    FOR EACH ROW EXECUTE FUNCTION materials.fn_movement_immutable();

-- TRG-IV-004: linha de movimentação confirmada é imutável; DELETE físico proibido
CREATE OR REPLACE FUNCTION materials.fn_movement_line_guard()
RETURNS TRIGGER AS $$
DECLARE v_status SMALLINT;
BEGIN
    SELECT status INTO v_status FROM materials.stock_movement
     WHERE id = COALESCE(NEW.movement_id, OLD.movement_id);
    IF TG_OP = 'DELETE' THEN
        IF v_status <> 1 THEN
            RAISE EXCEPTION 'IV-ERR-110: lines of non-draft movements cannot be deleted';
        END IF;
        RETURN OLD;
    END IF;
    IF TG_OP = 'UPDATE' AND v_status <> 1
       AND (NEW.quantity <> OLD.quantity OR NEW.item_id <> OLD.item_id) THEN
        -- snapshots de saldo são gravados pelo serviço na transação de confirmação
        IF OLD.balance_after_total IS NOT NULL THEN
            RAISE EXCEPTION 'IV-ERR-110: confirmed movement lines are immutable';
        END IF;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_movement_line_guard
    BEFORE UPDATE OR DELETE ON materials.stock_movement_line
    FOR EACH ROW EXECUTE FUNCTION materials.fn_movement_line_guard();

-- TRG-IV-005: saldo só muda com documento (INV-IV-01 — última linha de defesa)
-- Toda escrita em stock_balance exige last_movement_id novo e existente com status Confirmado/Estornado
CREATE OR REPLACE FUNCTION materials.fn_balance_requires_movement()
RETURNS TRIGGER AS $$
DECLARE v_status SMALLINT;
BEGIN
    IF TG_OP = 'UPDATE'
       AND NEW.last_movement_id = OLD.last_movement_id
       AND (NEW.total_qty <> OLD.total_qty OR NEW.reserved_qty <> OLD.reserved_qty) THEN
        RAISE EXCEPTION 'IV-ERR-001: balance change requires a new confirmed movement (MMS-P-08)';
    END IF;
    SELECT status INTO v_status FROM materials.stock_movement WHERE id = NEW.last_movement_id;
    IF v_status IS NULL OR v_status NOT IN (2, 3) THEN
        RAISE EXCEPTION 'IV-ERR-001: balance can only reference confirmed movements';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_balance_requires_movement
    BEFORE INSERT OR UPDATE ON materials.stock_balance
    FOR EACH ROW EXECUTE FUNCTION materials.fn_balance_requires_movement();

-- DELETE físico proibido na projeção e nos documentos
CREATE OR REPLACE FUNCTION materials.fn_forbid_delete()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'IV-ERR-001: physical delete is forbidden in inventory tables';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_movement_no_delete  BEFORE DELETE ON materials.stock_movement  FOR EACH ROW EXECUTE FUNCTION materials.fn_forbid_delete();
CREATE TRIGGER trg_reservation_no_delete BEFORE DELETE ON materials.reservation   FOR EACH ROW EXECUTE FUNCTION materials.fn_forbid_delete();
CREATE TRIGGER trg_adjustment_no_delete  BEFORE DELETE ON materials.adjustment    FOR EACH ROW EXECUTE FUNCTION materials.fn_forbid_delete();
CREATE TRIGGER trg_balance_no_delete     BEFORE DELETE ON materials.stock_balance FOR EACH ROW EXECUTE FUNCTION materials.fn_forbid_delete();
```

**Nota:** os triggers protegem integridade técnica como última linha de defesa; as regras completas (IV-BR) vivem no domínio (.NET — ADR-009). A auditoria de negócio (quem/o quê/quando + saldo anterior/posterior) é do FD-001-06, registrada pela aplicação na mesma transação (IV-BR-095).

## 15.5 Views

```sql
-- VW-IV-001: posição de estoque (posição por chave, com disponível — UC-IV-011)
CREATE VIEW materials.vw_stock_position AS
SELECT
    b.id,
    b.company_id,
    b.item_id,
    b.size_code,
    b.location_id,
    l.code        AS location_code,
    l.description AS location_description,
    b.client_id,
    b.contract_id,
    b.total_qty,
    b.reserved_qty,
    b.available_qty,
    b.avg_cost_reference,
    (b.total_qty * b.avg_cost_reference) AS stopped_capital_reference,
    b.last_movement_at
FROM materials.stock_balance b
JOIN materials.location l ON l.id = b.location_id;

-- VW-IV-002: fila operacional do almoxarifado (reservas ativas por vencimento — IV-BR-097; US-IV-014)
CREATE VIEW materials.vw_warehouse_queue AS
SELECT
    r.id,
    r.company_id,
    r.number,
    r.requisition_reference,
    r.item_id,
    r.size_code,
    r.quantity,
    r.fulfilled_quantity,
    (r.quantity - r.fulfilled_quantity) AS remaining_quantity,
    r.location_id,
    r.client_id,
    r.contract_id,
    r.expires_at,
    (r.expires_at <= now() + interval '24 hours') AS expiring_soon,
    r.created_at
FROM materials.reservation r
WHERE r.status = 1;

-- VW-IV-003: alertas abertos com posição atual (UC-IV-010)
CREATE VIEW materials.vw_open_alerts AS
SELECT
    a.id,
    a.company_id,
    a.alert_type,
    a.item_id,
    a.size_code,
    a.location_id,
    a.opened_at,
    a.acknowledged_by,
    a.acknowledged_at,
    b.total_qty,
    b.reserved_qty,
    b.available_qty
FROM materials.stock_alert a
LEFT JOIN materials.stock_balance b
       ON b.company_id = a.company_id
      AND b.item_id = a.item_id
      AND COALESCE(b.size_code, '') = COALESCE(a.size_code, '')
      AND b.location_id = a.location_id
      AND b.client_id IS NULL AND b.contract_id IS NULL
WHERE a.status = 1;
```

## 15.6 Materialized Views

```sql
-- MV-IV-001: sumário gerencial de estoque por empresa/local (capital, chaves, alertas; KPIs oficiais derivados da Timeline/Audit)
CREATE MATERIALIZED VIEW materials.mv_stock_summary AS
SELECT
    b.company_id,
    b.location_id,
    count(*)                                            AS balance_keys,
    sum(b.total_qty)                                    AS total_units,
    sum(b.reserved_qty)                                 AS reserved_units,
    sum(b.total_qty * COALESCE(b.avg_cost_reference,0)) AS stopped_capital_reference,
    count(*) FILTER (WHERE b.available_qty <= 0)        AS keys_without_availability
FROM materials.stock_balance b
GROUP BY b.company_id, b.location_id;

CREATE UNIQUE INDEX uq_mv_stock_summary
    ON materials.mv_stock_summary (company_id, location_id);
```

**Estratégia de refresh:** `REFRESH MATERIALIZED VIEW CONCURRENTLY materials.mv_stock_summary` a cada 15 minutos (job agendado) ou sob demanda. Indicadores oficiais do processo (MMS-004-06 §10) e a acuracidade por ciclo são derivados dos registros imutáveis (Timeline/Audit e `inventory_count`), não desta view.

## 15.7 Stored Procedures / Functions de negócio

A regra do projeto é que **a lógica de negócio vive no domínio (.NET)**, não no banco (ADR-009). O Inventory Management admite em banco apenas:

| Função | Propósito | Justificativa |
| ------ | --------- | ------------- |
| Triggers §15.4 | Integridade técnica (updated_at, version, imutabilidade, saldo com documento, delete proibido) | Última linha de defesa das invariantes INV-IV-01/03/04 |
| `materials.fn_next_document_number(company_id, kind)` | Número sequencial por empresa e tipo (`MOV`, `RSV`, `ADJ`, `INV`) via `document_sequence` com lock de linha | Sequencial sem lacunas exige serialização no banco (mesmo padrão do PR-001-11); formato final (`ENT-2026-000123`) montado no domínio |

```sql
CREATE OR REPLACE FUNCTION materials.fn_next_document_number(p_company_id UUID, p_kind VARCHAR(10))
RETURNS BIGINT AS $$
DECLARE v_next BIGINT;
BEGIN
    INSERT INTO materials.document_sequence AS ds (company_id, document_kind, last_value)
    VALUES (p_company_id, p_kind, 1)
    ON CONFLICT (company_id, document_kind)
    DO UPDATE SET last_value = ds.last_value + 1
    RETURNING last_value INTO v_next;
    RETURN v_next;
END;
$$ LANGUAGE plpgsql;
```

Qualquer nova procedure exige justificativa arquitetural registrada em ADR.

## 15.8 Estratégia de Particionamento

**Decisão para o MVP:** sem particionamento físico. A projeção `stock_balance` é pequena (uma linha por chave); o volume relevante está em `stock_movement`/`stock_movement_line`, que crescem de forma append-only.

**Gatilhos para ativar particionamento (revisão semestral):**

| Tabela | Gatilho | Estratégia-alvo |
| ------ | ------- | --------------- |
| stock_movement / stock_movement_line | > 50 milhões de linhas ou > 100 GB | `PARTITION BY RANGE (confirmed_at)` mensal; partições antigas somente leitura |
| count_entry | > 20 milhões de linhas | `PARTITION BY RANGE (counted_at)` anual |
| reservation / adjustment | Improvável (estados terminais, volume moderado) | Acompanham a estratégia de movement se necessário |
| stock_balance / stock_alert / location | Nunca (conjuntos pequenos e quentes) | — |

**Regras:** particionamento nunca por chave de negócio editável; partição destacável para arquivamento (`DETACH PARTITION`) antes de qualquer purge legal (retenção mínima de 5 anos para documentos — política corporativa); documentos confirmados jamais são apagados, apenas arquivados.

## 15.9 Keyset Pagination (padrão de consulta)

Toda listagem da API usa keyset pagination (Seção 12), com cursor opaco e assinado.

**Chave de paginação oficial do extrato:** `(confirmed_at DESC, id DESC)` — alinhada ao índice `idx_movement_confirmed_at`.

```sql
-- Extrato de movimentações do item (UC-IV-011) — primeira página
SELECT m.id, m.number, m.movement_type, m.origin_type, m.origin_reference,
       m.confirmed_at, l.quantity, l.size_code, l.from_location_id, l.to_location_id,
       l.balance_before_total, l.balance_after_total
FROM materials.stock_movement m
JOIN materials.stock_movement_line l ON l.movement_id = m.id
WHERE m.company_id = :company_id
  AND l.item_id = :item_id
  AND m.status IN (2, 3)
  AND (:location_id IS NULL OR l.from_location_id = :location_id OR l.to_location_id = :location_id)
  AND (:from_date IS NULL OR m.confirmed_at >= :from_date)
ORDER BY m.confirmed_at DESC, m.id DESC
LIMIT :page_size;

-- Página seguinte (cursor = confirmed_at + id do último documento)
--   ... AND (m.confirmed_at, m.id) < (:cursor_confirmed_at, :cursor_id) ...

-- Posição de estoque com filtros (GET /balances)
SELECT b.item_id, b.size_code, b.location_id, b.total_qty, b.reserved_qty, b.available_qty
FROM materials.stock_balance b
WHERE b.company_id = :company_id
  AND (:item_id IS NULL OR b.item_id = :item_id)
  AND (:location_id IS NULL OR b.location_id = :location_id)
ORDER BY b.item_id, b.location_id, b.id
LIMIT :page_size;

-- Validação de disponibilidade para MMS-003 (IV-BR-020; MMS-RG-09) — na transação de reserva
SELECT b.id, b.available_qty
FROM materials.stock_balance b
WHERE b.company_id = :company_id
  AND b.item_id = :item_id
  AND COALESCE(b.size_code, '') = COALESCE(:size_code, '')
  AND b.location_id = :location_id
  AND COALESCE(b.client_id,  '00000000-0000-0000-0000-000000000000') = COALESCE(:client_id,  '00000000-0000-0000-0000-000000000000')
  AND COALESCE(b.contract_id,'00000000-0000-0000-0000-000000000000') = COALESCE(:contract_id,'00000000-0000-0000-0000-000000000000')
FOR UPDATE; -- serialização por chave de saldo (IV-BR-090; INV-IV-15)
```

**Regras:**

- `page_size` entre 1 e 100 (padrão 50 — `materials.inventory.search.page-size`).
- Cursor opaco, assinado (HMAC) e com expiração de 15 minutos; inválido/expirado → `IV-ERR-400`.
- Proibido `OFFSET` em qualquer endpoint.
- O `SELECT ... FOR UPDATE` sobre `stock_balance` é o mecanismo oficial de serialização de confirmações concorrentes na mesma chave (a segunda transação aguarda ou falha por timeout com orientação de retry).

## 15.10 Plano de Indexação

| Consulta crítica | Índice de suporte | Cobertura |
| ---------------- | ----------------- | --------- |
| Validação de disponibilidade (MMS-003; p95 < 2s) | `uq_stock_balance_key` | Lookup direto por chave + lock de linha |
| Posição por item / por local | `idx_stock_balance_item` / `idx_stock_balance_location` | Filtros primários da posição |
| Extrato por item (UC-IV-011) | `idx_movement_line_item` + `idx_movement_confirmed_at` | Keyset sem sort adicional |
| Fila de reservas ativas (visão do almoxarifado) | `idx_reservation_status` (+ view VW-IV-002) | Subconjunto quente |
| Job de vencimento (TMR-IV-001) e janela de alerta (TMR-IV-002) | `idx_reservation_expiring` (parcial `status = 1`) | Range scan por `expires_at` |
| Fila de aprovação de ajustes | `idx_adjustment_status` | Pendentes por empresa ordenados |
| Idempotência de origem (FA-IV-005) | `uq_movement_origin` | Lookup direto |
| Alertas abertos (deduplicação IV-BR-091) | `uq_stock_alert_open` (parcial) | Lookup + unicidade |
| Ruptura/mínimo (avaliação pós-efeito) | `idx_stock_balance_below_zero_available` (parcial) | Varredura mínima |
| Árvore de locais | `idx_location_parent`, `uq_location_code_company` | Navegação e unicidade |

**Governança de índices:**

- Todo novo índice exige: consulta-alvo documentada, validação com `EXPLAIN ANALYZE` em volume representativo e registro nesta tabela.
- Índices não utilizados são revistos trimestralmente (`pg_stat_user_indexes.idx_scan = 0` por 90 dias → candidato a remoção).
- `ANALYZE` automático (autovacuum) habilitado; `default_statistics_target = 100` nas colunas de filtro frequente.

## 15.11 Plano de Performance

| Tema | Diretriz |
| ---- | -------- |
| **Consultas** | Sempre paginadas (§15.9), filtradas por `company_id` primeiro, projeção apenas das colunas necessárias (sem `SELECT *`) |
| **Efeito de saldo** | Confirmação = transação curta: lock por chave (`FOR UPDATE`), efeito por linha, snapshot de saldos, outbox — tudo na mesma transação; nunca I/O externo dentro dela |
| **N+1** | Detalhe do documento carrega linhas em uma única query por `movement_id`; posição consolidada via `stock_balance`, nunca agregando linhas em tempo de requisição |
| **Locks** | Concorrência otimista via `version` (TRG-IV-002) nos documentos; lock pessimista **apenas** na linha de `stock_balance` durante a confirmação (IV-BR-090) — ordenado por chave para evitar deadlock em documentos multi-linha |
| **Isolation level** | `READ COMMITTED` padrão; a serialização por chave de saldo dispensa `SERIALIZABLE` |
| **Pool** | PgBouncer (transaction pooling); máx. de conexões por serviço configurado (`db.pool.max`, padrão 20) |
| **Timeouts** | `statement_timeout = 30s` em OLTP (TIME-IV-001); `lock_timeout = 5s` (falha orienta retry — IV-ERR-409); `idle_in_transaction_session_timeout = 60s` |
| **Cache** | Posição de estoque em Redis (`materials.inventory.cache.ttl`), invalidada por evento (POL-IV-09); cache nunca é fonte de verdade (IV-BR-096; COMP-IV-004) |
| **Metas** | Validação de disponibilidade p95 < 500 ms (NFR < 2s ponta a ponta); posição paginada p95 < 200 ms; confirmação de documento p95 < 1s; extrato p95 < 300 ms |
| **Monitoramento** | `pg_stat_statements` habilitado; alerta para queries p95 acima da meta, seq scans em `stock_movement_line` e lock waits > 1s na projeção |
| **Manutenção** | Autovacuum agressivo nas tabelas quentes (`stock_balance`, `reservation`: `autovacuum_vacuum_scale_factor = 0.02`); `REINDEX CONCURRENTLY` em janela quando bloat > 30% |

## 15.12 Estratégia de Migração

| Aspecto | Diretriz |
| ------- | -------- |
| **Ferramenta** | Migrations versionadas via EF Core Migrations (assembly do módulo Materials), aplicadas por pipeline CI/CD — nunca manual em produção |
| **Formato** | Uma migration por alteração lógica; nome `YYYYMMDDHHMM_<descricao>`; SQL gerado revisado em PR |
| **Ordem** | Expand-and-contract: (1) adicionar nova estrutura compatível; (2) migrar dados; (3) remover estrutura antiga em release posterior |
| **Backward compatibility** | Toda migration compatível com a versão N-1 da aplicação (deploy blue/green sem downtime) |
| **Operações online** | Índices com `CREATE INDEX CONCURRENTLY`; alterações de tipo via nova coluna + backfill + swap |
| **Backfill** | Em lotes (`batch_size = 10.000`), com `statement_timeout` elevado apenas no job, monitorado e pausável |
| **Rollback** | Toda migration possui `Down()` testado; rollback que afete documentos confirmados é proibido (imutabilidade) |
| **Carga inicial** | Posição inicial **somente** por documentos de entrada de carga inicial (origem 4), gerados por job de importação auditado — nunca `INSERT` direto em `stock_balance` (MMS-004-01 §11.6) |
| **Mudança de granularidade** | Alteração de `addressing.level` exige migração assistida dos saldos por documentos de transferência (UC-IV-009 A2) — nunca update direto |
| **Ambientes** | dev → staging (réplica de produção anonimizada) → produção; nunca pular staging |

## 15.13 Política de Backup

| Aspecto | Diretriz |
| ------- | -------- |
| **Backup completo** | Diário, `pg_dump` em formato custom ou snapshot de volume (conforme plataforma), retenção 30 dias |
| **WAL archiving (PITR)** | Arquivamento contínuo de WAL; Point-in-Time Recovery com granularidade de 5 minutos, retenção 7 dias |
| **Backup semanal** | Retenção de 12 semanas |
| **Backup mensal** | Retenção de 12 meses; documentos de movimentação seguem retenção legal de 5 anos (arquivamento por partição quando ativado §15.8) |
| **Criptografia** | Backups criptografados em repouso (AES-256) e em trânsito (TLS); chaves gerenciadas fora do banco (KMS/cofre) |
| **Teste de restore** | Restore completo em ambiente isolado **mensal**, com evidência registrada; RPO ≤ 5 min, RTO ≤ 4 h |
| **Reconstrução da projeção** | Runbook de reconstrução de `stock_balance` a partir dos documentos confirmados (COMP-IV-004) testado junto com o restore |
| **Localidade** | Réplica em região/zona distinta da primária |

## 15.14 Versionamento de Schema

| Aspecto | Diretriz |
| ------- | -------- |
| **Versão do schema** | Derivada da última migration aplicada (`__EFMigrationsHistory`); exposta no health check do serviço (`/health` inclui `schemaVersion`) |
| **Compatibilidade aplicação × schema** | O serviço valida na inicialização que o schema mínimo exigido está aplicado; incompatibilidade impede o start (fail-fast) |
| **Rastreabilidade** | Toda migration referencia o documento de origem (este documento ou ADR); alterações de modelo exigem atualização deste documento **antes** da migration |
| **Multiempresa** | Um único schema lógico por módulo; isolamento por `company_id` (Seção 9), nunca schema por tenant |
| **Privilégios** | O papel `trino_inventory_app` é o único com escrita em `stock_balance`; verificação de privilégios no pipeline compõe a prova de integridade do DoD do módulo |
| **Evolução registrada** | Mudanças de modelo físico seguem o fluxo de governança (GOV-002): proposta → revisão arquitetural → ADR (se estrutural) → atualização deste documento → migration |

---

## Histórico de Versão

| Versão | Data | Autor | Alteração |
| ------ | ---- | ----- | --------- |
| 1.0.0 | 2026-08-23 | Arquiteto Principal | Versão inicial aprovada: modelo físico do Inventory Management no schema `materials` (ADR-009): 11 tabelas (stock_movement, stock_movement_line, reservation, adjustment, adjustment_line, inventory_count, inventory_count_location, count_entry, location, stock_balance como projeção não editável, stock_alert) + document_sequence; DDL PostgreSQL completo com constraints de integridade de saldo (total/reservado ≥ 0, available gerado, SoD, imutabilidade), numeração sequencial por empresa, idempotência de origem, reserva ativa única por chave, deduplicação de alerta aberto; FKs físicas internas + lógicas para MMS-002/MMS-003/MMS-005/Foundation; 5 famílias de triggers (updated_at, version, imutabilidade de documento e linha, saldo somente com documento, delete proibido); 3 views (posição, fila do almoxarifado, alertas abertos), 1 materialized view; função de numeração sequencial; particionamento por gatilho, keyset pagination com lock por chave de saldo, planos de indexação/performance/migração (carga inicial só por documento), backup com runbook de reconstrução da projeção e versionamento de schema com verificação de privilégios (prova de integridade do DoD). |
