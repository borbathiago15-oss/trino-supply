-- Migração inicial do Foundation (ADR-009: banco é projeção do domínio).
-- Schema `foundation`: tenant (company) + Transactional Outbox (ARC-005 §3).
-- RLS multi-tenant (ADR-015 §3, ARC-006 §12.6) — ver também deploy/db/rls.sql.
-- Idempotente: pode ser reaplicada com segurança.

CREATE SCHEMA IF NOT EXISTS foundation;

-- Empresa (tenant) — raiz do isolamento multi-tenant (FD-001-02).
CREATE TABLE IF NOT EXISTS foundation.company (
    id          UUID        NOT NULL DEFAULT gen_random_uuid(),
    legal_name  VARCHAR(200) NOT NULL,
    tax_id      VARCHAR(30)  NOT NULL,
    status      SMALLINT     NOT NULL DEFAULT 1,   -- 1=Active, 2=Inactive
    version     INTEGER      NOT NULL DEFAULT 1,
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT pk_company PRIMARY KEY (id),
    CONSTRAINT ck_company_status CHECK (status IN (1, 2)),
    CONSTRAINT ck_company_version CHECK (version >= 1)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_company_tax_id ON foundation.company (tax_id);

-- Transactional Outbox — eventos de negócio pendentes de publicação (ARC-005 §3, ADR-010).
CREATE TABLE IF NOT EXISTS foundation.outbox (
    id             UUID         NOT NULL DEFAULT gen_random_uuid(),
    company_id     UUID         NOT NULL,
    type           VARCHAR(200) NOT NULL,          -- nome de negócio do evento
    payload        JSONB        NOT NULL,          -- envelope + dados (ARC-005 §8)
    aggregate_type VARCHAR(100) NULL,
    aggregate_id   UUID         NULL,
    correlation_id UUID         NULL,
    occurred_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    published_at   TIMESTAMPTZ  NULL,
    retry_count    INTEGER      NOT NULL DEFAULT 0,
    last_error     TEXT         NULL,
    CONSTRAINT pk_outbox PRIMARY KEY (id)
);
-- Varredura de pendentes ordenada (publisher relê e publica) — ARC-005 §3.
CREATE INDEX IF NOT EXISTS idx_outbox_pending
    ON foundation.outbox (occurred_at)
    WHERE published_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_outbox_company ON foundation.outbox (company_id);

-- current_company(): tenant corrente da sessão (do JWT via SET LOCAL app.current_company).
CREATE OR REPLACE FUNCTION foundation.current_company() RETURNS uuid
LANGUAGE sql STABLE AS $$
    SELECT NULLIF(current_setting('app.current_company', true), '')::uuid
$$;

-- RLS na outbox: cada tenant só enxerga/insere suas mensagens (defesa em profundidade).
ALTER TABLE foundation.outbox ENABLE ROW LEVEL SECURITY;
ALTER TABLE foundation.outbox FORCE  ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation ON foundation.outbox;
CREATE POLICY tenant_isolation ON foundation.outbox
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());

-- Nota: a tabela foundation.company é o próprio catálogo de tenants (não recebe RLS por company_id).
-- O acesso a company é restrito por autorização de plataforma (Admin) — SEC-001.
