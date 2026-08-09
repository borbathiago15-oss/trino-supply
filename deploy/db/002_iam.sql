-- Migração IAM do Foundation (FD-001-01): papéis, usuários e RLS multi-tenant.
-- Tabelas de NEGÓCIO (escopadas ao tenant) → recebem RLS (SEC-004 §4). Idempotente.
-- Depende de 001_foundation_init.sql (schema foundation + foundation.current_company()).

-- Papel: conjunto nomeado de permissões, por tenant.
CREATE TABLE IF NOT EXISTS foundation.role (
    id          UUID         NOT NULL DEFAULT gen_random_uuid(),
    company_id  UUID         NOT NULL,
    name        VARCHAR(100) NOT NULL,
    permissions TEXT[]       NOT NULL DEFAULT '{}',
    version     INTEGER      NOT NULL DEFAULT 1,
    CONSTRAINT pk_role PRIMARY KEY (id),
    CONSTRAINT ck_role_version CHECK (version >= 1)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_role_company_name ON foundation.role (company_id, name);

-- Usuário da plataforma, por tenant. Identidade externa via `subject` (claim sub do JWT).
CREATE TABLE IF NOT EXISTS foundation.app_user (
    id           UUID         NOT NULL DEFAULT gen_random_uuid(),
    company_id   UUID         NOT NULL,
    subject      VARCHAR(200) NOT NULL,
    email        VARCHAR(200) NOT NULL,
    display_name VARCHAR(200) NOT NULL,
    status       SMALLINT     NOT NULL DEFAULT 1,   -- 1=Active, 2=Inactive
    role_ids     UUID[]       NOT NULL DEFAULT '{}',
    version      INTEGER      NOT NULL DEFAULT 1,
    CONSTRAINT pk_app_user PRIMARY KEY (id),
    CONSTRAINT ck_app_user_status CHECK (status IN (1, 2)),
    CONSTRAINT ck_app_user_version CHECK (version >= 1)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_app_user_company_subject ON foundation.app_user (company_id, subject);
CREATE UNIQUE INDEX IF NOT EXISTS uq_app_user_company_email   ON foundation.app_user (company_id, email);

-- RLS multi-tenant (SEC-004). FORCE vale inclusive para o owner da tabela.
ALTER TABLE foundation.role     ENABLE ROW LEVEL SECURITY;
ALTER TABLE foundation.role     FORCE  ROW LEVEL SECURITY;
ALTER TABLE foundation.app_user ENABLE ROW LEVEL SECURITY;
ALTER TABLE foundation.app_user FORCE  ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation ON foundation.role;
CREATE POLICY tenant_isolation ON foundation.role
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());

DROP POLICY IF EXISTS tenant_isolation ON foundation.app_user;
CREATE POLICY tenant_isolation ON foundation.app_user
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());
