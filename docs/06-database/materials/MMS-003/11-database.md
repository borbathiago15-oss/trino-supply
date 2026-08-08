# MMS-003-11 — Database Model

**Documento:** MMS-003-11 — Database Model
**Módulo:** MMS-003 — Material Requisition (Solicitação de Material)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-08-08
**Dependências:** MMS-003 v1.1.0, MMS-003-02 (Business Rules), MMS-003-03 (State Machine), MMS-003-04 (Domain Model), ADR-009, ADR-010, ADR-013, MMS-002-11 (schema `materials`), FD-001-03/06
**Referências:** MMS-004-11 / MMS-002-11 (padrão de formato), GOV-001

> Implementação física do modelo de domínio **MMS-003-04** (ADR-009). Schema `materials`. O módulo **não persiste saldo** (MR-BR-050): reservas/entregas vivem no MMS-004; aqui guardam-se apenas referências (`reservation_ref`, `purchase_ref`).

---

# 1. Princípios
PostgreSQL; UUID PK; UTC (`TIMESTAMPTZ`); multiempresa por `company_id`; soft delete de rascunho; documentos processados são imutáveis em campos estruturais (matriz de editabilidade — MMS-003-03 §8); versionamento otimista; nenhuma tabela de saldo.

# 2. Convenções
`id UUID PK` · `created_at/updated_at/deleted_at` · `created_by/updated_by/deleted_by` · `version INTEGER` · `status SMALLINT` (códigos ST-MR da State Machine).

# 3. Entidades

| Tabela | Papel | Domínio (MMS-003-04) |
|--------|-------|----------------------|
| `material_requisition` | Cabeçalho da solicitação | MaterialRequisition (AR) |
| `material_requisition_item` | Linha (item) | RequisitionItem |
| `material_requisition_attachment` | Anexo (ref FD-001-03) | RequisitionAttachment |
| `delivery_location` | Local de entrega | DeliveryLocation (AR) |

# 4. DDL PostgreSQL

```sql
-- Locais de entrega (código gerado pelo sistema — MR-BR-060)
CREATE TABLE materials.delivery_location (
    id           UUID         NOT NULL DEFAULT gen_random_uuid(),
    company_id   UUID         NOT NULL,
    code         VARCHAR(30)  NOT NULL,               -- gerado pelo sistema
    description  VARCHAR(200) NOT NULL,
    org_unit_id  UUID         NOT NULL,               -- FD-001-02 (lógica)
    active       BOOLEAN      NOT NULL DEFAULT true,
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
    deleted_at   TIMESTAMPTZ  NULL,
    created_by   UUID         NOT NULL,
    updated_by   UUID         NOT NULL,
    deleted_by   UUID         NULL,
    version      INTEGER      NOT NULL DEFAULT 1,
    CONSTRAINT pk_delivery_location PRIMARY KEY (id),
    CONSTRAINT ck_delivery_location_version CHECK (version >= 1)
);
CREATE UNIQUE INDEX uq_delivery_location_code ON materials.delivery_location (company_id, code) WHERE deleted_at IS NULL;

-- Cabeçalho da solicitação
CREATE TABLE materials.material_requisition (
    id                   UUID         NOT NULL DEFAULT gen_random_uuid(),
    company_id           UUID         NOT NULL,
    requester_id         UUID         NOT NULL,        -- FD-001-01 (lógica)
    cost_center_id       UUID         NOT NULL,        -- FD-001-02 (lógica)
    delivery_location_id UUID         NOT NULL,
    justification        VARCHAR(1000) NOT NULL,       -- MR-BR-003
    reason_type_code     VARCHAR(40)  NULL,            -- FD-001-09 (MR-BR-004)
    reason_code          VARCHAR(40)  NULL,
    needed_date          DATE         NULL,
    status               SMALLINT     NOT NULL DEFAULT 1,  -- ST-MR-001..009
    approval_decision    JSONB        NULL,            -- decisor, data/hora, parecer, ajustes por item (MR-BR-031)
    workflow_instance_id UUID         NULL,            -- FD-001-04 (lógica)
    created_at           TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at           TIMESTAMPTZ  NOT NULL DEFAULT now(),
    deleted_at           TIMESTAMPTZ  NULL,
    created_by           UUID         NOT NULL,
    updated_by           UUID         NOT NULL,
    deleted_by           UUID         NULL,
    version              INTEGER      NOT NULL DEFAULT 1,
    CONSTRAINT pk_material_requisition PRIMARY KEY (id),
    CONSTRAINT fk_requisition_delivery FOREIGN KEY (delivery_location_id) REFERENCES materials.delivery_location (id),
    CONSTRAINT ck_requisition_status CHECK (status IN (1,2,3,4,5,6,7,8,9)),
    CONSTRAINT ck_requisition_reason CHECK ((reason_type_code IS NULL) = (reason_code IS NULL)),
    CONSTRAINT ck_requisition_version CHECK (version >= 1)
);
CREATE INDEX idx_requisition_company_created ON materials.material_requisition (company_id, created_at DESC, id DESC) WHERE deleted_at IS NULL;
CREATE INDEX idx_requisition_requester ON materials.material_requisition (company_id, requester_id, status) WHERE deleted_at IS NULL;
CREATE INDEX idx_requisition_status ON materials.material_requisition (company_id, status) WHERE deleted_at IS NULL;         -- visão do almoxarifado (aprovadas)
CREATE INDEX idx_requisition_cost_center ON materials.material_requisition (company_id, cost_center_id) WHERE deleted_at IS NULL;

-- Itens da solicitação
CREATE TABLE materials.material_requisition_item (
    id               UUID          NOT NULL DEFAULT gen_random_uuid(),
    requisition_id   UUID          NOT NULL,
    item_id          UUID          NOT NULL,           -- MMS-002 (lógica; item Ativo — MR-BR-010)
    size_code        VARCHAR(30)   NULL,               -- obrigatório com grade (MR-BR-012)
    requested_qty    NUMERIC(18,4) NOT NULL,           -- > 0, unidade base (MR-BR-011)
    approved_qty     NUMERIC(18,4) NULL,               -- aprovação parcial (MR-BR-031)
    item_status      SMALLINT      NOT NULL DEFAULT 1, -- pendente..recebido..rejeitado
    route            SMALLINT      NULL,               -- 1=estoque, 2=compra, NULL=não roteado
    reservation_ref  UUID          NULL,               -- MMS-004 (lógica)
    purchase_ref     VARCHAR(100)  NULL,               -- PR-001 (lógica — rastreab. bidirecional MR-BR-043)
    decision_reason  VARCHAR(500)  NULL,               -- motivo do decisor (reduzido/rejeitado)
    CONSTRAINT pk_requisition_item PRIMARY KEY (id),
    CONSTRAINT fk_requisition_item_req FOREIGN KEY (requisition_id) REFERENCES materials.material_requisition (id),
    CONSTRAINT ck_requisition_item_qty CHECK (requested_qty > 0 AND (approved_qty IS NULL OR approved_qty >= 0)),
    CONSTRAINT ck_requisition_item_route CHECK (route IS NULL OR route IN (1,2))
);
CREATE INDEX idx_requisition_item_req ON materials.material_requisition_item (requisition_id);
CREATE INDEX idx_requisition_item_item ON materials.material_requisition_item (item_id);
CREATE INDEX idx_requisition_item_purchase ON materials.material_requisition_item (purchase_ref) WHERE purchase_ref IS NOT NULL;

-- Anexos (referência ao Document Management — FD-001-03)
CREATE TABLE materials.material_requisition_attachment (
    id             UUID         NOT NULL DEFAULT gen_random_uuid(),
    requisition_id UUID         NOT NULL,
    document_id    UUID         NOT NULL,              -- FD-001-03 (MinIO)
    kind           VARCHAR(40)  NULL,
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by     UUID         NOT NULL,
    deleted_at     TIMESTAMPTZ  NULL,
    deleted_by     UUID         NULL,
    CONSTRAINT pk_requisition_attachment PRIMARY KEY (id),
    CONSTRAINT fk_requisition_attachment_req FOREIGN KEY (requisition_id) REFERENCES materials.material_requisition (id)
);
CREATE INDEX idx_requisition_attachment_req ON materials.material_requisition_attachment (requisition_id) WHERE deleted_at IS NULL;
```

# 5. Foreign Keys

| FK | Tipo | Justificativa |
|----|------|---------------|
| item/attachment → requisition; requisition → delivery_location | Física | Integridade intra-módulo |
| company_id, requester_id, cost_center_id, org_unit_id, workflow_instance_id | **Lógica** | Foundation (ADR-009) |
| item_id, size_code, reason_* | **Lógica** | MMS-002 / FD-001-09 |
| reservation_ref → MMS-004; purchase_ref → PR-001; document_id → FD-001-03 | **Lógica** | Contextos externos (ARC-002) |

# 6. Triggers
`updated_at` automático; `version` incrementa de 1 em 1 (retrocesso → MR-ERR-409); campos estruturais imutáveis fora de Rascunho (matriz de editabilidade — MMS-003-03 §8). Auditoria de negócio no Foundation (FD-001-06), nunca em tabela do módulo.

# 7. Multiempresa, Soft Delete, Versionamento, Performance
`company_id`-first em todo índice (MR-BR-001); soft delete de rascunho; `version` (MR-BR-091); keyset por `(created_at, id) DESC` (extrato e filas), `page_size` padrão 20 (visão do solicitante) / 50 (visão do almoxarifado). Sem `OFFSET`.

# 8. Visão do Almoxarifado (consulta)
Filtro por status = Aprovada/Em Atendimento/Aguardando Compra e por solicitante, período, centro de custo, empresa, categoria (via item), número — sobre `idx_requisition_status` + join com item/catálogo. Acesso restrito aos papéis de almoxarifado (MR-BR-070) — filtro de permissão na aplicação.

# 9. Migração e Backup
EF Core migrations por CI/CD; expand-and-contract; backup/PITR conforme política da suíte (MMS-002-11 §15.13). Nenhuma projeção de saldo a reconstruir (o módulo não tem saldo).

# 10. Histórico de Versão

| Versão | Data | Autor | Alteração |
|--------|------|-------|-----------|
| 1.0.0 | 2026-08-08 | Arquiteto Principal | Criação do Database Model do Material Requisition no schema `materials` (ADR-009): 4 tabelas (material_requisition, material_requisition_item, material_requisition_attachment, delivery_location), DDL com constraints (status ST-MR, quantidade > 0 na unidade base, rota estoque/compra, código de local único por empresa), FKs físicas intra-módulo + lógicas para Foundation/MMS-002/MMS-004/PR-001/FD-001-03, índices company_id-first para visão do solicitante e do almoxarifado, referências de reserva/compra (rastreabilidade bidirecional), sem tabela de saldo (MR-BR-050) — padrão MMS-004-11. |
