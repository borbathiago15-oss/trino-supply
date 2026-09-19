using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.BuildingBlocks.Outbox;
using TrinoSupply.Procurement.Domain;

namespace TrinoSupply.Procurement.Infrastructure.Persistence;

/// <summary>Contexto de persistência de Compras (schema <c>procurement</c>). RLS por conexão (SEC-004).</summary>
public sealed class ProcurementDbContext(DbContextOptions<ProcurementDbContext> options) : DbContext(options)
{
    public DbSet<PurchaseRequisition> Requisitions => Set<PurchaseRequisition>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PayingCompany> PayingCompanies => Set<PayingCompany>();
    public DbSet<PurchaseOrder> Orders => Set<PurchaseOrder>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<SupplierStats> SupplierStats => Set<SupplierStats>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<PurchaseInvoice> Invoices => Set<PurchaseInvoice>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("procurement");

        b.Entity<PurchaseRequisition>(e =>
        {
            e.ToTable("purchase_requisition");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasConversion(id => id.Value, v => RequisitionId.From(v));
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.RequesterSubject).HasColumnName("requester_subject").HasMaxLength(200).IsRequired();
            e.Property(x => x.PayingCompanyId).HasColumnName("paying_company_id").HasConversion(id => id.Value, v => PayingCompanyId.From(v));
            e.Property(x => x.CostCenterId).HasColumnName("cost_center_id").HasConversion(id => id.Value, v => CostCenterId.From(v));
            e.Property(x => x.Priority).HasColumnName("priority").HasConversion<short>();
            e.Property(x => x.Justification).HasColumnName("justification").HasMaxLength(2000).IsRequired();
            e.Property(x => x.ApproverLevel1Subject).HasColumnName("approver_l1_subject").HasMaxLength(200).IsRequired();
            e.Property(x => x.ApproverLevel2Subject).HasColumnName("approver_l2_subject").HasMaxLength(200).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.NeededBy).HasColumnName("needed_by");
            e.Property(x => x.Level1DecidedBySubject).HasColumnName("l1_decided_by").HasMaxLength(200);
            e.Property(x => x.Level1DecidedAt).HasColumnName("l1_decided_at");
            e.Property(x => x.Level2DecidedBySubject).HasColumnName("l2_decided_by").HasMaxLength(200);
            e.Property(x => x.Level2DecidedAt).HasColumnName("l2_decided_at");
            e.Property(x => x.RejectedBySubject).HasColumnName("rejected_by").HasMaxLength(200);
            e.Property(x => x.RejectedAt).HasColumnName("rejected_at");
            e.Property(x => x.DecisionNote).HasColumnName("decision_note").HasMaxLength(500);
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasIndex(x => new { x.CompanyId, x.Status });
            e.Ignore(x => x.DomainEvents);

            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.RequisitionId);
        });

        b.Entity<CostCenter>(e =>
        {
            e.ToTable("cost_center");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasConversion(id => id.Value, v => CostCenterId.From(v));
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(x => x.PayingCompanyId).HasColumnName("paying_company_id")
                .HasConversion(id => id!.Value.Value, v => PayingCompanyId.From(v));
            e.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            e.Ignore(x => x.DomainEvents);
        });

        b.Entity<RequisitionLine>(e =>
        {
            e.ToTable("requisition_line");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.RequisitionId).HasColumnName("requisition_id").HasConversion(id => id.Value, v => RequisitionId.From(v));
            e.Property(x => x.ItemCode).HasColumnName("item_code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,6)");
            e.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(30).IsRequired();
            // v3: OC que cobre a linha (nula = a pedir). Rastreabilidade item→OC na compra dividida.
            e.Property(x => x.PurchaseOrderId).HasColumnName("purchase_order_id");
            e.Ignore(x => x.IsPending);
            e.HasIndex(x => x.RequisitionId);
            e.HasIndex(x => x.PurchaseOrderId);
        });

        b.Entity<Supplier>(e =>
        {
            e.ToTable("supplier");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasConversion(id => id.Value, v => SupplierId.From(v));
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(x => x.TaxId).HasColumnName("tax_id").HasMaxLength(30);
            e.Property(x => x.StateRegistration).HasColumnName("state_registration").HasMaxLength(30);
            e.Property(x => x.Address).HasColumnName("address").HasMaxLength(200);
            e.Property(x => x.District).HasColumnName("district").HasMaxLength(120);
            e.Property(x => x.City).HasColumnName("city").HasMaxLength(120);
            e.Property(x => x.State).HasColumnName("state").HasMaxLength(2);
            e.Property(x => x.ZipCode).HasColumnName("zip_code").HasMaxLength(12);
            e.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(40);
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(200);
            e.Property(x => x.PaymentTerms).HasColumnName("payment_terms").HasMaxLength(80);
            e.Property(x => x.PaymentMethod).HasColumnName("payment_method").HasMaxLength(80);
            e.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            e.Ignore(x => x.DomainEvents);
        });

        b.Entity<PayingCompany>(e =>
        {
            e.ToTable("paying_company");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasConversion(id => id.Value, v => PayingCompanyId.From(v));
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            e.Property(x => x.LegalName).HasColumnName("legal_name").HasMaxLength(200).IsRequired();
            e.Property(x => x.TaxId).HasColumnName("tax_id").HasMaxLength(30).IsRequired();
            e.Property(x => x.StateRegistration).HasColumnName("state_registration").HasMaxLength(30);
            e.Property(x => x.Address).HasColumnName("address").HasMaxLength(200);
            e.Property(x => x.District).HasColumnName("district").HasMaxLength(120);
            e.Property(x => x.City).HasColumnName("city").HasMaxLength(120);
            e.Property(x => x.State).HasColumnName("state").HasMaxLength(2);
            e.Property(x => x.ZipCode).HasColumnName("zip_code").HasMaxLength(12);
            e.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(40);
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(200);
            e.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            e.Ignore(x => x.DomainEvents);
        });

        b.Entity<PurchaseOrder>(e =>
        {
            e.ToTable("purchase_order");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasConversion(id => id.Value, v => PurchaseOrderId.From(v));
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.Number).HasColumnName("number");
            e.Property(x => x.RequisitionId).HasColumnName("requisition_id").HasConversion(id => id.Value, v => RequisitionId.From(v));
            e.Property(x => x.PayingCompanyId).HasColumnName("paying_company_id").HasConversion(id => id.Value, v => PayingCompanyId.From(v));
            e.Property(x => x.SupplierId).HasColumnName("supplier_id").HasConversion(id => id.Value, v => SupplierId.From(v));
            e.Property(x => x.PaymentTerms).HasColumnName("payment_terms").HasMaxLength(80);
            e.Property(x => x.PaymentMethod).HasColumnName("payment_method").HasMaxLength(80);
            e.Property(x => x.IpiValue).HasColumnName("ipi_value").HasColumnType("numeric(18,2)");
            e.Property(x => x.IcmsValue).HasColumnName("icms_value").HasColumnType("numeric(18,2)");
            e.Property(x => x.DiscountValue).HasColumnName("discount_value").HasColumnType("numeric(18,2)");
            e.Property(x => x.OtherExpenses).HasColumnName("other_expenses").HasColumnType("numeric(18,2)");
            e.Property(x => x.FreightTerms).HasColumnName("freight_terms").HasMaxLength(80);
            e.Property(x => x.IssuedBySubject).HasColumnName("issued_by_subject").HasMaxLength(200).IsRequired();
            e.Property(x => x.IssuedAt).HasColumnName("issued_at");
            e.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
            e.Property(x => x.CancelledBySubject).HasColumnName("cancelled_by_subject").HasMaxLength(200);
            e.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
            e.Property(x => x.CancelReason).HasColumnName("cancel_reason").HasMaxLength(500);
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            // v3 (compra dividida): uma requisição pode gerar VÁRIAS OCs — uma por fornecedor. O que
            // impede pedido em duplicidade agora é o vínculo por LINHA (requisition_line.purchase_order_id),
            // não um índice único por requisição. Índice não-único só para consulta.
            e.HasIndex(x => new { x.CompanyId, x.RequisitionId });
            e.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();        // nº da OC único por tenant
            e.Ignore(x => x.ProductsValue);
            e.Ignore(x => x.NetValue);
            e.Ignore(x => x.DomainEvents);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.OrderId);
        });

        b.Entity<OrderLine>(e =>
        {
            e.ToTable("order_line");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.OrderId).HasColumnName("order_id").HasConversion(id => id.Value, v => PurchaseOrderId.From(v));
            e.Property(x => x.ItemCode).HasColumnName("item_code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(300);
            e.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,6)");
            e.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(30).IsRequired();
            e.Property(x => x.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(18,4)");
            e.Property(x => x.IrrfPercent).HasColumnName("irrf_percent").HasColumnType("numeric(9,4)");
            e.Property(x => x.IssPercent).HasColumnName("iss_percent").HasColumnType("numeric(9,4)");
            e.Property(x => x.DeliveryDate).HasColumnName("delivery_date");
            e.Ignore(x => x.ServiceValue);
            e.Ignore(x => x.IrrfValue);
            e.Ignore(x => x.IssValue);
            e.HasIndex(x => x.OrderId);
        });

        // Projeção de histórico por fornecedor (read model), atualizada pelo consumidor de eventos.
        // ---- Recebimento de mercadoria (MMS-005) ----
        b.Entity<GoodsReceipt>(e =>
        {
            e.ToTable("goods_receipt");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasConversion(id => id.Value, v => GoodsReceiptId.From(v));
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.PurchaseOrderId).HasColumnName("purchase_order_id").HasConversion(id => id.Value, v => PurchaseOrderId.From(v));
            e.Property(x => x.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(60).IsRequired();
            e.Property(x => x.InvoiceDate).HasColumnName("invoice_date");
            e.Property(x => x.ReceivedBySubject).HasColumnName("received_by").HasMaxLength(200).IsRequired();
            e.Property(x => x.ReceivedAt).HasColumnName("received_at");
            e.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(1000);
            e.Property(x => x.StockPosted).HasColumnName("stock_posted");
            e.Property(x => x.StockPostedAt).HasColumnName("stock_posted_at");
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.ReceiptId);
            e.HasIndex(x => new { x.CompanyId, x.PurchaseOrderId });
            e.Ignore(x => x.HasOccurrence);
            e.Ignore(x => x.DomainEvents);
        });

        b.Entity<GoodsReceiptLine>(e =>
        {
            e.ToTable("goods_receipt_line");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.ReceiptId).HasColumnName("receipt_id").HasConversion(id => id.Value, v => GoodsReceiptId.From(v));
            e.Property(x => x.OrderLineId).HasColumnName("order_line_id");
            e.Property(x => x.ItemCode).HasColumnName("item_code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(30).IsRequired();
            e.Property(x => x.QuantityOrdered).HasColumnName("quantity_ordered").HasColumnType("numeric(18,6)");
            e.Property(x => x.QuantityReceived).HasColumnName("quantity_received").HasColumnType("numeric(18,6)");
            e.Property(x => x.QuantityDamaged).HasColumnName("quantity_damaged").HasColumnType("numeric(18,6)");
            e.Property(x => x.Occurrence).HasColumnName("occurrence").HasConversion<short>();
            e.Property(x => x.OccurrenceNote).HasColumnName("occurrence_note").HasMaxLength(1000);
            e.Ignore(x => x.NetQuantity);
            e.HasIndex(x => x.ReceiptId);
            e.HasIndex(x => x.OrderLineId);
        });

        b.Entity<Quotation>(e =>
        {
            e.ToTable("quotation");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasConversion(id => id.Value, v => QuotationId.From(v));
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.Number).HasColumnName("number");
            e.Property(x => x.RequisitionId).HasColumnName("requisition_id").HasConversion(id => id.Value, v => RequisitionId.From(v));
            e.Property(x => x.CreatedBySubject).HasColumnName("created_by").HasMaxLength(200).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.ClosesAt).HasColumnName("closes_at");
            e.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(1000);
            e.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
            e.Property(x => x.CancelledBySubject).HasColumnName("cancelled_by").HasMaxLength(200);
            e.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
            e.Property(x => x.CancelReason).HasColumnName("cancel_reason").HasMaxLength(500);
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.QuotationId);
            e.HasMany(x => x.Participants).WithOne().HasForeignKey(p => p.QuotationId);
            e.HasMany(x => x.Bids).WithOne().HasForeignKey(b => b.QuotationId);
            // Número da cotação é único por tenant (o sequencial se resolve por retry na colisão).
            e.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.Status });
            e.Ignore(x => x.ResponseCount);
            e.Ignore(x => x.DomainEvents);
        });

        b.Entity<QuotationLine>(e =>
        {
            e.ToTable("quotation_line");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.QuotationId).HasColumnName("quotation_id").HasConversion(id => id.Value, v => QuotationId.From(v));
            e.Property(x => x.RequisitionLineId).HasColumnName("requisition_line_id");
            e.Property(x => x.ItemCode).HasColumnName("item_code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,6)");
            e.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(30).IsRequired();
            e.Property(x => x.AwardedSupplierId).HasColumnName("awarded_supplier_id");
            e.Property(x => x.AwardedUnitPrice).HasColumnName("awarded_unit_price").HasColumnType("numeric(18,6)");
            e.Property(x => x.AwardNote).HasColumnName("award_note").HasMaxLength(1000);
            e.Property(x => x.AwardedBySubject).HasColumnName("awarded_by").HasMaxLength(200);
            e.Property(x => x.AwardedAt).HasColumnName("awarded_at");
            e.Ignore(x => x.IsAwarded);
            e.HasIndex(x => x.QuotationId);
            e.HasIndex(x => x.RequisitionLineId);
        });

        b.Entity<QuotationParticipant>(e =>
        {
            e.ToTable("quotation_participant");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.QuotationId).HasColumnName("quotation_id").HasConversion(id => id.Value, v => QuotationId.From(v));
            e.Property(x => x.SupplierId).HasColumnName("supplier_id");
            e.Property(x => x.InvitedAt).HasColumnName("invited_at");
            e.Property(x => x.RespondedAt).HasColumnName("responded_at");
            e.Property(x => x.IsLate).HasColumnName("is_late");
            e.Property(x => x.PaymentTerms).HasColumnName("payment_terms").HasMaxLength(120);
            e.Property(x => x.FreightTerms).HasColumnName("freight_terms").HasMaxLength(120);
            e.Property(x => x.ValidUntil).HasColumnName("valid_until");
            e.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(1000);
            e.Ignore(x => x.HasResponded);
            // Um fornecedor entra uma única vez na mesma cotação.
            e.HasIndex(x => new { x.QuotationId, x.SupplierId }).IsUnique();
        });

        b.Entity<QuotationBid>(e =>
        {
            e.ToTable("quotation_bid");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.QuotationId).HasColumnName("quotation_id").HasConversion(id => id.Value, v => QuotationId.From(v));
            e.Property(x => x.ParticipantId).HasColumnName("participant_id");
            e.Property(x => x.LineId).HasColumnName("line_id");
            e.Property(x => x.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(18,6)");
            e.Property(x => x.DeliveryDays).HasColumnName("delivery_days");
            e.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(1000);
            // Uma oferta por participante e item (recotar substitui a anterior).
            e.HasIndex(x => new { x.ParticipantId, x.LineId }).IsUnique();
            e.HasIndex(x => x.LineId);
        });

        b.Entity<PurchaseInvoice>(e =>
        {
            e.ToTable("purchase_invoice");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasConversion(id => id.Value, v => PurchaseInvoiceId.From(v));
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.PurchaseOrderId).HasColumnName("purchase_order_id").HasConversion(id => id.Value, v => PurchaseOrderId.From(v));
            e.Property(x => x.AccessKey).HasColumnName("access_key").HasMaxLength(44).IsRequired();
            e.Property(x => x.Number).HasColumnName("number");
            e.Property(x => x.Series).HasColumnName("series").HasMaxLength(10).IsRequired();
            e.Property(x => x.IssuedAt).HasColumnName("issued_at");
            e.Property(x => x.EmitterTaxId).HasColumnName("emitter_tax_id").HasMaxLength(20).IsRequired();
            e.Property(x => x.EmitterName).HasColumnName("emitter_name").HasMaxLength(200).IsRequired();
            e.Property(x => x.TotalValue).HasColumnName("total_value").HasColumnType("numeric(18,2)");
            e.Property(x => x.ImportedBySubject).HasColumnName("imported_by").HasMaxLength(200).IsRequired();
            e.Property(x => x.ImportedAt).HasColumnName("imported_at");
            e.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
            e.Property(x => x.MatchedAt).HasColumnName("matched_at");
            e.Property(x => x.MatchSummary).HasColumnName("match_summary").HasMaxLength(2000);
            e.Property(x => x.ReleasedToFinance).HasColumnName("released_to_finance");
            e.Property(x => x.ReleasedBySubject).HasColumnName("released_by").HasMaxLength(200);
            e.Property(x => x.ReleasedAt).HasColumnName("released_at");
            e.Property(x => x.ReleaseNote).HasColumnName("release_note").HasMaxLength(1000);
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.InvoiceId);
            // A chave de acesso é a identidade fiscal da nota: a mesma não entra duas vezes.
            e.HasIndex(x => new { x.CompanyId, x.AccessKey }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.PurchaseOrderId });
            e.Ignore(x => x.DomainEvents);
        });

        b.Entity<PurchaseInvoiceLine>(e =>
        {
            e.ToTable("purchase_invoice_line");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.InvoiceId).HasColumnName("invoice_id").HasConversion(id => id.Value, v => PurchaseInvoiceId.From(v));
            e.Property(x => x.ItemNumber).HasColumnName("item_number");
            e.Property(x => x.ProductCode).HasColumnName("product_code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(255).IsRequired();
            e.Property(x => x.Ncm).HasColumnName("ncm").HasMaxLength(10);
            e.Property(x => x.Cfop).HasColumnName("cfop").HasMaxLength(10);
            e.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(30).IsRequired();
            e.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,6)");
            e.Property(x => x.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(18,6)");
            e.Property(x => x.TotalValue).HasColumnName("total_value").HasColumnType("numeric(18,2)");
            e.HasIndex(x => x.InvoiceId);
            e.HasIndex(x => new { x.InvoiceId, x.ItemNumber }).IsUnique();
        });

        b.Entity<SupplierStats>(e =>
        {
            e.ToTable("supplier_stats");
            e.HasKey(x => new { x.CompanyId, x.SupplierId });
            e.Property(x => x.CompanyId).HasColumnName("company_id");
            e.Property(x => x.SupplierId).HasColumnName("supplier_id");
            e.Property(x => x.OrdersCount).HasColumnName("orders_count");
            e.Property(x => x.TotalValue).HasColumnName("total_value").HasColumnType("numeric(18,2)");
            e.Property(x => x.LastOrderAt).HasColumnName("last_order_at");
        });

        // Idempotência do consumidor (infra, sem RLS): um evento aplicado no máximo uma vez.
        b.Entity<ProcessedEvent>(e =>
        {
            e.ToTable("processed_event");
            e.HasKey(x => x.EventId);
            e.Property(x => x.EventId).HasColumnName("event_id");
            e.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        });

        // Outbox transacional (ARC-005): eventos do Procurement gravam na MESMA tabela do Foundation
        // (foundation.outbox). O relay do Worker relê e publica; sem duplicar infra.
        b.Entity<OutboxMessage>(e =>
        {
            // Tabela pertence ao Foundation (foundation.outbox) — não é gerenciada pelas migrations do
            // Procurement; aqui só a mapeamos para gravar/ler. ExcludeFromMigrations evita duplicá-la.
            e.ToTable("outbox", "foundation", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CompanyId).HasColumnName("company_id");
            e.Property(x => x.Type).HasColumnName("type").HasMaxLength(200).IsRequired();
            e.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.AggregateType).HasColumnName("aggregate_type").HasMaxLength(100);
            e.Property(x => x.AggregateId).HasColumnName("aggregate_id");
            e.Property(x => x.CorrelationId).HasColumnName("correlation_id");
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            e.Property(x => x.PublishedAt).HasColumnName("published_at");
            e.Property(x => x.RetryCount).HasColumnName("retry_count");
            e.Property(x => x.LastError).HasColumnName("last_error");
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        WriteOutboxFromDomainEvents();
        return await base.SaveChangesAsync(ct);
    }

    /// <summary>Coleta eventos de domínio das raízes rastreadas e os grava no Outbox (mesma transação).</summary>
    private void WriteOutboxFromDomainEvents()
    {
        var roots = ChangeTracker.Entries<IHasDomainEvents>()
            .Where(x => x.Entity.DomainEvents.Count > 0)
            .ToList();

        foreach (var entry in roots)
        {
            var root = entry.Entity;
            var companyId = entry.Entity is IBelongsToTenant owned ? owned.CompanyId.Value : Guid.Empty;
            foreach (var ev in root.DomainEvents)
            {
                Outbox.Add(new OutboxMessage
                {
                    Id = ev.EventId == Guid.Empty ? Guid.NewGuid() : ev.EventId,
                    CompanyId = companyId,
                    Type = ev.GetType().Name,
                    Payload = JsonSerializer.Serialize(ev, ev.GetType()),
                    AggregateType = root.GetType().Name,
                    AggregateId = null,
                    OccurredAt = ev.OccurredAt,
                });
            }
            root.ClearDomainEvents();
        }
    }
}
