using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotationProcess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "bid_number_seq",
                schema: "procurement");

            migrationBuilder.CreateSequence(
                name: "rfq_number_seq",
                schema: "procurement");

            migrationBuilder.AddColumn<string>(
                name: "portal_key_hash",
                schema: "procurement",
                table: "supplier",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "delivery_days",
                schema: "procurement",
                table: "purchase_order",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "freight_value",
                schema: "procurement",
                table: "purchase_order",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_terms",
                schema: "procurement",
                table: "purchase_order",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pdf_document_id",
                schema: "procurement",
                table: "purchase_order",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "quotation_id",
                schema: "procurement",
                table: "purchase_order",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "quotation_number",
                schema: "procurement",
                table: "purchase_order",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "company_profile",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    district = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    zip = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    tax_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    state_registration = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    delivery_address = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    delivery_tax_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    standard_clauses = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    payment_policy = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_profile", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "process_event",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quotation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    from_status = table.Column<short>(type: "smallint", nullable: true),
                    to_status = table.Column<short>(type: "smallint", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_process_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quotation",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    source_pr_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_pr_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    cost_center = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    justification = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    winner_supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    winner_proposal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    selection_criteria = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    selection_justification = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    selected_by = table.Column<Guid>(type: "uuid", nullable: true),
                    selected_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    selected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    manager_approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    manager_approved_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    manager_approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    director_approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    director_approved_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    director_approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purchase_order_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stored_document",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stored_document", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "proposal",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quotation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    total_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    delivery_days = table.Column<int>(type: "integer", nullable: true),
                    payment_terms = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    freight_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    submitted_via = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    submitted_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attachment_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attachment_file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposal", x => x.id);
                    table.ForeignKey(
                        name: "FK_proposal_quotation_quotation_id",
                        column: x => x.quotation_id,
                        principalSchema: "procurement",
                        principalTable: "quotation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quotation_item",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quotation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    catalog_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_of_measure = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotation_item", x => x.id);
                    table.ForeignKey(
                        name: "FK_quotation_item_quotation_quotation_id",
                        column: x => x.quotation_id,
                        principalSchema: "procurement",
                        principalTable: "quotation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quotation_supplier",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quotation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    tax_id = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: false),
                    invited_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    invited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotation_supplier", x => x.id);
                    table.ForeignKey(
                        name: "FK_quotation_supplier_quotation_quotation_id",
                        column: x => x.quotation_id,
                        principalSchema: "procurement",
                        principalTable: "quotation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "proposal_item",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quotation_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposal_item", x => x.id);
                    table.ForeignKey(
                        name: "FK_proposal_item_proposal_proposal_id",
                        column: x => x.proposal_id,
                        principalSchema: "procurement",
                        principalTable: "proposal",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_process_event_quotation_id_occurred_at",
                schema: "procurement",
                table: "process_event",
                columns: new[] { "quotation_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_proposal_quotation_id_supplier_id_version_number",
                schema: "procurement",
                table: "proposal",
                columns: new[] { "quotation_id", "supplier_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_proposal_item_proposal_id",
                schema: "procurement",
                table: "proposal_item",
                column: "proposal_id");

            migrationBuilder.CreateIndex(
                name: "IX_quotation_number",
                schema: "procurement",
                table: "quotation",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quotation_source_pr_id",
                schema: "procurement",
                table: "quotation",
                column: "source_pr_id");

            migrationBuilder.CreateIndex(
                name: "IX_quotation_status_created_at",
                schema: "procurement",
                table: "quotation",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_quotation_item_quotation_id",
                schema: "procurement",
                table: "quotation_item",
                column: "quotation_id");

            migrationBuilder.CreateIndex(
                name: "IX_quotation_supplier_quotation_id_supplier_id",
                schema: "procurement",
                table: "quotation_supplier",
                columns: new[] { "quotation_id", "supplier_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stored_document_entity_type_entity_id",
                schema: "foundation",
                table: "stored_document",
                columns: new[] { "entity_type", "entity_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_profile",
                schema: "foundation");

            migrationBuilder.DropTable(
                name: "process_event",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "proposal_item",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "quotation_item",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "quotation_supplier",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "stored_document",
                schema: "foundation");

            migrationBuilder.DropTable(
                name: "proposal",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "quotation",
                schema: "procurement");

            migrationBuilder.DropColumn(
                name: "portal_key_hash",
                schema: "procurement",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "delivery_days",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "freight_value",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "payment_terms",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "pdf_document_id",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "quotation_id",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "quotation_number",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropSequence(
                name: "bid_number_seq",
                schema: "procurement");

            migrationBuilder.DropSequence(
                name: "rfq_number_seq",
                schema: "procurement");
        }
    }
}
