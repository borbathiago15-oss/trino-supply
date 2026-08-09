-- Row Level Security (RLS) — isolamento multi-tenant no PostgreSQL (defesa em profundidade).
-- ADR-015 §3, ARC-006 §12.6, SEC-001, SEC-004.
--
-- Modelo de enforcement (SEC-004):
--   1. A aplicação conecta com uma role SEM privilégios de bypass (ver "Hardening da role" abaixo).
--   2. A cada conexão, o TenantConnectionInterceptor executa:
--          SELECT set_config('app.current_company', '<uuid-do-JWT>', false);
--      (string vazia quando não há tenant → NULL → nega tudo: fail-closed).
--   3. As policies restringem toda linha a company_id = foundation.current_company().
--
-- RLS NÃO substitui a autorização server-side (SEC-001); é a última linha de defesa caso um
-- filtro `company_id` da aplicação seja esquecido.

CREATE SCHEMA IF NOT EXISTS foundation;

-- Função utilitária: company atual da sessão (NULL se não definida → policies negam).
CREATE OR REPLACE FUNCTION foundation.current_company() RETURNS uuid
LANGUAGE sql STABLE AS $$
    SELECT NULLIF(current_setting('app.current_company', true), '')::uuid
$$;

-- ---------------------------------------------------------------------------
-- Template de policy — aplicar a CADA tabela de negócio que possua company_id.
-- (Exemplo com materials.item; replicar trocando schema.tabela.)
-- ---------------------------------------------------------------------------
--
--   ALTER TABLE materials.item ENABLE ROW LEVEL SECURITY;
--   ALTER TABLE materials.item FORCE  ROW LEVEL SECURITY;   -- vale inclusive p/ o dono da tabela
--
--   CREATE POLICY tenant_isolation ON materials.item
--       USING      (company_id = foundation.current_company())
--       WITH CHECK (company_id = foundation.current_company());
--
-- Efeito:
--   * SELECT/UPDATE/DELETE só enxergam/afetam linhas do tenant corrente;
--   * INSERT exige company_id = tenant corrente (WITH CHECK) — impede gravar em outro tenant;
--   * recurso fora do escopo → 0 linhas (a aplicação traduz para 404, anti-enumeração).
--
-- FORCE ROW LEVEL SECURITY é obrigatório: sem ele, o dono da tabela (owner) ignora as policies.

-- ---------------------------------------------------------------------------
-- Hardening da role de aplicação (SEC-004) — executar no provisionamento do banco.
-- ---------------------------------------------------------------------------
--
--   CREATE ROLE trino_app LOGIN PASSWORD '<secret>'
--       NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
--   -- A aplicação conecta SOMENTE com esta role. NUNCA com superuser/owner:
--   --   * SUPERUSER e BYPASSRLS ignoram TODAS as policies → quebram o isolamento;
--   --   * o owner da tabela também ignora policies, exceto sob FORCE ROW LEVEL SECURITY.
--   GRANT USAGE ON SCHEMA foundation, materials TO trino_app;
--   GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA materials TO trino_app;
--
-- Serviços de sistema (publisher do Outbox, serviço de saldo) que legitimamente cruzam tenants
-- NÃO devem usar BYPASSRLS. Preferir:
--   * uma role dedicada com GRANT explícito apenas nas tabelas de infraestrutura (ex.: outbox), ou
--   * definir app.current_company por operação quando o escopo do tenant for conhecido.
-- Qualquer uso de BYPASSRLS deve ser exceção auditada e justificada por ADR.
--
-- Notas operacionais:
--   * set_config(..., is_local := false) define no nível de sessão; o Npgsql reseta o estado ao
--     devolver a conexão ao pool, e o interceptor reaplica a cada abertura (sem vazamento entre tenants);
--   * foundation.outbox e foundation.company NÃO recebem RLS (infra/catálogo de tenants — ver
--     deploy/db/001_foundation_init.sql e SEC-004); seu isolamento é por privilégio de role.
