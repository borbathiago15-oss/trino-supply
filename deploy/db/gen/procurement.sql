DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'procurement') THEN
        CREATE SCHEMA procurement;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS procurement.__ef_migrations (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809135701_InitialProcurement') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'procurement') THEN
            CREATE SCHEMA procurement;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809135701_InitialProcurement') THEN
    CREATE TABLE procurement.purchase_requisition (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        requester_subject character varying(200) NOT NULL,
        status smallint NOT NULL,
        created_at timestamp with time zone NOT NULL,
        decided_by_subject character varying(200),
        decided_at timestamp with time zone,
        decision_note character varying(500),
        version integer NOT NULL,
        CONSTRAINT "PK_purchase_requisition" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809135701_InitialProcurement') THEN
    CREATE TABLE procurement.requisition_line (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        requisition_id uuid NOT NULL,
        item_code character varying(60) NOT NULL,
        quantity numeric(18,6) NOT NULL,
        unit character varying(30) NOT NULL,
        CONSTRAINT "PK_requisition_line" PRIMARY KEY (id),
        CONSTRAINT "FK_requisition_line_purchase_requisition_requisition_id" FOREIGN KEY (requisition_id) REFERENCES procurement.purchase_requisition (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809135701_InitialProcurement') THEN
    CREATE INDEX "IX_purchase_requisition_company_id_status" ON procurement.purchase_requisition (company_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809135701_InitialProcurement') THEN
    CREATE INDEX "IX_requisition_line_requisition_id" ON procurement.requisition_line (requisition_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809135701_InitialProcurement') THEN

    ALTER TABLE procurement.purchase_requisition ENABLE ROW LEVEL SECURITY;
    ALTER TABLE procurement.purchase_requisition FORCE  ROW LEVEL SECURITY;
    ALTER TABLE procurement.requisition_line     ENABLE ROW LEVEL SECURITY;
    ALTER TABLE procurement.requisition_line     FORCE  ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809135701_InitialProcurement') THEN

    CREATE POLICY tenant_isolation ON procurement.purchase_requisition
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809135701_InitialProcurement') THEN

    CREATE POLICY tenant_isolation ON procurement.requisition_line
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809135701_InitialProcurement') THEN
    INSERT INTO procurement.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260809135701_InitialProcurement', '9.0.0');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809192236_SupplierAndOrder') THEN
    CREATE TABLE procurement.purchase_order (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        requisition_id uuid NOT NULL,
        supplier_id uuid NOT NULL,
        issued_by_subject character varying(200) NOT NULL,
        issued_at timestamp with time zone NOT NULL,
        status smallint NOT NULL,
        version integer NOT NULL,
        CONSTRAINT "PK_purchase_order" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809192236_SupplierAndOrder') THEN
    CREATE TABLE procurement.supplier (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        code character varying(60) NOT NULL,
        name character varying(200) NOT NULL,
        tax_id character varying(30) NOT NULL,
        status smallint NOT NULL,
        version integer NOT NULL,
        CONSTRAINT "PK_supplier" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809192236_SupplierAndOrder') THEN
    CREATE TABLE procurement.order_line (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        order_id uuid NOT NULL,
        item_code character varying(60) NOT NULL,
        quantity numeric(18,6) NOT NULL,
        unit character varying(30) NOT NULL,
        CONSTRAINT "PK_order_line" PRIMARY KEY (id),
        CONSTRAINT "FK_order_line_purchase_order_order_id" FOREIGN KEY (order_id) REFERENCES procurement.purchase_order (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809192236_SupplierAndOrder') THEN
    CREATE INDEX "IX_order_line_order_id" ON procurement.order_line (order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809192236_SupplierAndOrder') THEN
    CREATE UNIQUE INDEX "IX_purchase_order_company_id_requisition_id" ON procurement.purchase_order (company_id, requisition_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809192236_SupplierAndOrder') THEN
    CREATE UNIQUE INDEX "IX_supplier_company_id_code" ON procurement.supplier (company_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809192236_SupplierAndOrder') THEN

    ALTER TABLE procurement.supplier       ENABLE ROW LEVEL SECURITY;
    ALTER TABLE procurement.supplier       FORCE  ROW LEVEL SECURITY;
    ALTER TABLE procurement.purchase_order ENABLE ROW LEVEL SECURITY;
    ALTER TABLE procurement.purchase_order FORCE  ROW LEVEL SECURITY;
    ALTER TABLE procurement.order_line     ENABLE ROW LEVEL SECURITY;
    ALTER TABLE procurement.order_line     FORCE  ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809192236_SupplierAndOrder') THEN

    CREATE POLICY tenant_isolation ON procurement.supplier
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809192236_SupplierAndOrder') THEN

    CREATE POLICY tenant_isolation ON procurement.purchase_order
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809192236_SupplierAndOrder') THEN

    CREATE POLICY tenant_isolation ON procurement.order_line
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260809192236_SupplierAndOrder') THEN
    INSERT INTO procurement.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260809192236_SupplierAndOrder', '9.0.0');
    END IF;
END $EF$;
COMMIT;

