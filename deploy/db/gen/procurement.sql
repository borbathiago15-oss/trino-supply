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

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919161857_SplitPurchaseOrders') THEN
    DROP INDEX procurement."IX_purchase_order_company_id_requisition_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919161857_SplitPurchaseOrders') THEN
    ALTER TABLE procurement.requisition_line ADD purchase_order_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919161857_SplitPurchaseOrders') THEN
    CREATE INDEX "IX_requisition_line_purchase_order_id" ON procurement.requisition_line (purchase_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919161857_SplitPurchaseOrders') THEN
    CREATE INDEX "IX_purchase_order_company_id_requisition_id" ON procurement.purchase_order (company_id, requisition_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919161857_SplitPurchaseOrders') THEN

    UPDATE procurement.requisition_line rl
       SET purchase_order_id = po.id
      FROM procurement.purchase_order po
     WHERE po.requisition_id = rl.requisition_id
       AND po.company_id    = rl.company_id
       AND po.status        = 1            -- Issued (OC cancelada não prende a linha)
       AND rl.purchase_order_id IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919161857_SplitPurchaseOrders') THEN

    UPDATE procurement.purchase_requisition r
       SET status = 8
     WHERE r.status = 4
       AND EXISTS     (SELECT 1 FROM procurement.requisition_line l WHERE l.requisition_id = r.id)
       AND NOT EXISTS (SELECT 1 FROM procurement.requisition_line l
                        WHERE l.requisition_id = r.id AND l.purchase_order_id IS NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919161857_SplitPurchaseOrders') THEN
    INSERT INTO procurement.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260919161857_SplitPurchaseOrders', '9.0.0');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919172619_GoodsReceipt') THEN
    CREATE TABLE procurement.goods_receipt (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        purchase_order_id uuid NOT NULL,
        invoice_number character varying(60) NOT NULL,
        invoice_date date,
        received_by character varying(200) NOT NULL,
        received_at timestamp with time zone NOT NULL,
        notes character varying(1000),
        stock_posted boolean NOT NULL,
        stock_posted_at timestamp with time zone,
        version integer NOT NULL,
        CONSTRAINT "PK_goods_receipt" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919172619_GoodsReceipt') THEN
    CREATE TABLE procurement.goods_receipt_line (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        receipt_id uuid NOT NULL,
        order_line_id uuid NOT NULL,
        item_code character varying(60) NOT NULL,
        unit character varying(30) NOT NULL,
        quantity_ordered numeric(18,6) NOT NULL,
        quantity_received numeric(18,6) NOT NULL,
        quantity_damaged numeric(18,6) NOT NULL,
        occurrence smallint NOT NULL,
        occurrence_note character varying(1000),
        CONSTRAINT "PK_goods_receipt_line" PRIMARY KEY (id),
        CONSTRAINT "FK_goods_receipt_line_goods_receipt_receipt_id" FOREIGN KEY (receipt_id) REFERENCES procurement.goods_receipt (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919172619_GoodsReceipt') THEN
    CREATE INDEX "IX_goods_receipt_company_id_purchase_order_id" ON procurement.goods_receipt (company_id, purchase_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919172619_GoodsReceipt') THEN
    CREATE INDEX "IX_goods_receipt_line_order_line_id" ON procurement.goods_receipt_line (order_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919172619_GoodsReceipt') THEN
    CREATE INDEX "IX_goods_receipt_line_receipt_id" ON procurement.goods_receipt_line (receipt_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919172619_GoodsReceipt') THEN

    ALTER TABLE procurement.goods_receipt      ENABLE ROW LEVEL SECURITY;
    ALTER TABLE procurement.goods_receipt      FORCE  ROW LEVEL SECURITY;
    ALTER TABLE procurement.goods_receipt_line ENABLE ROW LEVEL SECURITY;
    ALTER TABLE procurement.goods_receipt_line FORCE  ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919172619_GoodsReceipt') THEN

    CREATE POLICY tenant_isolation ON procurement.goods_receipt
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919172619_GoodsReceipt') THEN

    CREATE POLICY tenant_isolation ON procurement.goods_receipt_line
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919172619_GoodsReceipt') THEN
    INSERT INTO procurement.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260919172619_GoodsReceipt', '9.0.0');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919201514_Quotation') THEN
    CREATE TABLE procurement.quotation (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        number bigint NOT NULL,
        requisition_id uuid NOT NULL,
        created_by character varying(200) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        closes_at timestamp with time zone NOT NULL,
        notes character varying(1000),
        status smallint NOT NULL,
        cancelled_by character varying(200),
        cancelled_at timestamp with time zone,
        cancel_reason character varying(500),
        version integer NOT NULL,
        CONSTRAINT "PK_quotation" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919201514_Quotation') THEN
    CREATE TABLE procurement.quotation_bid (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        quotation_id uuid NOT NULL,
        participant_id uuid NOT NULL,
        line_id uuid NOT NULL,
        unit_price numeric(18,6) NOT NULL,
        delivery_days integer,
        notes character varying(1000),
        CONSTRAINT "PK_quotation_bid" PRIMARY KEY (id),
        CONSTRAINT "FK_quotation_bid_quotation_quotation_id" FOREIGN KEY (quotation_id) REFERENCES procurement.quotation (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919201514_Quotation') THEN
    CREATE TABLE procurement.quotation_line (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        quotation_id uuid NOT NULL,
        requisition_line_id uuid NOT NULL,
        item_code character varying(60) NOT NULL,
        quantity numeric(18,6) NOT NULL,
        unit character varying(30) NOT NULL,
        awarded_supplier_id uuid,
        awarded_unit_price numeric(18,6),
        award_note character varying(1000),
        awarded_by character varying(200),
        awarded_at timestamp with time zone,
        CONSTRAINT "PK_quotation_line" PRIMARY KEY (id),
        CONSTRAINT "FK_quotation_line_quotation_quotation_id" FOREIGN KEY (quotation_id) REFERENCES procurement.quotation (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919201514_Quotation') THEN
    CREATE TABLE procurement.quotation_participant (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        quotation_id uuid NOT NULL,
        supplier_id uuid NOT NULL,
        invited_at timestamp with time zone NOT NULL,
        responded_at timestamp with time zone,
        is_late boolean NOT NULL,
        payment_terms character varying(120),
        freight_terms character varying(120),
        valid_until date,
        notes character varying(1000),
        CONSTRAINT "PK_quotation_participant" PRIMARY KEY (id),
        CONSTRAINT "FK_quotation_participant_quotation_quotation_id" FOREIGN KEY (quotation_id) REFERENCES procurement.quotation (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919201514_Quotation') THEN
    CREATE UNIQUE INDEX "IX_quotation_company_id_number" ON procurement.quotation (company_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919201514_Quotation') THEN
    CREATE INDEX "IX_quotation_company_id_status" ON procurement.quotation (company_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919201514_Quotation') THEN
    CREATE INDEX "IX_quotation_bid_line_id" ON procurement.quotation_bid (line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919201514_Quotation') THEN
    CREATE UNIQUE INDEX "IX_quotation_bid_participant_id_line_id" ON procurement.quotation_bid (participant_id, line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919201514_Quotation') THEN
    CREATE INDEX "IX_quotation_bid_quotation_id" ON procurement.quotation_bid (quotation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919201514_Quotation') THEN
    CREATE INDEX "IX_quotation_line_quotation_id" ON procurement.quotation_line (quotation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919201514_Quotation') THEN
    CREATE INDEX "IX_quotation_line_requisition_line_id" ON procurement.quotation_line (requisition_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919201514_Quotation') THEN
    CREATE UNIQUE INDEX "IX_quotation_participant_quotation_id_supplier_id" ON procurement.quotation_participant (quotation_id, supplier_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919201514_Quotation') THEN

    ALTER TABLE procurement.quotation             ENABLE ROW LEVEL SECURITY;
    ALTER TABLE procurement.quotation             FORCE  ROW LEVEL SECURITY;
    ALTER TABLE procurement.quotation_line        ENABLE ROW LEVEL SECURITY;
    ALTER TABLE procurement.quotation_line        FORCE  ROW LEVEL SECURITY;
    ALTER TABLE procurement.quotation_participant ENABLE ROW LEVEL SECURITY;
    ALTER TABLE procurement.quotation_participant FORCE  ROW LEVEL SECURITY;
    ALTER TABLE procurement.quotation_bid         ENABLE ROW LEVEL SECURITY;
    ALTER TABLE procurement.quotation_bid         FORCE  ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919201514_Quotation') THEN

    CREATE POLICY tenant_isolation ON procurement.quotation
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919201514_Quotation') THEN

    CREATE POLICY tenant_isolation ON procurement.quotation_line
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919201514_Quotation') THEN

    CREATE POLICY tenant_isolation ON procurement.quotation_participant
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919201514_Quotation') THEN

    CREATE POLICY tenant_isolation ON procurement.quotation_bid
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919201514_Quotation') THEN
    INSERT INTO procurement.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260919201514_Quotation', '9.0.0');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919210402_PurchaseInvoice') THEN
    CREATE TABLE procurement.purchase_invoice (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        purchase_order_id uuid NOT NULL,
        access_key character varying(44) NOT NULL,
        number bigint NOT NULL,
        series character varying(10) NOT NULL,
        issued_at timestamp with time zone NOT NULL,
        emitter_tax_id character varying(20) NOT NULL,
        emitter_name character varying(200) NOT NULL,
        total_value numeric(18,2) NOT NULL,
        imported_by character varying(200) NOT NULL,
        imported_at timestamp with time zone NOT NULL,
        status smallint NOT NULL,
        matched_at timestamp with time zone,
        match_summary character varying(2000),
        released_to_finance boolean NOT NULL,
        released_by character varying(200),
        released_at timestamp with time zone,
        release_note character varying(1000),
        version integer NOT NULL,
        CONSTRAINT "PK_purchase_invoice" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919210402_PurchaseInvoice') THEN
    CREATE TABLE procurement.purchase_invoice_line (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        invoice_id uuid NOT NULL,
        item_number integer NOT NULL,
        product_code character varying(60) NOT NULL,
        description character varying(255) NOT NULL,
        ncm character varying(10),
        cfop character varying(10),
        unit character varying(30) NOT NULL,
        quantity numeric(18,6) NOT NULL,
        unit_price numeric(18,6) NOT NULL,
        total_value numeric(18,2) NOT NULL,
        CONSTRAINT "PK_purchase_invoice_line" PRIMARY KEY (id),
        CONSTRAINT "FK_purchase_invoice_line_purchase_invoice_invoice_id" FOREIGN KEY (invoice_id) REFERENCES procurement.purchase_invoice (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919210402_PurchaseInvoice') THEN
    CREATE UNIQUE INDEX "IX_purchase_invoice_company_id_access_key" ON procurement.purchase_invoice (company_id, access_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919210402_PurchaseInvoice') THEN
    CREATE INDEX "IX_purchase_invoice_company_id_purchase_order_id" ON procurement.purchase_invoice (company_id, purchase_order_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919210402_PurchaseInvoice') THEN
    CREATE INDEX "IX_purchase_invoice_line_invoice_id" ON procurement.purchase_invoice_line (invoice_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919210402_PurchaseInvoice') THEN
    CREATE UNIQUE INDEX "IX_purchase_invoice_line_invoice_id_item_number" ON procurement.purchase_invoice_line (invoice_id, item_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919210402_PurchaseInvoice') THEN

    ALTER TABLE procurement.purchase_invoice      ENABLE ROW LEVEL SECURITY;
    ALTER TABLE procurement.purchase_invoice      FORCE  ROW LEVEL SECURITY;
    ALTER TABLE procurement.purchase_invoice_line ENABLE ROW LEVEL SECURITY;
    ALTER TABLE procurement.purchase_invoice_line FORCE  ROW LEVEL SECURITY;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919210402_PurchaseInvoice') THEN

    CREATE POLICY tenant_isolation ON procurement.purchase_invoice
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919210402_PurchaseInvoice') THEN

    CREATE POLICY tenant_isolation ON procurement.purchase_invoice_line
        USING      (company_id = foundation.current_company())
        WITH CHECK (company_id = foundation.current_company());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919210402_PurchaseInvoice') THEN
    INSERT INTO procurement.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260919210402_PurchaseInvoice', '9.0.0');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919211805_RequisitionNeededBy') THEN
    ALTER TABLE procurement.purchase_requisition ADD needed_by date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919211805_RequisitionNeededBy') THEN
    INSERT INTO procurement.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260919211805_RequisitionNeededBy', '9.0.0');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919212756_RequisitionCreatedAtIndex') THEN
    CREATE INDEX "IX_purchase_requisition_company_id_created_at" ON procurement.purchase_requisition (company_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM procurement.__ef_migrations WHERE "MigrationId" = '20260919212756_RequisitionCreatedAtIndex') THEN
    INSERT INTO procurement.__ef_migrations ("MigrationId", "ProductVersion")
    VALUES ('20260919212756_RequisitionCreatedAtIndex', '9.0.0');
    END IF;
END $EF$;
COMMIT;

