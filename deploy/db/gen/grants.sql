-- Provisionamento da role de aplicação (SEC-004). Idempotente. Roda APÓS as migrations.
-- A aplicação conecta como `trino_app` (não-superuser, sem BYPASSRLS) → RLS efetivo.
-- Senha: vem do setting de sessão `trino.app_password` (definido pelo bootstrap a partir de
-- APP_DB_PASSWORD); sem o setting, usa o padrão de PILOTO 'apppw' — troque em produção.
DO $$
DECLARE pw text := coalesce(nullif(current_setting('trino.app_password', true), ''), 'apppw');
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'trino_app') THEN
        EXECUTE format('CREATE ROLE trino_app LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS', pw);
    ELSE
        -- Idempotente + rotação: reaplicar o bootstrap com APP_DB_PASSWORD novo troca a senha.
        EXECUTE format('ALTER ROLE trino_app PASSWORD %L', pw);
    END IF;
END $$;

GRANT USAGE ON SCHEMA foundation, materials, procurement TO trino_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA foundation, materials, procurement TO trino_app;
-- Auditoria é append-only: a role da app não recebe UPDATE/DELETE (SEC-002).
REVOKE UPDATE, DELETE ON foundation.audit_entry FROM trino_app;
GRANT EXECUTE ON FUNCTION foundation.current_company() TO trino_app;
