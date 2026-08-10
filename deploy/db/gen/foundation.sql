DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'foundation') THEN
        CREATE SCHEMA foundation;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS foundation.__ef_migrations (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809024405_InitialFoundation') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'foundation') THEN
            CREATE SCHEMA foundation;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809024405_InitialFoundation') THEN
    CREATE TABLE foundation.app_user (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        subject character varying(200) NOT NULL,
        email character varying(200) NOT NULL,
        display_name character varying(200) NOT NULL,
        status smallint NOT NULL,
        role_ids uuid[] NOT NULL,
        version integer NOT NULL,
        CONSTRAINT "PK_app_user" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809024405_InitialFoundation') THEN
    CREATE TABLE foundation.company (
        id uuid NOT NULL,
        legal_name character varying(200) NOT NULL,
        tax_id character varying(30) NOT NULL,
        status smallint NOT NULL,
        version integer NOT NULL,
        CONSTRAINT "PK_company" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809024405_InitialFoundation') THEN
    CREATE TABLE foundation.outbox (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        type character varying(200) NOT NULL,
        payload jsonb NOT NULL,
        aggregate_type character varying(100),
        aggregate_id uuid,
        correlation_id uuid,
        occurred_at timestamp with time zone NOT NULL,
        published_at timestamp with time zone,
        retry_count integer NOT NULL,
        last_error text,
        CONSTRAINT "PK_outbox" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809024405_InitialFoundation') THEN
    CREATE TABLE foundation.role (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        name character varying(100) NOT NULL,
        permissions text[] NOT NULL,
        version integer NOT NULL,
        CONSTRAINT "PK_role" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809024405_InitialFoundation') THEN
    CREATE UNIQUE INDEX "IX_app_user_company_id_email" ON foundation.app_user (company_id, email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809024405_InitialFoundation') THEN
    CREATE UNIQUE INDEX "IX_app_user_company_id_subject" ON foundation.app_user (company_id, subject);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809024405_InitialFoundation') THEN
    CREATE UNIQUE INDEX "IX_company_tax_id" ON foundation.company (tax_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809024405_InitialFoundation') THEN
    CREATE INDEX "IX_outbox_published_at" ON foundation.outbox (published_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809024405_InitialFoundation') THEN
    CREATE UNIQUE INDEX "IX_role_company_id_name" ON foundation.role (company_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809024405_InitialFoundation') THEN
    CREATE INDEX IF NOT EXISTS idx_outbox_pending ON foundation.outbox (occurred_at) WHERE published_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809024405_InitialFoundation') THEN
    CREATE INDEX IF NOT EXISTS idx_outbox_company ON foundation.outbox (company_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809024405_InitialFoundation') THEN

    CREATE OR REPLACE FUNCTION foundation.current_company() RETURNS uuid
    LANGUAGE sql STABLE AS $$
        SELECT NULLIF(current_setting('app.current_company', true), '')::uuid
    $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809024405_InitialFoundation') THEN

    ALTER TABLE foundation.role     ENABLE ROW LEVEL SECURITY;
    ALTER TABLE foundation.role     FORCE  ROW LEVEL SECURITY;
    ALTER TABLE foundation.app_user ENABLE ROW LEVEL SECURITY;
    ALTER TABLE foundation.app_user FORCE  ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809024405_InitialFoundation') THEN

    CREATE POLICY tenant_isolation ON foundation.role
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809024405_InitialFoundation') THEN

    CREATE POLICY tenant_isolation ON foundation.app_user
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809024405_InitialFoundation') THEN
    INSERT INTO foundation.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260809024405_InitialFoundation', '9.0.0');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809101216_AuditTrail') THEN
    CREATE TABLE foundation.audit_entry (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        occurred_at timestamp with time zone NOT NULL,
        actor_subject character varying(200) NOT NULL,
        action character varying(100) NOT NULL,
        target_type character varying(100),
        target_id character varying(100),
        metadata jsonb,
        CONSTRAINT "PK_audit_entry" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809101216_AuditTrail') THEN
    CREATE INDEX "IX_audit_entry_company_id_occurred_at" ON foundation.audit_entry (company_id, occurred_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809101216_AuditTrail') THEN

    ALTER TABLE foundation.audit_entry ENABLE ROW LEVEL SECURITY;
    ALTER TABLE foundation.audit_entry FORCE  ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809101216_AuditTrail') THEN

    CREATE POLICY tenant_isolation ON foundation.audit_entry
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809101216_AuditTrail') THEN

    CREATE OR REPLACE FUNCTION foundation.audit_no_mutate() RETURNS trigger
    LANGUAGE plpgsql AS $$
    BEGIN
        RAISE EXCEPTION 'foundation.audit_entry é append-only (SEC-002): % não é permitido', TG_OP;
    END;
    $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809101216_AuditTrail') THEN

    CREATE TRIGGER trg_audit_no_mutate
        BEFORE UPDATE OR DELETE ON foundation.audit_entry
        FOR EACH ROW EXECUTE FUNCTION foundation.audit_no_mutate();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809101216_AuditTrail') THEN
    INSERT INTO foundation.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260809101216_AuditTrail', '9.0.0');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809102400_AuthCredentials') THEN
    ALTER TABLE foundation.app_user ADD password_hash character varying(300);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809102400_AuthCredentials') THEN
    CREATE TABLE foundation.refresh_token (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        user_id uuid NOT NULL,
        token_hash character varying(100) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        revoked_at timestamp with time zone,
        CONSTRAINT "PK_refresh_token" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809102400_AuthCredentials') THEN
    CREATE UNIQUE INDEX "IX_refresh_token_token_hash" ON foundation.refresh_token (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809102400_AuthCredentials') THEN

    ALTER TABLE foundation.refresh_token ENABLE ROW LEVEL SECURITY;
    ALTER TABLE foundation.refresh_token FORCE  ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809102400_AuthCredentials') THEN

    CREATE POLICY tenant_isolation ON foundation.refresh_token
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM foundation.__ef_migrations WHERE "MigrationId" = '20260809102400_AuthCredentials') THEN
    INSERT INTO foundation.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260809102400_AuthCredentials', '9.0.0');
    END IF;
END $EF$;
COMMIT;

