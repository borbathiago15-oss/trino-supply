-- Row Level Security (RLS) do Foundation — isolamento multi-tenant no PostgreSQL.
-- ADR-015 §3 (RLS complementar ao filtro company_id da aplicação), ARC-006 §12.6, SEC-001.
--
-- Padrão: cada conexão define o GUC `app.current_company` (do JWT, via ITenantContext);
-- as policies restringem toda linha a company_id = current_setting('app.current_company').
-- A aplicação executa, por transação:  SET LOCAL app.current_company = '<uuid>';

-- Exemplo aplicado à tabela de negócio (padrão replicável a todas as tabelas com company_id).
-- (A tabela foundation.company é o próprio tenant; RLS incide sobre as tabelas dependentes.)

CREATE SCHEMA IF NOT EXISTS foundation;

-- Função utilitária: company atual da sessão (NULL se não definida).
CREATE OR REPLACE FUNCTION foundation.current_company() RETURNS uuid
LANGUAGE sql STABLE AS $$
    SELECT NULLIF(current_setting('app.current_company', true), '')::uuid
$$;

-- Modelo de policy (aplicar a cada tabela <schema>.<tabela> que possua company_id):
--
--   ALTER TABLE materials.item ENABLE ROW LEVEL SECURITY;
--   ALTER TABLE materials.item FORCE ROW LEVEL SECURITY;
--
--   CREATE POLICY tenant_isolation ON materials.item
--       USING      (company_id = foundation.current_company())
--       WITH CHECK (company_id = foundation.current_company());
--
-- Efeito: SELECT/UPDATE/DELETE só enxergam/afetam linhas do tenant corrente;
-- INSERT exige company_id = tenant corrente. Recurso fora do escopo → 0 linhas (a app traduz p/ 404).
--
-- Observações:
--  * usar SET LOCAL dentro da transação (compatível com pool de conexões);
--  * o papel de migração/serviço de saldo pode ter BYPASSRLS quando estritamente necessário
--    (ex.: StockBalanceService — MMS-004-11 §8), sempre auditado;
--  * RLS é defesa em profundidade: NÃO substitui a autorização server-side (SEC-001).
