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

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.supplier ADD address character varying(200) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.supplier ADD city character varying(120) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.supplier ADD district character varying(120) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.supplier ADD email character varying(200) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.supplier ADD payment_method character varying(80) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.supplier ADD payment_terms character varying(80) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.supplier ADD phone character varying(40) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.supplier ADD state character varying(2) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.supplier ADD state_registration character varying(30) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.supplier ADD zip_code character varying(12) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.purchase_order ADD discount_value numeric(18,2) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.purchase_order ADD freight_terms character varying(80) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.purchase_order ADD icms_value numeric(18,2) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.purchase_order ADD ipi_value numeric(18,2) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.purchase_order ADD number bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.purchase_order ADD other_expenses numeric(18,2) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.purchase_order ADD paying_company_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.purchase_order ADD payment_method character varying(80) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.purchase_order ADD payment_terms character varying(80) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.order_line ADD delivery_date timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.order_line ADD description character varying(300) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.order_line ADD irrf_percent numeric(9,4) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.order_line ADD iss_percent numeric(9,4) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    ALTER TABLE procurement.order_line ADD unit_price numeric(18,4) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    CREATE TABLE procurement.paying_company (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        code character varying(60) NOT NULL,
        legal_name character varying(200) NOT NULL,
        tax_id character varying(30) NOT NULL,
        state_registration character varying(30) NOT NULL,
        address character varying(200) NOT NULL,
        district character varying(120) NOT NULL,
        city character varying(120) NOT NULL,
        state character varying(2) NOT NULL,
        zip_code character varying(12) NOT NULL,
        phone character varying(40) NOT NULL,
        email character varying(200) NOT NULL,
        status smallint NOT NULL,
        version integer NOT NULL,
        CONSTRAINT "PK_paying_company" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    CREATE UNIQUE INDEX "IX_purchase_order_company_id_number" ON procurement.purchase_order (company_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    CREATE UNIQUE INDEX "IX_paying_company_company_id_code" ON procurement.paying_company (company_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN

    ALTER TABLE procurement.paying_company ENABLE ROW LEVEL SECURITY;
    ALTER TABLE procurement.paying_company FORCE  ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN

    CREATE POLICY tenant_isolation ON procurement.paying_company
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810022704_OcPayingCompanyAndPricing') THEN
    INSERT INTO procurement.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260810022704_OcPayingCompanyAndPricing', '9.0.0');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810121405_OcCancellation') THEN
    DROP INDEX procurement."IX_purchase_order_company_id_requisition_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810121405_OcCancellation') THEN
    ALTER TABLE procurement.purchase_order ADD cancel_reason character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810121405_OcCancellation') THEN
    ALTER TABLE procurement.purchase_order ADD cancelled_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810121405_OcCancellation') THEN
    ALTER TABLE procurement.purchase_order ADD cancelled_by_subject character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810121405_OcCancellation') THEN
    CREATE UNIQUE INDEX "IX_purchase_order_company_id_requisition_id" ON procurement.purchase_order (company_id, requisition_id) WHERE status = 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810121405_OcCancellation') THEN
    INSERT INTO procurement.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260810121405_OcCancellation', '9.0.0');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810143702_SupplierStatsProjection') THEN
    CREATE TABLE procurement.processed_event (
        event_id uuid NOT NULL,
        processed_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_processed_event" PRIMARY KEY (event_id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810143702_SupplierStatsProjection') THEN
    CREATE TABLE procurement.supplier_stats (
        company_id uuid NOT NULL,
        supplier_id uuid NOT NULL,
        orders_count integer NOT NULL,
        total_value numeric(18,2) NOT NULL,
        last_order_at timestamp with time zone,
        CONSTRAINT "PK_supplier_stats" PRIMARY KEY (company_id, supplier_id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810143702_SupplierStatsProjection') THEN

    ALTER TABLE procurement.supplier_stats ENABLE ROW LEVEL SECURITY;
    ALTER TABLE procurement.supplier_stats FORCE  ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810143702_SupplierStatsProjection') THEN

    CREATE POLICY tenant_isolation ON procurement.supplier_stats
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260810143702_SupplierStatsProjection') THEN
    INSERT INTO procurement.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260810143702_SupplierStatsProjection', '9.0.0');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260811013526_RequisitionHeaderAndTwoLevel') THEN
    ALTER TABLE procurement.purchase_requisition RENAME COLUMN decided_by_subject TO rejected_by;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260811013526_RequisitionHeaderAndTwoLevel') THEN
    ALTER TABLE procurement.purchase_requisition RENAME COLUMN decided_at TO rejected_at;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260811013526_RequisitionHeaderAndTwoLevel') THEN
    ALTER TABLE procurement.purchase_requisition ADD approver_l1_subject character varying(200) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260811013526_RequisitionHeaderAndTwoLevel') THEN
    ALTER TABLE procurement.purchase_requisition ADD approver_l2_subject character varying(200) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260811013526_RequisitionHeaderAndTwoLevel') THEN
    ALTER TABLE procurement.purchase_requisition ADD cost_center_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260811013526_RequisitionHeaderAndTwoLevel') THEN
    ALTER TABLE procurement.purchase_requisition ADD justification character varying(2000) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260811013526_RequisitionHeaderAndTwoLevel') THEN
    ALTER TABLE procurement.purchase_requisition ADD l1_decided_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260811013526_RequisitionHeaderAndTwoLevel') THEN
    ALTER TABLE procurement.purchase_requisition ADD l1_decided_by character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260811013526_RequisitionHeaderAndTwoLevel') THEN
    ALTER TABLE procurement.purchase_requisition ADD l2_decided_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260811013526_RequisitionHeaderAndTwoLevel') THEN
    ALTER TABLE procurement.purchase_requisition ADD l2_decided_by character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260811013526_RequisitionHeaderAndTwoLevel') THEN
    ALTER TABLE procurement.purchase_requisition ADD paying_company_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260811013526_RequisitionHeaderAndTwoLevel') THEN
    ALTER TABLE procurement.purchase_requisition ADD priority smallint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260811013526_RequisitionHeaderAndTwoLevel') THEN
    CREATE TABLE procurement.cost_center (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        code character varying(60) NOT NULL,
        name character varying(200) NOT NULL,
        status smallint NOT NULL,
        version integer NOT NULL,
        CONSTRAINT "PK_cost_center" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260811013526_RequisitionHeaderAndTwoLevel') THEN
    CREATE UNIQUE INDEX "IX_cost_center_company_id_code" ON procurement.cost_center (company_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260811013526_RequisitionHeaderAndTwoLevel') THEN

    ALTER TABLE procurement.cost_center ENABLE ROW LEVEL SECURITY;
    ALTER TABLE procurement.cost_center FORCE  ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260811013526_RequisitionHeaderAndTwoLevel') THEN

    CREATE POLICY tenant_isolation ON procurement.cost_center
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260811013526_RequisitionHeaderAndTwoLevel') THEN
    INSERT INTO procurement.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260811013526_RequisitionHeaderAndTwoLevel', '9.0.0');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260811113319_CostCenterPayingLink') THEN
    ALTER TABLE procurement.cost_center ADD paying_company_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260811113319_CostCenterPayingLink') THEN
    INSERT INTO procurement.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260811113319_CostCenterPayingLink', '9.0.0');
    END IF;
END $EF$;
COMMIT;

