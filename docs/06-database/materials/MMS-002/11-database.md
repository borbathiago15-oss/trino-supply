# MMS-002-11 — Database Model

**Documento:** MMS-002-11 — Database Model
**Módulo:** MMS-002 — Item Catalog (Catálogo de Itens)
**Versão:** 1.0.0
**Status:** 🟢 Approved
**Data:** 2026-07-30
**Dependências:** MMS-002 v1.1.0, MMS-002-02 v1.1.0, MMS-002-04 (Domain Model), MMS-002-05 (Event Storming), MMS-002-07 (Use Cases), ADR-009, ADR-010, FD-001-03, FD-001-09
**Referências:** PR-001-11 (padrão de formato Enterprise — `docs/06-database/procurement/PR-001/11-database.md`), GOV-001

> Decisão arquitetural associada: ADR-009 — Banco de dados como projeção do domínio.
> Modelo de persistência do módulo Item Catalog.

---

# 1. Objetivo

Definir a estrutura de persistência responsável pelo armazenamento dos itens do catálogo (EPI, fardamento e materiais) e seus componentes (sinônimos e parâmetros de reposição).

Este documento representa a implementação física do modelo de domínio descrito em **MMS-002-04**.

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
* Aggregate enxuto: imagem via FD-001-03 (referência lógica), auditoria/timeline via Foundation — nunca tabelas próprias.

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

## item

### Chave

```text
id
```

### Campos

| Campo              | Tipo          |
| ------------------ | ------------- |
| id                 | UUID          |
| company_id         | UUID          |
| code               | VARCHAR(50)   |
| description        | VARCHAR(500)  |
| unit_of_measure_id | UUID          |
| group_id           | UUID          |
| category_id        | UUID          |
| erp_code           | VARCHAR(50)   |
| ca_number          | VARCHAR(30)   |
| ca_expiry_date     | DATE          |
| size_grid_id       | UUID          |
| image_file_id      | UUID          |
| status             | SMALLINT      |
| created_at         | TIMESTAMP     |
| updated_at         | TIMESTAMP     |
| deleted_at         | TIMESTAMP NULL |
| version            | INTEGER       |

---

## item_synonym

| Campo           | Tipo         |
| --------------- | ------------ |
| id              | UUID         |
| item_id         | UUID         |
| term            | VARCHAR(100) |
| normalized_term | VARCHAR(100) |
| created_at      | TIMESTAMP    |
| deleted_at      | TIMESTAMP NULL |

---

## item_replenishment_parameters

| Campo            | Tipo          |
| ---------------- | ------------- |
| item_id          | UUID (PK/FK)  |
| reorder_point    | NUMERIC(18,4) |
| min_stock        | NUMERIC(18,4) |
| max_stock        | NUMERIC(18,4) |
| lead_time_days   | INTEGER       |
| created_at       | TIMESTAMP     |
| updated_at       | TIMESTAMP     |
| version          | INTEGER       |

---

# 5. Relacionamentos

```
Item

1

↓

N

Item Synonym

1

↓

0..1

Item Replenishment Parameters

Referências lógicas (sem FK física):

item.unit_of_measure_id → FD-001-09 Master Data (unidade de medida)
item.group_id / category_id → FD-001-09 Master Data (grupo/categoria)
item.size_grid_id → FD-001-09 Master Data (grade SIZE_GRID)
item.image_file_id → FD-001-03 Document Management (MinIO)
item.company_id → FD-001-01/02 Foundation (empresa)
```

---

# 6. Índices

## item

```sql
idx_item_code
idx_item_status
idx_item_company
idx_item_group
idx_item_category
idx_item_created_at
idx_item_erp_code
```

---

## item_synonym

```sql
idx_item_synonym_item
idx_item_synonym_normalized
```

---

# 7. Constraints

Código único por empresa (soft delete ciente).

Código ERP único por empresa quando informado.

Status válido (1=Rascunho, 2=Ativo, 3=Inativo, 4=Inativo/Descarte).

Sinônimo único por item (termo normalizado).

Coerência de parâmetros: min ≤ reorder_point ≤ max (quando informados).

Versão >= 1.

---

# 8. Soft Delete

Todas as entidades utilizarão:

```text
deleted_at

deleted_by
```

Nunca exclusão física. O descarte de item (ST-IC-004) é transição de status, **não** soft delete — o registro permanece consultável para histórico.

---

# 9. Multiempresa

Toda tabela principal deverá possuir:

```text
company_id
```

Nenhuma consulta poderá ignorar esse filtro.

---

# 10. Auditoria

A auditoria operacional ficará em um domínio compartilhado (FD-001-06).

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

Identity (FD-001-01)

Document Management (FD-001-03 — imagem)

Master Data (FD-001-09 — unidade, grupo, categoria, grade)

Audit (FD-001-06)

Notification (FD-001-05)

---

# 14. Nota Arquitetural — Aggregate enxuto (adotada)

Diferentemente do PR-001-11 (cuja proposta está em análise), o Item Catalog **já nasce com aggregate enxuto**, aderente à direção da ADR-009:

* Imagem do item → referência lógica (`image_file_id`) ao Document Management (FD-001-03); binário no MinIO, metadados no Foundation.
* Timeline / Audit / Notification → serviços transversais do Foundation (FD-001-05/06/07).
* Unidade, grupo, categoria e grade de tamanhos → referências lógicas ao Master Data (FD-001-09); validação na camada de aplicação/domínio.

Modelo:

```text
Item (aggregate root)
├── ItemSynonym (entidade interna)
├── ItemReplenishmentParameters (value object persistido 1:1)
└── Referências lógicas para:
    ├── MasterDataValue (unidade, grupo, categoria, grade)
    ├── DocumentFile (imagem)
    ├── AuditTrail
    └── Timeline
```

Benefícios: reutilização entre módulos, menor acoplamento, evolução independente dos serviços e melhor aderência ao DDD.

---

# 15. DDL PostgreSQL (implementação física)

> Implementação física oficial do modelo das Seções 4–7. Schema: `materials`. Todas as datas em UTC (`TIMESTAMPTZ` na implementação física, equivalente ao `TIMESTAMP` do modelo conceitual). FKs para entidades do Foundation (empresa, master data, arquivo) são lógicas/referenciais por serviço — ver §15.3.

## 15.1 Tabelas

```sql
CREATE SCHEMA IF NOT EXISTS materials;

CREATE TABLE materials.item (
    id                 UUID          NOT NULL DEFAULT gen_random_uuid(),
    company_id         UUID          NOT NULL,
    code               VARCHAR(50)   NOT NULL,
    description        VARCHAR(500)  NOT NULL,
    unit_of_measure_id UUID          NOT NULL,
    group_id           UUID          NOT NULL,
    category_id        UUID          NOT NULL,
    erp_code           VARCHAR(50)   NULL,
    ca_number          VARCHAR(30)   NULL,
    ca_expiry_date     DATE          NULL,
    size_grid_id       UUID          NULL,
    image_file_id      UUID          NULL,
    status             SMALLINT      NOT NULL DEFAULT 1,
    created_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    updated_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    deleted_at         TIMESTAMPTZ   NULL,
    created_by         UUID          NOT NULL,
    updated_by         UUID          NOT NULL,
    deleted_by         UUID          NULL,
    version            INTEGER       NOT NULL DEFAULT 1,
    CONSTRAINT pk_item PRIMARY KEY (id),
    CONSTRAINT ck_item_status CHECK (status IN (1, 2, 3, 4)), -- 1=Rascunho, 2=Ativo, 3=Inativo, 4=Inativo/Descarte
    CONSTRAINT ck_item_version CHECK (version >= 1),
    CONSTRAINT ck_item_description CHECK (char_length(description) BETWEEN 3 AND 500),
    CONSTRAINT ck_item_code_format CHECK (code ~ '^[A-Za-z0-9._/-]+$'),
    CONSTRAINT ck_item_ca_consistency CHECK (
        (ca_number IS NULL AND ca_expiry_date IS NULL) OR
        (ca_number IS NOT NULL)
    ), -- validade só existe com CA; formato/vigência validados no domínio (IC-BR-081)
    CONSTRAINT ck_item_soft_delete CHECK (
        (deleted_at IS NULL AND deleted_by IS NULL) OR
        (deleted_at IS NOT NULL AND deleted_by IS NOT NULL)
    )
);

-- Unicidade de código por empresa, ciente de soft delete (IC-BR-001)
CREATE UNIQUE INDEX uq_item_code_company
    ON materials.item (company_id, code)
    WHERE deleted_at IS NULL;

-- Unicidade de código ERP por empresa quando informado (IC-BR-011)
CREATE UNIQUE INDEX uq_item_erp_code_company
    ON materials.item (company_id, erp_code)
    WHERE deleted_at IS NULL AND erp_code IS NOT NULL;

CREATE TABLE materials.item_synonym (
    id              UUID         NOT NULL DEFAULT gen_random_uuid(),
    item_id         UUID         NOT NULL,
    term            VARCHAR(100) NOT NULL,
    normalized_term VARCHAR(100) NOT NULL,
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    deleted_at      TIMESTAMPTZ  NULL,
    created_by      UUID         NOT NULL,
    deleted_by      UUID         NULL,
    CONSTRAINT pk_item_synonym PRIMARY KEY (id),
    CONSTRAINT fk_item_synonym_item
        FOREIGN KEY (item_id)
        REFERENCES materials.item (id),
    CONSTRAINT ck_item_synonym_term CHECK (char_length(term) BETWEEN 2 AND 100),
    CONSTRAINT ck_item_synonym_normalized CHECK (normalized_term = lower(btrim(term)))
);

-- Sinônimo único por item, termo normalizado (IC-BR-040)
CREATE UNIQUE INDEX uq_item_synonym_term
    ON materials.item_synonym (item_id, normalized_term)
    WHERE deleted_at IS NULL;

CREATE TABLE materials.item_replenishment_parameters (
    item_id        UUID          NOT NULL,
    reorder_point  NUMERIC(18,4) NULL,
    min_stock      NUMERIC(18,4) NULL,
    max_stock      NUMERIC(18,4) NULL,
    lead_time_days INTEGER       NULL,
    created_at     TIMESTAMPTZ   NOT NULL DEFAULT now(),
    updated_at     TIMESTAMPTZ   NOT NULL DEFAULT now(),
    created_by     UUID          NOT NULL,
    updated_by     UUID          NOT NULL,
    version        INTEGER       NOT NULL DEFAULT 1,
    CONSTRAINT pk_item_replenishment PRIMARY KEY (item_id),
    CONSTRAINT fk_item_replenishment_item
        FOREIGN KEY (item_id)
        REFERENCES materials.item (id),
    CONSTRAINT ck_item_repl_nonneg CHECK (
        (reorder_point IS NULL OR reorder_point >= 0) AND
        (min_stock IS NULL OR min_stock >= 0) AND
        (max_stock IS NULL OR max_stock >= 0)
    ),
    CONSTRAINT ck_item_repl_coherence CHECK (
        (min_stock IS NULL OR max_stock IS NULL OR min_stock <= max_stock) AND
        (reorder_point IS NULL OR min_stock IS NULL OR reorder_point >= min_stock) AND
        (reorder_point IS NULL OR max_stock IS NULL OR reorder_point <= max_stock)
    ), -- IC-BR-050
    CONSTRAINT ck_item_repl_lead_time CHECK (lead_time_days IS NULL OR lead_time_days >= 0), -- IC-BR-052
    CONSTRAINT ck_item_repl_version CHECK (version >= 1)
);
```

## 15.2 Índices (implementação física)

```sql
-- item (modelo conceitual Seção 6, expandido com padrão company_id-first)
CREATE INDEX idx_item_code      ON materials.item (company_id, code)              WHERE deleted_at IS NULL;
CREATE INDEX idx_item_status    ON materials.item (company_id, status)            WHERE deleted_at IS NULL;
CREATE INDEX idx_item_company   ON materials.item (company_id)                    WHERE deleted_at IS NULL;
CREATE INDEX idx_item_group     ON materials.item (company_id, group_id)          WHERE deleted_at IS NULL;
CREATE INDEX idx_item_category  ON materials.item (company_id, category_id)       WHERE deleted_at IS NULL;
CREATE INDEX idx_item_created_at ON materials.item (company_id, created_at DESC, id DESC) WHERE deleted_at IS NULL;
CREATE INDEX idx_item_erp_code  ON materials.item (company_id, erp_code)          WHERE deleted_at IS NULL AND erp_code IS NOT NULL;

-- Busca operacional: itens ativos por empresa (projeção de leitura — IC-BR-021/071)
CREATE INDEX idx_item_active    ON materials.item (company_id, description)       WHERE deleted_at IS NULL AND status = 2;

-- EPI sem CA (fiscalização — US-IC-013)
CREATE INDEX idx_item_epi_sem_ca ON materials.item (company_id, group_id)         WHERE deleted_at IS NULL AND ca_number IS NULL;

-- item_synonym
CREATE INDEX idx_item_synonym_item       ON materials.item_synonym (item_id)      WHERE deleted_at IS NULL;
CREATE INDEX idx_item_synonym_normalized ON materials.item_synonym (normalized_term) WHERE deleted_at IS NULL;
```

**Padrão:** todos os índices de consulta iniciam por `company_id` (isolamento multiempresa, Seção 9) e são parciais (`WHERE deleted_at IS NULL`) para não carregar lixo de soft delete.

## 15.3 Foreign Keys

| FK | Origem → Destino | Tipo | Justificativa |
| -- | ---------------- | ---- | ------------- |
| fk_item_synonym_item | item_synonym → item | Física (dentro do módulo) | Integridade do aggregate |
| fk_item_replenishment_item | item_replenishment_parameters → item | Física | Integridade do aggregate (1:1) |
| company_id | → Foundation (FD-001-01/02) | **Lógica** (sem constraint física) | Baixo acoplamento entre bounded contexts; validação na camada de aplicação/domínio; ADR-009 |
| unit_of_measure_id, group_id, category_id, size_grid_id | → Master Data (FD-001-09) | **Lógica** | Idem; validação nas IC-BR-003/004/005/082 |
| image_file_id | → Document Management (FD-001-03) | **Lógica** | Idem; validação na IC-BR-083 |

## 15.4 Triggers

```sql
-- TRG-IC-001: manter updated_at
CREATE OR REPLACE FUNCTION materials.fn_set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_item_updated_at
    BEFORE UPDATE ON materials.item
    FOR EACH ROW EXECUTE FUNCTION materials.fn_set_updated_at();

CREATE TRIGGER trg_item_repl_updated_at
    BEFORE UPDATE ON materials.item_replenishment_parameters
    FOR EACH ROW EXECUTE FUNCTION materials.fn_set_updated_at();

-- TRG-IC-002: incrementar version (optimistic concurrency) e impedir seu retrocesso
CREATE OR REPLACE FUNCTION materials.fn_increment_version()
RETURNS TRIGGER AS $$
BEGIN
    IF NEW.version <> OLD.version + 1 THEN
        RAISE EXCEPTION 'IC-ERR-409: version must increment by 1 (expected %, got %)', OLD.version + 1, NEW.version
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_item_version
    BEFORE UPDATE ON materials.item
    FOR EACH ROW EXECUTE FUNCTION materials.fn_increment_version();

CREATE TRIGGER trg_item_repl_version
    BEFORE UPDATE ON materials.item_replenishment_parameters
    FOR EACH ROW EXECUTE FUNCTION materials.fn_increment_version();

-- TRG-IC-003: estado terminal — item descartado (status=4) é imutável
CREATE OR REPLACE FUNCTION materials.fn_item_terminal_immutable()
RETURNS TRIGGER AS $$
BEGIN
    IF OLD.status = 4 THEN
        RAISE EXCEPTION 'IC-ERR-090: discarded items are immutable (terminal state)'
            USING ERRCODE = 'raise_exception';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_item_terminal_immutable
    BEFORE UPDATE ON materials.item
    FOR EACH ROW EXECUTE FUNCTION materials.fn_item_terminal_immutable();
```

**Nota:** a auditoria de alterações (quem/o quê/quando) é registrada pelo domínio compartilhado (Seção 10); os triggers acima protegem apenas integridade técnica, não substituem a auditoria de negócio.

## 15.5 Views

```sql
-- VW-IC-001: listagem do catálogo (governo — todos os estados, sem colunas de soft delete)
CREATE VIEW materials.vw_item_catalog AS
SELECT
    i.id,
    i.company_id,
    i.code,
    i.description,
    i.unit_of_measure_id,
    i.group_id,
    i.category_id,
    i.erp_code,
    i.ca_number,
    i.ca_expiry_date,
    i.size_grid_id,
    i.image_file_id,
    i.status,
    (i.ca_number IS NOT NULL)  AS has_ca,
    (i.image_file_id IS NOT NULL) AS has_image,
    (SELECT count(*) FROM materials.item_synonym s
      WHERE s.item_id = i.id AND s.deleted_at IS NULL) AS synonym_count,
    i.created_at,
    i.updated_at
FROM materials.item i
WHERE i.deleted_at IS NULL;

-- VW-IC-002: busca operacional (apenas Ativos — recorte dos módulos consumidores, IC-BR-021)
CREATE VIEW materials.vw_item_operational AS
SELECT
    i.id,
    i.company_id,
    i.code,
    i.description,
    i.unit_of_measure_id,
    i.group_id,
    i.category_id,
    i.size_grid_id,
    i.image_file_id
FROM materials.item i
WHERE i.deleted_at IS NULL
  AND i.status = 2;

-- VW-IC-003: EPIs sem CA pendente de regularização (fiscalização — US-IC-013)
CREATE VIEW materials.vw_item_epi_sem_ca AS
SELECT
    i.id,
    i.company_id,
    i.code,
    i.description,
    i.category_id,
    i.status,
    i.created_at
FROM materials.item i
WHERE i.deleted_at IS NULL
  AND i.ca_number IS NULL
  AND i.status IN (1, 2); -- Rascunho ou Ativo sem CA (Ativo sem CA só ocorre se grupo mudou — POL-IC-05)
```

## 15.6 Materialized Views

```sql
-- MV-IC-001: painel gerencial do catálogo por empresa/status/grupo (analytics leve; KPIs oficiais derivados da Timeline)
CREATE MATERIALIZED VIEW materials.mv_item_status_summary AS
SELECT
    company_id,
    status,
    group_id,
    count(*) AS total,
    count(*) FILTER (WHERE ca_number IS NULL)     AS sem_ca,
    count(*) FILTER (WHERE image_file_id IS NULL) AS sem_imagem
FROM materials.item
WHERE deleted_at IS NULL
GROUP BY company_id, status, group_id;

CREATE UNIQUE INDEX uq_mv_item_status_summary
    ON materials.mv_item_status_summary (company_id, status, group_id);
```

**Estratégia de refresh:** `REFRESH MATERIALIZED VIEW CONCURRENTLY materials.mv_item_status_summary` a cada 15 minutos (job agendado) ou sob demanda. Indicadores oficiais do processo (MMS-002-06 §10) são derivados da Timeline imutável, não desta view.

## 15.7 Stored Procedures / Functions de negócio

A regra do projeto é que **a lógica de negócio vive no domínio (.NET)**, não no banco (ADR-009). O Item Catalog **não possui procedures de negócio**: o código do item é informado pelo usuário (não há sequencial gerado, diferentemente do PR-001) e a normalização de sinônimos é garantida por constraint determinística (`ck_item_synonym_normalized`).

Funções em banco admitidas apenas para:

| Função | Propósito | Justificativa |
| ------ | --------- | ------------- |
| Triggers §15.4 | Integridade técnica (updated_at, version, estado terminal) | Proteção de última linha de defesa |

Qualquer nova procedure exige justificativa arquitetural registrada em ADR.

## 15.8 Estratégia de Particionamento

**Decisão para o MVP:** sem particionamento físico. Os volumes projetados de catálogo (centenas a poucos milhares de itens por empresa) não justificam a complexidade operacional.

**Gatilhos para ativar particionamento (revisão semestral):**

| Tabela | Gatilho | Estratégia-alvo |
| ------ | ------- | --------------- |
| item | > 5 milhões de linhas por empresa ou > 20 GB | `PARTITION BY LIST (company_id)` para grandes tenants |
| item_synonym | > 20 milhões de linhas | `PARTITION BY HASH (item_id)` com 16 partições |
| item_replenishment_parameters | Nunca (1:1 com item) | Acompanha a estratégia de `item` se necessário |

**Regras:** particionamento nunca por chave de negócio editável; partição destacável para arquivamento (`DETACH PARTITION`) antes de qualquer purge legal; soft delete continua sendo a regra de exclusão lógica independentemente do particionamento.

## 15.9 Keyset Pagination (padrão de consulta)

Toda listagem da API usa keyset pagination (Seção 12), com cursor opaco e assinado.

**Chave de paginação oficial:** `(created_at DESC, id DESC)` — determinística, alinhada ao índice `idx_item_created_at`.

```sql
-- Primeira página (listagem de governo do catálogo)
SELECT id, code, description, group_id, category_id, status, created_at
FROM materials.item
WHERE company_id = :company_id
  AND deleted_at IS NULL
  AND (:status IS NULL OR status = :status)
  AND (:group_id IS NULL OR group_id = :group_id)
ORDER BY created_at DESC, id DESC
LIMIT :page_size;

-- Página seguinte (cursor = created_at + id do último item)
SELECT id, code, description, group_id, category_id, status, created_at
FROM materials.item
WHERE company_id = :company_id
  AND deleted_at IS NULL
  AND (:status IS NULL OR status = :status)
  AND (:group_id IS NULL OR group_id = :group_id)
  AND (created_at, id) < (:cursor_created_at, :cursor_id)
ORDER BY created_at DESC, id DESC
LIMIT :page_size;

-- Busca operacional por termo (código, descrição ou sinônimo — UC-IC-006)
SELECT DISTINCT i.id, i.code, i.description
FROM materials.item i
LEFT JOIN materials.item_synonym s
  ON s.item_id = i.id AND s.deleted_at IS NULL
WHERE i.company_id = :company_id
  AND i.deleted_at IS NULL
  AND i.status = 2
  AND (
      i.code ILIKE '%' || :term || '%'
      OR i.description ILIKE '%' || :term || '%'
      OR s.normalized_term ILIKE '%' || lower(btrim(:term)) || '%'
  )
ORDER BY i.code, i.id
LIMIT :page_size;
```

**Regras:**

- `page_size` entre 1 e 100 (padrão 20).
- Cursor opaco, assinado (HMAC) e com expiração de 15 minutos; inválido/expirado → `IC-ERR-400`.
- Proibido `OFFSET` em qualquer endpoint (UC-IC-006).
- Filtros opcionais devem ser compatíveis com os índices §15.2 (filtro sem índice exige revisão do plano de indexação antes do deploy).
- Busca textual do MVP usa `ILIKE` sobre colunas indexadas + sinônimos normalizados; evolução para `pg_trgm`/full-text search exige revisão deste documento e registro em ADR.

## 15.10 Plano de Indexação

| Consulta crítica | Índice de suporte | Cobertura |
| ---------------- | ----------------- | --------- |
| Listagem por empresa + ordenação temporal (UC-IC-006) | `idx_item_created_at` | Scan parcial por empresa, sem sort adicional |
| Filtro por status | `idx_item_status` | Prefixo `company_id` garante seletividade |
| Busca por código | `uq_item_code_company` / `idx_item_code` | Lookup direto |
| Busca operacional (apenas Ativos) | `idx_item_active` (parcial `status = 2`) | Pequeno e quente |
| Filtro por grupo/categoria | `idx_item_group`, `idx_item_category` | Filtros secundários |
| Busca por sinônimo | `idx_item_synonym_normalized` + `idx_item_synonym_item` | Termo normalizado → item |
| Fiscalização EPI sem CA (US-IC-013) | `idx_item_epi_sem_ca` (parcial) | Lista reduzida |
| Detalhe com parâmetros | PK de `item_replenishment_parameters` (1:1) | Lookup direto |
| Código ERP | `uq_item_erp_code_company` / `idx_item_erp_code` | Lookup direto |

**Governança de índices:**

- Todo novo índice exige: consulta-alvo documentada, validação com `EXPLAIN ANALYZE` em volume representativo e registro nesta tabela.
- Índices não utilizados são revistos trimestralmente (`pg_stat_user_indexes.idx_scan = 0` por 90 dias → candidato a remoção).
- `ANALYZE` automático (autovacuum) habilitado; `default_statistics_target = 100` nas colunas de filtro frequente.

## 15.11 Plano de Performance

| Tema | Diretriz |
| ---- | -------- |
| **Consultas** | Sempre paginadas (§15.9), filtradas por `company_id` primeiro, projeção apenas das colunas necessárias (sem `SELECT *` em listagens) |
| **N+1** | Detalhe do item carrega sinônimos e parâmetros em uma única query por `item_id`; proibido loop de queries |
| **Transações** | Curtas; alteração do aggregate + outbox na mesma transação (MMS-002-05 §14.1); nunca esperar I/O externo dentro de transação |
| **Locks** | Concorrência otimista via `version` (TRG-IC-002); sem locks pessimistas no catálogo |
| **Isolation level** | `READ COMMITTED` padrão; `SERIALIZABLE` não utilizado |
| **Pool** | PgBouncer (transaction pooling); máx. de conexões por serviço configurado (`db.pool.max`, padrão 20) |
| **Timeouts** | `statement_timeout = 30s` em OLTP; `lock_timeout = 5s`; `idle_in_transaction_session_timeout = 60s` |
| **Cache** | Projeção de leitura do catálogo em Redis (IC-BR-071), invalidada por evento (POL-IC-03); cache nunca é fonte de verdade (COMP-IC-003) |
| **Metas** | Busca operacional paginada p95 < 200 ms; detalhe p95 < 150 ms; escrita p95 < 300 ms |
| **Monitoramento** | `pg_stat_statements` habilitado; alerta para queries p95 acima da meta ou seq scans em tabelas > 100k linhas |
| **Manutenção** | Autovacuum ajustado para tabelas quentes (`item`: `autovacuum_vacuum_scale_factor = 0.05`); `REINDEX CONCURRENTLY` em janela de manutenção quando bloat > 30% |

## 15.12 Estratégia de Migração

| Aspecto | Diretriz |
| ------- | -------- |
| **Ferramenta** | Migrations versionadas via EF Core Migrations (assembly do módulo Materials), aplicadas por pipeline CI/CD — nunca manual em produção |
| **Formato** | Uma migration por alteração lógica; nome `YYYYMMDDHHMM_<descricao>`; SQL gerado revisado em PR |
| **Ordem** | Expand-and-contract: (1) adicionar nova estrutura compatível; (2) migrar dados; (3) remover estrutura antiga em release posterior |
| **Backward compatibility** | Toda migration deve ser compatível com a versão N-1 da aplicação (deploy blue/green sem downtime) |
| **Operações online** | Índices com `CREATE INDEX CONCURRENTLY`; alterações de tipo de coluna via nova coluna + backfill + swap |
| **Backfill** | Em lotes (`batch_size = 10.000`), com `statement_timeout` elevado apenas no job, monitorado e pausável |
| **Rollback** | Toda migration possui `Down()` testado; rollback de dados irreversível exige aprovação explícita |
| **Seeds** | Dados de referência (configurações padrão `materials.item.*`) em migrations idempotentes |
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
| 1.0.0 | 2026-07-30 | Arquiteto Principal | Versão inicial aprovada: modelo físico do Item Catalog no schema `materials` (aggregate enxuto — ADR-009): 3 tabelas (item, item_synonym, item_replenishment_parameters), DDL PostgreSQL completo com constraints e check constraints, unicidade soft-delete-ciente de código e código ERP, 10+ índices parciais company_id-first, FKs físicas internas + lógicas para Foundation, 3 triggers (updated_at, version, imutabilidade de descarte), 3 views (catálogo, operacional, EPI sem CA), 1 materialized view, sem procedures de negócio, estratégia de particionamento, keyset pagination oficial, plano de indexação, plano de performance, estratégia de migração, política de backup e versionamento de schema. |
