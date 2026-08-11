DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'materials') THEN
        CREATE SCHEMA materials;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS materials.__ef_migrations (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809105805_InitialMaterials') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'materials') THEN
            CREATE SCHEMA materials;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809105805_InitialMaterials') THEN
    CREATE TABLE materials.item (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        code character varying(60) NOT NULL,
        name character varying(200) NOT NULL,
        base_unit_id uuid NOT NULL,
        status smallint NOT NULL,
        version integer NOT NULL,
        CONSTRAINT "PK_item" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809105805_InitialMaterials') THEN
    CREATE TABLE materials.unit_of_measure (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        code character varying(30) NOT NULL,
        name character varying(100) NOT NULL,
        dimension character varying(50) NOT NULL,
        factor_to_base numeric(18,6) NOT NULL,
        version integer NOT NULL,
        CONSTRAINT "PK_unit_of_measure" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809105805_InitialMaterials') THEN
    CREATE UNIQUE INDEX "IX_item_company_id_code" ON materials.item (company_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809105805_InitialMaterials') THEN
    CREATE UNIQUE INDEX "IX_unit_of_measure_company_id_code" ON materials.unit_of_measure (company_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809105805_InitialMaterials') THEN

    ALTER TABLE materials.item ENABLE ROW LEVEL SECURITY;
    ALTER TABLE materials.item FORCE  ROW LEVEL SECURITY;
    ALTER TABLE materials.unit_of_measure ENABLE ROW LEVEL SECURITY;
    ALTER TABLE materials.unit_of_measure FORCE  ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809105805_InitialMaterials') THEN

    CREATE POLICY tenant_isolation ON materials.item
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809105805_InitialMaterials') THEN

    CREATE POLICY tenant_isolation ON materials.unit_of_measure
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809105805_InitialMaterials') THEN
    INSERT INTO materials.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260809105805_InitialMaterials', '9.0.0');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809113052_StockLedger') THEN
    CREATE TABLE materials.stock_balance (
        item_id uuid NOT NULL,
        company_id uuid NOT NULL,
        quantity numeric(18,6) NOT NULL,
        version integer NOT NULL,
        CONSTRAINT "PK_stock_balance" PRIMARY KEY (item_id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809113052_StockLedger') THEN
    CREATE TABLE materials.stock_movement (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        item_id uuid NOT NULL,
        direction smallint NOT NULL,
        quantity numeric(18,6) NOT NULL,
        occurred_at timestamp with time zone NOT NULL,
        reason character varying(300),
        CONSTRAINT "PK_stock_movement" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809113052_StockLedger') THEN
    CREATE INDEX "IX_stock_balance_company_id" ON materials.stock_balance (company_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809113052_StockLedger') THEN
    CREATE INDEX "IX_stock_movement_company_id_item_id_occurred_at" ON materials.stock_movement (company_id, item_id, occurred_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809113052_StockLedger') THEN

    ALTER TABLE materials.stock_balance  ENABLE ROW LEVEL SECURITY;
    ALTER TABLE materials.stock_balance  FORCE  ROW LEVEL SECURITY;
    ALTER TABLE materials.stock_movement ENABLE ROW LEVEL SECURITY;
    ALTER TABLE materials.stock_movement FORCE  ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809113052_StockLedger') THEN

    CREATE POLICY tenant_isolation ON materials.stock_balance
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809113052_StockLedger') THEN

    CREATE POLICY tenant_isolation ON materials.stock_movement
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809113052_StockLedger') THEN
    INSERT INTO materials.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260809113052_StockLedger', '9.0.0');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809130854_ReplenishmentPolicy') THEN
    CREATE TABLE materials.replenishment_policy (
        item_id uuid NOT NULL,
        company_id uuid NOT NULL,
        min_level numeric(18,6) NOT NULL,
        max_level numeric(18,6) NOT NULL,
        active boolean NOT NULL,
        version integer NOT NULL,
        CONSTRAINT "PK_replenishment_policy" PRIMARY KEY (item_id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809130854_ReplenishmentPolicy') THEN
    CREATE INDEX "IX_replenishment_policy_company_id" ON materials.replenishment_policy (company_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809130854_ReplenishmentPolicy') THEN

    ALTER TABLE materials.replenishment_policy ENABLE ROW LEVEL SECURITY;
    ALTER TABLE materials.replenishment_policy FORCE  ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809130854_ReplenishmentPolicy') THEN

    CREATE POLICY tenant_isolation ON materials.replenishment_policy
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260809130854_ReplenishmentPolicy') THEN
    INSERT INTO materials.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260809130854_ReplenishmentPolicy', '9.0.0');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811012012_ItemProductGroup') THEN
    ALTER TABLE materials.item ADD product_group character varying(60) NOT NULL DEFAULT 'Sem grupo';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811012012_ItemProductGroup') THEN
    CREATE INDEX "IX_item_company_id_product_group" ON materials.item (company_id, product_group);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811012012_ItemProductGroup') THEN
    INSERT INTO materials.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260811012012_ItemProductGroup', '9.0.0');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811022344_AlmoxarifadoConsumption') THEN
    ALTER TABLE materials.item ADD ca character varying(60);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811022344_AlmoxarifadoConsumption') THEN
    CREATE TABLE materials.collaborator (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        name character varying(200) NOT NULL,
        registration character varying(60),
        cost_center_code character varying(60),
        company_code character varying(60),
        admission_date date,
        status smallint NOT NULL,
        version integer NOT NULL,
        CONSTRAINT "PK_collaborator" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811022344_AlmoxarifadoConsumption') THEN
    CREATE TABLE materials.consumption (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        company_code character varying(60) NOT NULL,
        cost_center_code character varying(60) NOT NULL,
        collaborator_id uuid NOT NULL,
        reason character varying(200) NOT NULL,
        issued_by character varying(200) NOT NULL,
        issued_at timestamp with time zone NOT NULL,
        version integer NOT NULL,
        CONSTRAINT "PK_consumption" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811022344_AlmoxarifadoConsumption') THEN
    CREATE TABLE materials.consumption_line (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        consumption_id uuid NOT NULL,
        item_code character varying(60) NOT NULL,
        quantity numeric(18,6) NOT NULL,
        CONSTRAINT "PK_consumption_line" PRIMARY KEY (id),
        CONSTRAINT "FK_consumption_line_consumption_consumption_id" FOREIGN KEY (consumption_id) REFERENCES materials.consumption (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811022344_AlmoxarifadoConsumption') THEN
    CREATE INDEX "IX_collaborator_company_id_name" ON materials.collaborator (company_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811022344_AlmoxarifadoConsumption') THEN
    CREATE INDEX "IX_consumption_company_id_collaborator_id" ON materials.consumption (company_id, collaborator_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811022344_AlmoxarifadoConsumption') THEN
    CREATE INDEX "IX_consumption_line_company_id_consumption_id" ON materials.consumption_line (company_id, consumption_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811022344_AlmoxarifadoConsumption') THEN
    CREATE INDEX "IX_consumption_line_consumption_id" ON materials.consumption_line (consumption_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811022344_AlmoxarifadoConsumption') THEN

    ALTER TABLE materials.collaborator     ENABLE ROW LEVEL SECURITY;
    ALTER TABLE materials.collaborator     FORCE  ROW LEVEL SECURITY;
    ALTER TABLE materials.consumption      ENABLE ROW LEVEL SECURITY;
    ALTER TABLE materials.consumption      FORCE  ROW LEVEL SECURITY;
    ALTER TABLE materials.consumption_line ENABLE ROW LEVEL SECURITY;
    ALTER TABLE materials.consumption_line FORCE  ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811022344_AlmoxarifadoConsumption') THEN

    CREATE POLICY tenant_isolation ON materials.collaborator
        USING (company_id = foundation.current_company()) WITH CHECK (company_id = foundation.current_company());
    CREATE POLICY tenant_isolation ON materials.consumption
        USING (company_id = foundation.current_company()) WITH CHECK (company_id = foundation.current_company());
    CREATE POLICY tenant_isolation ON materials.consumption_line
        USING (company_id = foundation.current_company()) WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811022344_AlmoxarifadoConsumption') THEN
    INSERT INTO materials.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260811022344_AlmoxarifadoConsumption', '9.0.0');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811024045_StockRequestFlow') THEN
    CREATE TABLE materials.stock_request (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        requester_subject character varying(200) NOT NULL,
        company_code character varying(60) NOT NULL,
        cost_center_code character varying(60) NOT NULL,
        manager_subject character varying(200) NOT NULL,
        reason character varying(200) NOT NULL,
        status smallint NOT NULL,
        created_at timestamp with time zone NOT NULL,
        decision_by character varying(200),
        decision_at timestamp with time zone,
        decision_note character varying(1000),
        version integer NOT NULL,
        CONSTRAINT "PK_stock_request" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811024045_StockRequestFlow') THEN
    CREATE TABLE materials.stock_request_line (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        request_id uuid NOT NULL,
        item_code character varying(60) NOT NULL,
        quantity numeric(18,6) NOT NULL,
        CONSTRAINT "PK_stock_request_line" PRIMARY KEY (id),
        CONSTRAINT "FK_stock_request_line_stock_request_request_id" FOREIGN KEY (request_id) REFERENCES materials.stock_request (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811024045_StockRequestFlow') THEN
    CREATE INDEX "IX_stock_request_company_id_requester_subject" ON materials.stock_request (company_id, requester_subject);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811024045_StockRequestFlow') THEN
    CREATE INDEX "IX_stock_request_company_id_status" ON materials.stock_request (company_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811024045_StockRequestFlow') THEN
    CREATE INDEX "IX_stock_request_line_company_id_request_id" ON materials.stock_request_line (company_id, request_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811024045_StockRequestFlow') THEN
    CREATE INDEX "IX_stock_request_line_request_id" ON materials.stock_request_line (request_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811024045_StockRequestFlow') THEN

    ALTER TABLE materials.stock_request      ENABLE ROW LEVEL SECURITY;
    ALTER TABLE materials.stock_request      FORCE  ROW LEVEL SECURITY;
    ALTER TABLE materials.stock_request_line ENABLE ROW LEVEL SECURITY;
    ALTER TABLE materials.stock_request_line FORCE  ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811024045_StockRequestFlow') THEN

    CREATE POLICY tenant_isolation ON materials.stock_request
        USING (company_id = foundation.current_company()) WITH CHECK (company_id = foundation.current_company());
    CREATE POLICY tenant_isolation ON materials.stock_request_line
        USING (company_id = foundation.current_company()) WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM materials.__ef_migrations WHERE "MigrationId" = '20260811024045_StockRequestFlow') THEN
    INSERT INTO materials.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260811024045_StockRequestFlow', '9.0.0');
    END IF;
END $EF$;
COMMIT;

