# MMS-004-11 — Database Model

**Documento:** MMS-004-11 — Database Model
**Módulo:** MMS-004 — Inventory Management (Estoque)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-004 v1.1.0, MMS-004-02 (Business Rules v1.1.0), MMS-004-03 (State Machine v1.1.0), MMS-004-04 (Domain Model v1.1.0), MMS-004-05 (Event Storming), ADR-009, ADR-010, ADR-013 (Conversão de UoM), ADR-014 (Motor de Regras de Reposição), MMS-002-11 (Item Catalog — schema `materials`), FD-001-06
**Referências:** MMS-002-11 (padrão de formato Enterprise), GOV-001

> Decisão arquitetural associada: **ADR-009 — Banco de dados como projeção do domínio**. Este documento é a implementação física do modelo de domínio **MMS-004-04**.
> Princípio central (MMS-P-08 / INV-IV-01): **saldo não é um agregado editável** — `stock_balance` é uma **projeção** escrita exclusivamente pelo `StockBalanceService` na confirmação de documentos. Não há caminho de escrita direta de saldo (nem por usuário, API ou job).

---

# 1. Objetivo

Definir a estrutura de persistência do módulo Inventory Management: documentos de movimentação, reservas, ajustes, inventários, locais, **projeção de saldo** e **sugestões de reposição** (ADR-014). Toda quantidade é persistida na **unidade base** do item (ADR-013); a captura em unidade alternativa guarda o fator aplicado.

# 2. Princípios

- PostgreSQL como banco oficial; schema `materials` (compartilhado com o Item Catalog — um schema por Bounded Context, ARC-002 §5).
- UUID como chave primária; Timezone UTC (`TIMESTAMPTZ`).
- Multiempresa por `company_id` em toda tabela e índice (`company_id`-first).
- Soft delete onde aplicável; **documentos confirmados são imutáveis** (correção por estorno — IV-BR-003).
- Versionamento otimista (`version`) e confirmação **serializada por chave de saldo** (IV-BR-012).
- Saldo é projeção derivada (ADR-009 / INV-IV-01): **sem escrita direta**.
- Aggregate enxuto: auditoria/timeline/notificação via Foundation (FD-001-05/06/07), nunca tabelas próprias.

# 3. Convenções

```text
Chave primária:      id UUID PRIMARY KEY
Datas:               created_at · updated_at · deleted_at
Auditoria:           created_by · updated_by · deleted_by
Concorrência:        version INTEGER  (Optimistic Concurrency)
Status:              SMALLINT com CHECK conforme códigos ST-IV da State Machine (MMS-004-03)
```

---

# 4. Entidades (visão conceitual)

| Tabela | Papel | Origem no domínio (MMS-004-04) |
|--------|-------|--------------------------------|
| `location` | Estrutura almoxarifado → depósito → endereço | Location (AR) |
| `stock_movement` | Documento de movimentação (cabeçalho) | StockMovement (AR) |
| `stock_movement_line` | Linha de efeito do documento | MovementLine |
| `reservation` | Bloqueio de disponível por solicitação | Reservation (AR) |
| `adjustment` | Ajuste com justificativa/aprovação | Adjustment (AR) |
| `adjustment_line` | Linha do ajuste | AdjustmentLine |
| `inventory_count` | Ciclo de contagem física | InventoryCount (AR) |
| `inventory_count_entry` | Contagem por item × local | CountEntry |
| `replenishment_suggestion` | Sugestão de reposição (ADR-014) | ReplenishmentSuggestion (AR) |
| `stock_balance` | **Projeção de saldo** (não editável) | StockBalance (read model) |

---

# 5. Relacionamentos

```text
location            1 ── N  location            (hierarquia por parent_id)
stock_movement      1 ── N  stock_movement_line
stock_movement      1 ── 0..1 stock_movement    (estorno: reverses_id / reversed_by_id)
stock_movement      N ── 0..1 reservation       (saída por atendimento baixa reserva)
adjustment          1 ── N  adjustment_line
adjustment          1 ── 0..1 stock_movement    (documento de efeito, após aprovação)
adjustment          N ── 0..1 inventory_count   (origem, quando de inventário)
inventory_count     1 ── N  inventory_count_entry
inventory_count     1 ── N  adjustment          (divergências tratadas)
replenishment_suggestion N ── 0..1 stock_movement / PR-001  (outcome, quando confirmada)

Referências lógicas (sem FK física — ADR-009):
*.item_id / *.uom_id / *.size_code → MMS-002 / FD-001-09 Master Data
*.company_id → FD-001-01/02 Foundation
reservation.requisition_ref → MMS-003
stock_movement.origin_reference → MMS-005 / MMS-003 / PR-001 (conforme origin_type)
```

---

# 6. DDL PostgreSQL (implementação física)

> Schema: `materials` (mesmo do Item Catalog). Datas em UTC (`TIMESTAMPTZ`). FKs para Foundation/MMS-002/MMS-003 são **lógicas** (validação no domínio; baixo acoplamento entre bounded contexts).

## 6.1 Locais

```sql
CREATE TABLE materials.location (
    id            UUID        NOT NULL DEFAULT gen_random_uuid(),
    company_id    UUID        NOT NULL,
    location_type SMALLINT    NOT NULL,             -- 1=Warehouse, 2=Deposit, 3=Address
    code          VARCHAR(50) NOT NULL,
    description   VARCHAR(200) NOT NULL,
    parent_id     UUID        NULL,                 -- Warehouse: NULL; Deposit: Warehouse; Address: Deposit
    active        BOOLEAN     NOT NULL DEFAULT true,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at    TIMESTAMPTZ NULL,
    created_by    UUID        NOT NULL,
    updated_by    UUID        NOT NULL,
    deleted_by    UUID        NULL,
    version       INTEGER     NOT NULL DEFAULT 1,
    CONSTRAINT pk_location PRIMARY KEY (id),
    CONSTRAINT fk_location_parent FOREIGN KEY (parent_id) REFERENCES materials.location (id),
    CONSTRAINT ck_location_type CHECK (location_type IN (1, 2, 3)),
    CONSTRAINT ck_location_hierarchy CHECK (
        (location_type = 1 AND parent_id IS NULL) OR
        (location_type IN (2, 3) AND parent_id IS NOT NULL)
    ), -- IV-BR-050
    CONSTRAINT ck_location_version CHECK (version >= 1)
);
CREATE UNIQUE INDEX uq_location_code ON materials.location (company_id, location_type, code) WHERE deleted_at IS NULL;
CREATE INDEX idx_location_parent ON materials.location (company_id, parent_id) WHERE deleted_at IS NULL;
```

## 6.2 Documento de movimentação

```sql
CREATE TABLE materials.stock_movement (
    id               UUID        NOT NULL DEFAULT gen_random_uuid(),
    company_id       UUID        NOT NULL,
    movement_type    SMALLINT    NOT NULL,          -- 1=Entrada,2=Saída,3=Transferência,4=LiberaçãoReserva,5=Estorno
    origin_type      SMALLINT    NOT NULL,          -- recebimento, devolução, atendimento, consumo, ajuste, carga inicial, operação
    origin_reference VARCHAR(100) NOT NULL,         -- referência lógica ao documento de origem (IV-BR-002)
    status           SMALLINT    NOT NULL DEFAULT 1, -- ST-IV-001..004
    reason           VARCHAR(500) NULL,
    reverses_id      UUID        NULL,              -- documento estornado por este (IV-BR-003)
    reversed_by_id   UUID        NULL,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    confirmed_at     TIMESTAMPTZ NULL,
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at       TIMESTAMPTZ NULL,
    created_by       UUID        NOT NULL,
    confirmed_by     UUID        NULL,
    updated_by       UUID        NOT NULL,
    deleted_by       UUID        NULL,
    version          INTEGER     NOT NULL DEFAULT 1,
    CONSTRAINT pk_stock_movement PRIMARY KEY (id),
    CONSTRAINT fk_movement_reverses FOREIGN KEY (reverses_id) REFERENCES materials.stock_movement (id),
    CONSTRAINT ck_movement_type CHECK (movement_type IN (1, 2, 3, 4, 5)),
    CONSTRAINT ck_movement_status CHECK (status IN (1, 2, 3, 4)),
    CONSTRAINT ck_movement_confirmed CHECK (
        (status = 1 AND confirmed_at IS NULL) OR (status <> 1)  -- confirmado/estornado/cancelado coerente
    ),
    CONSTRAINT ck_movement_version CHECK (version >= 1)
);
CREATE INDEX idx_movement_company_created ON materials.stock_movement (company_id, created_at DESC, id DESC) WHERE deleted_at IS NULL;
CREATE INDEX idx_movement_origin ON materials.stock_movement (company_id, origin_type, origin_reference) WHERE deleted_at IS NULL;
CREATE INDEX idx_movement_status ON materials.stock_movement (company_id, status) WHERE deleted_at IS NULL;

CREATE TABLE materials.stock_movement_line (
    id                UUID          NOT NULL DEFAULT gen_random_uuid(),
    movement_id       UUID          NOT NULL,
    item_id           UUID          NOT NULL,        -- MMS-002 (lógica)
    size_code         VARCHAR(30)   NULL,            -- obrigatório quando item tem grade (IV-BR-120)
    quantity          NUMERIC(18,4) NOT NULL,        -- SEMPRE na unidade base (ADR-013, IV-BR-009)
    captured_quantity NUMERIC(18,4) NULL,            -- quantidade informada em unidade alternativa
    captured_uom_id   UUID          NULL,            -- unidade alternativa informada (MMS-002/FD-001-09)
    applied_factor    NUMERIC(18,6) NULL,            -- fator de conversão aplicado no momento (IC-BR-093)
    from_location_id  UUID          NULL,            -- saída/transferência exige origem
    to_location_id    UUID          NULL,            -- entrada/transferência exige destino
    segregation_key   VARCHAR(120)  NULL,            -- cliente/contrato (IV-BR-060)
    CONSTRAINT pk_movement_line PRIMARY KEY (id),
    CONSTRAINT fk_movement_line_movement FOREIGN KEY (movement_id) REFERENCES materials.stock_movement (id),
    CONSTRAINT fk_movement_line_from FOREIGN KEY (from_location_id) REFERENCES materials.location (id),
    CONSTRAINT fk_movement_line_to   FOREIGN KEY (to_location_id)   REFERENCES materials.location (id),
    CONSTRAINT ck_movement_line_qty CHECK (quantity > 0),   -- IV-BR-009
    CONSTRAINT ck_movement_line_capture CHECK (             -- captura em unidade alternativa é tudo-ou-nada
        (captured_quantity IS NULL AND captured_uom_id IS NULL AND applied_factor IS NULL) OR
        (captured_quantity IS NOT NULL AND captured_uom_id IS NOT NULL AND applied_factor IS NOT NULL AND applied_factor > 0)
    )
);
CREATE INDEX idx_movement_line_movement ON materials.stock_movement_line (movement_id);
CREATE INDEX idx_movement_line_item ON materials.stock_movement_line (item_id);
```

## 6.3 Reserva

```sql
CREATE TABLE materials.reservation (
    id                 UUID          NOT NULL DEFAULT gen_random_uuid(),
    company_id         UUID          NOT NULL,
    requisition_ref    VARCHAR(100)  NOT NULL,       -- MMS-003 (IV-BR-020)
    item_id            UUID          NOT NULL,
    size_code          VARCHAR(30)   NULL,
    quantity           NUMERIC(18,4) NOT NULL,       -- unidade base
    fulfilled_quantity NUMERIC(18,4) NOT NULL DEFAULT 0,
    location_id        UUID          NOT NULL,
    segregation_key    VARCHAR(120)  NULL,
    status             SMALLINT      NOT NULL DEFAULT 10, -- ST-IV-010..013
    expires_at         TIMESTAMPTZ   NOT NULL,        -- created + TTL (IV-BR-021)
    released_reason    VARCHAR(500)  NULL,
    created_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    updated_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    created_by         UUID          NOT NULL,
    updated_by         UUID          NOT NULL,
    version            INTEGER       NOT NULL DEFAULT 1,
    CONSTRAINT pk_reservation PRIMARY KEY (id),
    CONSTRAINT fk_reservation_location FOREIGN KEY (location_id) REFERENCES materials.location (id),
    CONSTRAINT ck_reservation_status CHECK (status IN (10, 11, 12, 13)),
    CONSTRAINT ck_reservation_qty CHECK (quantity > 0 AND fulfilled_quantity >= 0 AND fulfilled_quantity <= quantity), -- INV-IV-07
    CONSTRAINT ck_reservation_version CHECK (version >= 1)
);
-- No máximo uma reserva ATIVA por (requisição, item, tamanho, local) — INV-IV-06
CREATE UNIQUE INDEX uq_reservation_active_key
    ON materials.reservation (company_id, requisition_ref, item_id, COALESCE(size_code, ''), location_id)
    WHERE status = 10;
CREATE INDEX idx_reservation_expiring ON materials.reservation (company_id, expires_at) WHERE status = 10;
```

## 6.4 Ajuste

```sql
CREATE TABLE materials.adjustment (
    id                   UUID         NOT NULL DEFAULT gen_random_uuid(),
    company_id           UUID         NOT NULL,
    adjustment_reason    SMALLINT     NOT NULL,       -- divergência, perda, avaria, achado, erro de lançamento
    justification        VARCHAR(500) NOT NULL,       -- IV-BR-040
    status               SMALLINT     NOT NULL DEFAULT 20, -- ST-IV-020..023
    inventory_id         UUID         NULL,           -- vínculo com inventário (IV-BR-072)
    registered_by        UUID         NOT NULL,
    approver_id          UUID         NULL,           -- ≠ registered_by (IV-BR-041 / INV-IV-10)
    rejection_reason     VARCHAR(500) NULL,
    movement_id          UUID         NULL,           -- documento de efeito, após aprovação
    created_at           TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at           TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_by           UUID         NOT NULL,
    version              INTEGER      NOT NULL DEFAULT 1,
    CONSTRAINT pk_adjustment PRIMARY KEY (id),
    CONSTRAINT fk_adjustment_movement FOREIGN KEY (movement_id) REFERENCES materials.stock_movement (id),
    CONSTRAINT fk_adjustment_inventory FOREIGN KEY (inventory_id) REFERENCES materials.inventory_count (id),
    CONSTRAINT ck_adjustment_status CHECK (status IN (20, 21, 22, 23)),
    CONSTRAINT ck_adjustment_approver CHECK (approver_id IS NULL OR approver_id <> registered_by), -- INV-IV-10
    CONSTRAINT ck_adjustment_version CHECK (version >= 1)
);

CREATE TABLE materials.adjustment_line (
    id              UUID          NOT NULL DEFAULT gen_random_uuid(),
    adjustment_id   UUID          NOT NULL,
    item_id         UUID          NOT NULL,
    size_code       VARCHAR(30)   NULL,
    quantity_delta  NUMERIC(18,4) NOT NULL,          -- com sinal (respeita IV-BR-004 na aprovação)
    location_id     UUID          NOT NULL,
    segregation_key VARCHAR(120)  NULL,
    CONSTRAINT pk_adjustment_line PRIMARY KEY (id),
    CONSTRAINT fk_adjustment_line_adj FOREIGN KEY (adjustment_id) REFERENCES materials.adjustment (id),
    CONSTRAINT fk_adjustment_line_loc FOREIGN KEY (location_id) REFERENCES materials.location (id),
    CONSTRAINT ck_adjustment_line_delta CHECK (quantity_delta <> 0)
);
CREATE INDEX idx_adjustment_pending ON materials.adjustment (company_id, status) WHERE status = 20;
```

## 6.5 Inventário

```sql
CREATE TABLE materials.inventory_count (
    id             UUID         NOT NULL DEFAULT gen_random_uuid(),
    company_id     UUID         NOT NULL,
    scope_type     SMALLINT     NOT NULL,             -- 1=ABC, 2=geral
    scope_detail   JSONB        NOT NULL,             -- classes ABC e/ou locais do escopo (IV-BR-070)
    status         SMALLINT     NOT NULL DEFAULT 30,  -- ST-IV-030..033
    responsible_id UUID         NOT NULL,
    deadline       TIMESTAMPTZ  NULL,
    started_at     TIMESTAMPTZ  NULL,
    closed_at      TIMESTAMPTZ  NULL,
    summary        JSONB        NULL,                 -- contados, divergentes, ajustados, dentro da tolerância
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by     UUID         NOT NULL,
    updated_by     UUID         NOT NULL,
    version        INTEGER      NOT NULL DEFAULT 1,
    CONSTRAINT pk_inventory_count PRIMARY KEY (id),
    CONSTRAINT ck_inventory_status CHECK (status IN (30, 31, 32, 33)),
    CONSTRAINT ck_inventory_version CHECK (version >= 1)
);

CREATE TABLE materials.inventory_count_entry (
    id                  UUID          NOT NULL DEFAULT gen_random_uuid(),
    inventory_id        UUID          NOT NULL,
    item_id             UUID          NOT NULL,
    size_code           VARCHAR(30)   NULL,
    location_id         UUID          NOT NULL,
    counted_qty         NUMERIC(18,4) NOT NULL,       -- ≥ 0 (IV-BR-071)
    system_qty_snapshot NUMERIC(18,4) NOT NULL,       -- saldo no instante da contagem (IV-BR-073)
    counted_by          UUID          NOT NULL,
    counted_at          TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT pk_count_entry PRIMARY KEY (id),
    CONSTRAINT fk_count_entry_inventory FOREIGN KEY (inventory_id) REFERENCES materials.inventory_count (id),
    CONSTRAINT fk_count_entry_loc FOREIGN KEY (location_id) REFERENCES materials.location (id),
    CONSTRAINT ck_count_entry_qty CHECK (counted_qty >= 0)
);
CREATE INDEX idx_count_entry_inventory ON materials.inventory_count_entry (inventory_id);
```

## 6.6 Sugestão de reposição (ADR-014)

```sql
CREATE TABLE materials.replenishment_suggestion (
    id                     UUID          NOT NULL DEFAULT gen_random_uuid(),
    company_id             UUID          NOT NULL,
    item_id                UUID          NOT NULL,
    location_id            UUID          NOT NULL,       -- depósito
    triggered_available    NUMERIC(18,4) NOT NULL,       -- disponível no gatilho
    reorder_point          NUMERIC(18,4) NOT NULL,       -- snapshot do ponto de pedido
    policy                 SMALLINT      NOT NULL,       -- 1=ate_maximo,2=multiplo_embalagem,3=lote_fixo (IC-BR-100)
    suggested_quantity     NUMERIC(18,4) NOT NULL,       -- na unidade base, já resolvida pela política
    route                  SMALLINT      NOT NULL,       -- 1=transferencia,2=compra,3=manual (IC-BR-101)
    status                 SMALLINT      NOT NULL DEFAULT 40, -- ST-IV-040..042
    outcome_reference      VARCHAR(100)  NULL,           -- PR-001 gerado ou documento de transferência
    created_at             TIMESTAMPTZ   NOT NULL DEFAULT now(),
    decided_by             UUID          NULL,
    decided_at             TIMESTAMPTZ   NULL,
    updated_at             TIMESTAMPTZ   NOT NULL DEFAULT now(),
    version                INTEGER       NOT NULL DEFAULT 1,
    CONSTRAINT pk_replenishment_suggestion PRIMARY KEY (id),
    CONSTRAINT fk_replenishment_location FOREIGN KEY (location_id) REFERENCES materials.location (id),
    CONSTRAINT ck_replenishment_policy CHECK (policy IN (1, 2, 3)),
    CONSTRAINT ck_replenishment_route  CHECK (route IN (1, 2, 3)),
    CONSTRAINT ck_replenishment_status CHECK (status IN (40, 41, 42)),
    CONSTRAINT ck_replenishment_qty CHECK (suggested_quantity > 0),
    CONSTRAINT ck_replenishment_version CHECK (version >= 1)
);
-- No máximo uma sugestão ABERTA (Sugerida) por item × depósito — evita duplicidade (IV-BR-130)
CREATE UNIQUE INDEX uq_replenishment_open ON materials.replenishment_suggestion (company_id, item_id, location_id) WHERE status = 40;
```

## 6.7 Projeção de saldo (não editável)

```sql
-- stock_balance: PROJEÇÃO derivada (ADR-009 / INV-IV-01). Escrita EXCLUSIVA do StockBalanceService
-- na confirmação de documentos. NÃO há endpoint, comando de usuário ou job que a atualize de outra forma.
CREATE TABLE materials.stock_balance (
    company_id        UUID          NOT NULL,
    item_id           UUID          NOT NULL,
    size_code         VARCHAR(30)   NOT NULL DEFAULT '',   -- '' quando o item não tem grade
    location_id       UUID          NOT NULL,
    segregation_key   VARCHAR(120)  NOT NULL DEFAULT '',   -- '' quando saldo comum
    total_qty         NUMERIC(18,4) NOT NULL DEFAULT 0,
    reserved_qty      NUMERIC(18,4) NOT NULL DEFAULT 0,
    available_qty     NUMERIC(18,4) GENERATED ALWAYS AS (total_qty - reserved_qty) STORED, -- INV-IV-05
    avg_cost_ref      NUMERIC(18,4) NULL,                  -- custo médio de referência (gerencial, MVP)
    last_movement_id  UUID          NULL,
    last_movement_at  TIMESTAMPTZ   NULL,
    updated_at        TIMESTAMPTZ   NOT NULL DEFAULT now(),
    version           INTEGER       NOT NULL DEFAULT 1,
    CONSTRAINT pk_stock_balance PRIMARY KEY (company_id, item_id, size_code, location_id, segregation_key),
    CONSTRAINT fk_stock_balance_location FOREIGN KEY (location_id) REFERENCES materials.location (id),
    CONSTRAINT ck_balance_nonneg CHECK (total_qty >= 0 AND reserved_qty >= 0 AND reserved_qty <= total_qty) -- INV-IV-04
);
CREATE INDEX idx_balance_item ON materials.stock_balance (company_id, item_id);
CREATE INDEX idx_balance_below_min ON materials.stock_balance (company_id, item_id, location_id); -- suporte a alerta de mínimo/ruptura
```

---

# 7. Foreign Keys

| FK | Origem → Destino | Tipo | Justificativa |
|----|------------------|------|---------------|
| fk_location_parent | location → location | Física | Integridade da hierarquia (IV-BR-050) |
| fk_movement_line_movement / _from / _to | linhas/locais → tabela do módulo | Física | Integridade do aggregate / locais |
| fk_reservation_location, fk_adjustment_*, fk_count_entry_*, fk_replenishment_location, fk_stock_balance_location | → location / movement / inventory | Física | Integridade intra-módulo |
| company_id | → Foundation (FD-001-01/02) | **Lógica** | Baixo acoplamento entre bounded contexts (ADR-009) |
| item_id, uom_id, size_code | → MMS-002 / FD-001-09 | **Lógica** | Validação no domínio (IV-BR-007/009/120) |
| requisition_ref, origin_reference, outcome_reference | → MMS-003 / MMS-005 / PR-001 | **Lógica** | Referência entre contextos por evento/contrato (ARC-002) |

> **Sem FK física de saldo:** `stock_balance` não referencia documentos por FK — é projeção reconstruível a partir do razão de movimentos.

---

# 8. Triggers (integridade técnica)

```sql
-- TRG-IV-001: updated_at automático (todas as tabelas com updated_at)
-- TRG-IV-002: version incrementa de 1 em 1; retrocesso levanta IV-ERR-012 (concorrência)
-- TRG-IV-003: documento CONFIRMADO/ESTORNADO/CANCELADO é imutável (bloqueia UPDATE de campos de negócio) — IV-BR-003
-- TRG-IV-004: stock_balance protegida — REVOGAR INSERT/UPDATE/DELETE de todos os papéis exceto o
--             role do StockBalanceService (defesa em profundidade de INV-IV-01); toda escrita fora
--             desse role levanta IV-ERR-001.
```

Os triggers protegem apenas integridade técnica; a auditoria de negócio (quem/o quê/quando + saldo anterior/posterior) é do Foundation (FD-001-06, IV-BR-090) — nunca em tabela do módulo.

---

# 9. Multiempresa, Soft Delete e Versionamento

- `company_id` obrigatório e primeiro em todo índice de consulta; nenhuma query o ignora (IV-BR-006).
- Soft delete (`deleted_at`/`deleted_by`) em locais e documentos em Rascunho; **documentos confirmados nunca são excluídos** — correção por estorno.
- `version` para concorrência otimista; confirmações sobre a mesma chave de saldo são serializadas na camada de aplicação (bloqueio por chave — IV-BR-012).

---

# 10. Performance

| Tema | Diretriz |
|------|----------|
| Consultas | Sempre paginadas (keyset), `company_id`-first, projeção mínima |
| Saldo | Leitura direta em `stock_balance` (projeção); cache Redis com invalidação por evento (IV-BR-110) |
| Extrato | `stock_movement` + linhas por `(company_id, created_at DESC, id DESC)` (IV-BR-111) |
| Concorrência | Serialização por chave de saldo na confirmação; sem lock pessimista amplo |
| Metas | Validação/consulta de saldo p95 < 2 s (IV-BR-110); extrato paginado p95 < 500 ms |

**Keyset (extrato):** `(created_at, id) < (:cursor_created_at, :cursor_id)` ordenado `DESC`, `page_size` 1–100 (padrão 50). `OFFSET` proibido.

---

# 11. Migração, Backup e Versionamento de Schema

- **Migrations** EF Core versionadas, aplicadas por CI/CD (nunca manual em produção); expand-and-contract; `Down()` testado.
- **Backup/PITR** conforme política da suíte (ver MMS-002-11 §15.13): full diário, WAL contínuo (RPO ≤ 5 min), teste de restore mensal.
- **Reconstrução da projeção:** `stock_balance` é reconstruível a partir de `stock_movement` confirmados — procedimento de rebuild documentado (não substitui backup, mas garante que a projeção nunca é fonte de verdade).
- **Versão do schema** exposta no health check; incompatibilidade impede o start (fail-fast).

---

# 12. Dependências

| Dependência | Uso |
|-------------|-----|
| MMS-004-04 (Domain Model v1.1.0) | Aggregates, invariantes e projeção implementados aqui |
| MMS-002-11 | Schema `materials` compartilhado; item_id/uom lógicos |
| ADR-009 | Banco como projeção do domínio (saldo não editável) |
| ADR-013 | Colunas de captura de conversão em `stock_movement_line` |
| ADR-014 | Tabela `replenishment_suggestion` |
| FD-001-06 | Auditoria (nunca tabela própria) |

---

# 13. Histórico de Versão

| Versão | Data | Autor | Alteração |
|--------|------|-------|-----------|
| 1.0.0 | 2026-08-08 | Arquiteto Principal | Criação do Database Model do Inventory Management no schema `materials` (ADR-009): 10 tabelas (location, stock_movement + line, reservation, adjustment + line, inventory_count + entry, replenishment_suggestion, stock_balance como projeção não editável com `available_qty` gerada); DDL PostgreSQL completo com constraints refletindo invariantes INV-IV (não-negatividade, reserva única ativa, aprovador ≠ registrante, captura de conversão tudo-ou-nada, sugestão aberta única); conversão de UoM (ADR-013) em `stock_movement_line`; sugestão de reposição (ADR-014); FKs físicas intra-módulo + lógicas para Foundation/MMS-002/003 e PR-001; triggers de imutabilidade e proteção da projeção de saldo; multiempresa, soft delete, versionamento, keyset, performance, migração e backup — no padrão MMS-002-11. |
