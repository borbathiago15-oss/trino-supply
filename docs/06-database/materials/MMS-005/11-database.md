# MMS-005-11 — Database Model

**Documento:** MMS-005-11 — Database Model
**Módulo:** MMS-005 — Receiving (Recebimento)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-005-02/03/04, ADR-009, ADR-013, MMS-002-11 (schema `materials`), MMS-004-11, FD-001-03/06
**Referências:** MMS-004-11 / MMS-003-11 (padrão), GOV-001

> Implementação física do domínio MMS-005-04 (ADR-009). Schema `materials`. **Não persiste saldo** (RC-BR-020): a entrada é gerada no MMS-004.

# 1. Princípios
PostgreSQL; UUID PK; UTC; multiempresa `company_id`; documentos concluídos imutáveis; versionamento otimista; conversão de UoM registrada; sem tabela de saldo.

# 2. Entidades
| Tabela | Papel |
|--------|-------|
| `receiving` | Cabeçalho do recebimento (Receiving AR) |
| `receiving_line` | Linha de conferência (ReceivingLine) |
| `receiving_divergence` | Divergência com destino (Divergence) |

# 3. DDL PostgreSQL
```sql
CREATE TABLE materials.receiving (
    id                      UUID         NOT NULL DEFAULT gen_random_uuid(),
    company_id              UUID         NOT NULL,
    origin_type             SMALLINT     NOT NULL,        -- 1=pedido(PR-001),2=transferência(MMS-004),3=devolução(MMS-003)
    origin_reference        VARCHAR(100) NOT NULL,        -- RC-BR-001
    warehouse_id            UUID         NOT NULL,        -- MMS-004 location (depósito) — lógica
    status                  SMALLINT     NOT NULL DEFAULT 1, -- ST-RC-001..005
    stock_entry_movement_id UUID         NULL,            -- MMS-004 (entrada gerada na conclusão)
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    completed_at            TIMESTAMPTZ  NULL,
    updated_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    deleted_at              TIMESTAMPTZ  NULL,
    created_by              UUID         NOT NULL,
    completed_by            UUID         NULL,
    updated_by              UUID         NOT NULL,
    deleted_by              UUID         NULL,
    version                 INTEGER      NOT NULL DEFAULT 1,
    CONSTRAINT pk_receiving PRIMARY KEY (id),
    CONSTRAINT ck_receiving_origin_type CHECK (origin_type IN (1,2,3)),
    CONSTRAINT ck_receiving_status CHECK (status IN (1,2,3,4,5)),
    CONSTRAINT ck_receiving_version CHECK (version >= 1)
);
CREATE INDEX idx_receiving_company_created ON materials.receiving (company_id, created_at DESC, id DESC) WHERE deleted_at IS NULL;
CREATE INDEX idx_receiving_origin ON materials.receiving (company_id, origin_type, origin_reference) WHERE deleted_at IS NULL;
CREATE INDEX idx_receiving_status ON materials.receiving (company_id, status) WHERE deleted_at IS NULL;

CREATE TABLE materials.receiving_line (
    id                UUID          NOT NULL DEFAULT gen_random_uuid(),
    receiving_id      UUID          NOT NULL,
    item_id           UUID          NOT NULL,             -- MMS-002 (lógica)
    size_code         VARCHAR(30)   NULL,
    expected_qty      NUMERIC(18,4) NOT NULL,
    received_qty      NUMERIC(18,4) NOT NULL DEFAULT 0,   -- informada
    captured_uom_id   UUID          NULL,                 -- unidade de compra (ADR-013)
    applied_factor    NUMERIC(18,6) NULL,
    base_received_qty NUMERIC(18,4) NOT NULL DEFAULT 0,   -- na unidade base
    CONSTRAINT pk_receiving_line PRIMARY KEY (id),
    CONSTRAINT fk_receiving_line_rcv FOREIGN KEY (receiving_id) REFERENCES materials.receiving (id),
    CONSTRAINT ck_receiving_line_qty CHECK (expected_qty >= 0 AND received_qty >= 0 AND base_received_qty >= 0),
    CONSTRAINT ck_receiving_line_capture CHECK (
        (captured_uom_id IS NULL AND applied_factor IS NULL) OR
        (captured_uom_id IS NOT NULL AND applied_factor IS NOT NULL AND applied_factor > 0)
    )  -- IV-BR-009 / ADR-013
);
CREATE INDEX idx_receiving_line_rcv ON materials.receiving_line (receiving_id);

CREATE TABLE materials.receiving_divergence (
    id              UUID          NOT NULL DEFAULT gen_random_uuid(),
    receiving_id    UUID          NOT NULL,
    receiving_line_id UUID        NULL,
    type            SMALLINT      NOT NULL,               -- 1=falta,2=excesso,3=avaria
    qty             NUMERIC(18,4) NOT NULL,
    destination     VARCHAR(200)  NOT NULL,               -- destino documentado (RC-BR-011)
    approval_status SMALLINT      NULL,                   -- quando exige aprovação (RC-BR-040)
    reason          VARCHAR(500)  NULL,
    created_at      TIMESTAMPTZ   NOT NULL DEFAULT now(),
    created_by      UUID          NOT NULL,
    CONSTRAINT pk_receiving_divergence PRIMARY KEY (id),
    CONSTRAINT fk_receiving_divergence_rcv FOREIGN KEY (receiving_id) REFERENCES materials.receiving (id),
    CONSTRAINT fk_receiving_divergence_line FOREIGN KEY (receiving_line_id) REFERENCES materials.receiving_line (id),
    CONSTRAINT ck_receiving_divergence_type CHECK (type IN (1,2,3)),
    CONSTRAINT ck_receiving_divergence_qty CHECK (qty > 0)
);
CREATE INDEX idx_receiving_divergence_rcv ON materials.receiving_divergence (receiving_id);
```

# 4. Foreign Keys
Físicas intra-módulo (line/divergence → receiving). Lógicas: company_id/warehouse_id → Foundation/MMS-004; item_id/uom → MMS-002/FD-001-09; origin_reference → PR-001/MMS-004/MMS-003; stock_entry_movement_id → MMS-004.

# 5. Triggers
`updated_at`; `version` +1 (retrocesso → RC-ERR-030); documento Concluído/Cancelado imutável. Auditoria de negócio no FD-001-06.

# 6. Multiempresa/Soft delete/Keyset
`company_id`-first; soft delete de Aguardando/Em Conferência; keyset `(created_at, id) DESC`.

# 7. Migração/Backup
EF Core migrations por CI/CD; backup/PITR conforme MMS-002-11 §15.13. Sem projeção de saldo a reconstruir.

# 8. Histórico de Versão
| Versão | Data | Autor | Alteração |
|--------|------|-------|-----------|
| 1.0.0 | 2026-08-08 | Arquiteto Principal | Database Model do Receiving no schema `materials` (ADR-009): 3 tabelas (receiving, receiving_line, receiving_divergence), DDL com constraints (status ST-RC, conversão de UoM tudo-ou-nada, tipo de divergência, destino obrigatório), referência à entrada gerada no MMS-004, FKs físicas intra-módulo + lógicas para Foundation/MMS-002/004/PR-001/003, sem tabela de saldo (RC-BR-020) — padrão MMS-004-11/MMS-003-11. |
