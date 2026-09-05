-- Verificação de integridade do banco do Trino Supply.
--
-- Só leitura: nenhum INSERT, UPDATE, DELETE ou DDL. Pode rodar em produção.
--
--   psql "$DATABASE_URL" -f scripts/verificar-banco.sql
--
-- Cada linha é uma checagem. Contagem zero é o esperado; qualquer número
-- diferente de zero é uma inconsistência para investigar.
--
-- Por que estas checagens existem: as tabelas moram em três schemas
-- (foundation, materials, procurement) e as referências entre schemas são
-- Guid sem FOREIGN KEY, para manter os módulos desacoplados. O preço dessa
-- escolha é que o banco não impede um órfão sozinho — quem verifica é isto.

\pset border 2

SELECT categoria, checagem, ocorrencias FROM (

  -- ---- referências órfãs entre schemas ------------------------------------
  SELECT 1 AS ord, 'referência' AS categoria,
         'item de SC apontando para produto inexistente' AS checagem,
         count(*) AS ocorrencias
    FROM procurement.purchase_requisition_item i
   WHERE i.catalog_item_id IS NOT NULL
     AND NOT EXISTS (SELECT 1 FROM materials.catalog_item c WHERE c.id = i.catalog_item_id)

  UNION ALL SELECT 2, 'referência', 'item de cotação sem a SC de origem', count(*)
    FROM procurement.quotation_item q
   WHERE q.source_pr_id IS NOT NULL
     AND NOT EXISTS (SELECT 1 FROM procurement.purchase_requisition r WHERE r.id = q.source_pr_id)

  UNION ALL SELECT 3, 'referência', 'cotação com vencedor sem proposta correspondente', count(*)
    FROM procurement.quotation q
   WHERE q.winner_proposal_id IS NOT NULL
     AND NOT EXISTS (SELECT 1 FROM procurement.proposal p WHERE p.id = q.winner_proposal_id)

  UNION ALL SELECT 4, 'referência', 'adjudicação apontando para proposta inexistente', count(*)
    FROM procurement.quotation_award a
   WHERE NOT EXISTS (SELECT 1 FROM procurement.proposal p WHERE p.id = a.proposal_id)

  UNION ALL SELECT 5, 'referência', 'O.C. apontando para cotação inexistente', count(*)
    FROM procurement.purchase_order o
   WHERE o.quotation_id IS NOT NULL
     AND NOT EXISTS (SELECT 1 FROM procurement.quotation q WHERE q.id = o.quotation_id)

  UNION ALL SELECT 6, 'referência', 'fornecedor do produto sem cadastro de fornecedor', count(*)
    FROM materials.catalog_item_supplier s
   WHERE NOT EXISTS (SELECT 1 FROM procurement.supplier f WHERE f.id = s.supplier_id)

  UNION ALL SELECT 7, 'referência', 'aprovador de centro de custo sem usuário', count(*)
    FROM foundation.cost_center_approver a
   WHERE NOT EXISTS (SELECT 1 FROM foundation.app_user u WHERE u.id = a.user_id)

  UNION ALL SELECT 8, 'referência', 'centro de custo com gerente inexistente', count(*)
    FROM foundation.cost_center c
   WHERE c.manager_user_id IS NOT NULL
     AND NOT EXISTS (SELECT 1 FROM foundation.app_user u WHERE u.id = c.manager_user_id)

  UNION ALL SELECT 9, 'referência', 'saldo de estoque sem local cadastrado', count(*)
    FROM materials.stock_balance b
   WHERE NOT EXISTS (SELECT 1 FROM materials.storage_location l WHERE l.id = b.location_id)

  UNION ALL SELECT 10, 'referência', 'movimento de estoque sem produto', count(*)
    FROM materials.stock_movement m
   WHERE NOT EXISTS (SELECT 1 FROM materials.catalog_item c WHERE c.id = m.catalog_item_id)

  -- ---- regras de negócio --------------------------------------------------
  UNION ALL SELECT 20, 'regra', 'RFQ-ERR-030: aprovada por quem selecionou o fornecedor', count(*)
    FROM procurement.quotation q
   WHERE q.selected_by IS NOT NULL
     AND (q.manager_approved_by = q.selected_by OR q.director_approved_by = q.selected_by)

  UNION ALL SELECT 21, 'regra', 'RFQ-ERR-030: Nível 2 dado por quem deu o Nível 1', count(*)
    FROM procurement.quotation q
   WHERE q.director_approved_by IS NOT NULL
     AND q.director_approved_by = q.manager_approved_by

  UNION ALL SELECT 22, 'regra', 'RFQ-ERR-040: O.C. registrada sem as duas aprovações', count(*)
    FROM procurement.purchase_order o
    JOIN procurement.quotation q ON q.id = o.quotation_id
   WHERE o.erp_number IS NOT NULL
     AND (q.manager_approved_at IS NULL OR q.director_approved_at IS NULL)

  UNION ALL SELECT 23, 'regra', 'RFQ-ERR-041: número de O.C. do ERP repetido', count(*)
    FROM (SELECT erp_number FROM procurement.purchase_order
           WHERE erp_number IS NOT NULL AND erp_number <> ''
           GROUP BY 1 HAVING count(*) > 1) d

  UNION ALL SELECT 25, 'regra', 'PO-BR-011: pedido faturado sem O.C. do ERP e sem observação', count(*)
    FROM procurement.purchase_order o
   WHERE o.erp_number IS NULL
     AND (o.no_erp_reason IS NULL OR btrim(o.no_erp_reason) = '')
     AND EXISTS (SELECT 1 FROM procurement.purchase_order_invoice i WHERE i.order_id = o.id)

  UNION ALL SELECT 24, 'regra', 'IC-ERR-023: EPI/EPC ativo sem C.A. em nenhum fornecedor', count(*)
    FROM materials.catalog_item c
   WHERE c.active AND c.product_type IN ('EPI', 'EPC')
     AND NOT EXISTS (SELECT 1 FROM materials.catalog_item_supplier s
                      WHERE s.catalog_item_id = c.id
                        AND s.ca_number IS NOT NULL AND s.ca_number <> '')

  -- ---- duplicidades que o banco não impede --------------------------------
  UNION ALL SELECT 30, 'duplicidade', 'mesmo fornecedor repetido no mesmo produto', count(*)
    FROM (SELECT catalog_item_id, supplier_id FROM materials.catalog_item_supplier
           GROUP BY 1, 2 HAVING count(*) > 1) d

  UNION ALL SELECT 31, 'duplicidade', 'CNPJ de fornecedor repetido entre ativos', count(*)
    FROM (SELECT tax_id FROM procurement.supplier
           WHERE active AND tax_id IS NOT NULL AND tax_id <> ''
           GROUP BY 1 HAVING count(*) > 1) d

  -- ---- estados presos ------------------------------------------------------
  UNION ALL SELECT 40, 'estado', 'cotação aprovada para emissão há mais de 30 dias sem O.C.', count(*)
    FROM procurement.quotation q
   WHERE q.status = 5   -- ApprovedForIssue
     AND q.director_approved_at < now() - interval '30 days'
     AND NOT EXISTS (SELECT 1 FROM procurement.purchase_order o
                      WHERE o.quotation_id = q.id AND o.erp_number IS NOT NULL)

  UNION ALL SELECT 41, 'estado', 'refresh token vencido há mais de 30 dias ainda guardado', count(*)
    FROM foundation.refresh_token t
   WHERE t.expires_at < now() - interval '30 days'

  UNION ALL SELECT 42, 'estado', 'SEC-004: usuário ativo há mais de 30 dias ainda com senha provisória', count(*)
    FROM foundation.app_user u
   WHERE u.active AND u.must_change_password AND u.created_at < now() - interval '30 days'

) AS checagens
ORDER BY ord;
