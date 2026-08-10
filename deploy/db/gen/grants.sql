-- Provisionamento da role de aplicação (SEC-004). Idempotente. Roda APÓS as migrations.
-- A aplicação conecta como `trino_app` (não-superuser, sem BYPASSRLS) → RLS efetivo.
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'trino_app') THEN
        CREATE ROLE trino_app LOGIN PASSWORD 'apppw' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
    END IF;
END $$;

GRANT USAGE ON SCHEMA foundation, materials, procurement TO trino_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA foundation, materials, procurement TO trino_app;
-- Auditoria é append-only: a role da app não recebe UPDATE/DELETE (SEC-002).
REVOKE UPDATE, DELETE ON foundation.audit_entry FROM trino_app;
GRANT EXECUTE ON FUNCTION foundation.current_company() TO trino_app;
