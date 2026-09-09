using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Api.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PurchaseRequisition> Requisitions => Set<PurchaseRequisition>();
    public DbSet<RequisitionItem> RequisitionItems => Set<RequisitionItem>();
    public DbSet<Catalog.CatalogItem> CatalogItems => Set<Catalog.CatalogItem>();
    public DbSet<Catalog.CatalogItemSupplier> CatalogItemSuppliers => Set<Catalog.CatalogItemSupplier>();
    public DbSet<Catalog.ProductFamily> ProductFamilies => Set<Catalog.ProductFamily>();
    public DbSet<Inventory.StorageLocation> StorageLocations => Set<Inventory.StorageLocation>();
    public DbSet<Inventory.StockBalance> StockBalances => Set<Inventory.StockBalance>();
    public DbSet<Inventory.StockMovement> StockMovements => Set<Inventory.StockMovement>();
    public DbSet<Materials.MaterialRequisition> MaterialRequisitions => Set<Materials.MaterialRequisition>();
    public DbSet<Materials.MaterialRequisitionItem> MaterialRequisitionItems => Set<Materials.MaterialRequisitionItem>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierContractItem> SupplierContractItems => Set<SupplierContractItem>();
    public DbSet<SupplierDocument> SupplierDocuments => Set<SupplierDocument>();
    public DbSet<ContractAdjustment> ContractAdjustments => Set<ContractAdjustment>();
    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<QuotationItem> QuotationItems => Set<QuotationItem>();
    public DbSet<QuotationSupplier> QuotationSuppliers => Set<QuotationSupplier>();
    public DbSet<QuotationAward> QuotationAwards => Set<QuotationAward>();
    public DbSet<Proposal> Proposals => Set<Proposal>();
    public DbSet<ProposalItem> ProposalItems => Set<ProposalItem>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<ScoreWeights> ScoreWeights => Set<ScoreWeights>();
    public DbSet<PaymentTermOption> PaymentTermOptions => Set<PaymentTermOption>();
    public DbSet<ProcessEvent> ProcessEvents => Set<ProcessEvent>();
    public DbSet<StoredDocument> StoredDocuments => Set<StoredDocument>();
    public DbSet<RequisitionAttachment> RequisitionAttachments => Set<RequisitionAttachment>();
    public DbSet<PurchaseOrderInvoice> PurchaseOrderInvoices => Set<PurchaseOrderInvoice>();
    public DbSet<Domain.CostCenterApprover> CostCenterApprovers => Set<Domain.CostCenterApprover>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AnnouncementDismissal> AnnouncementDismissals => Set<AnnouncementDismissal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("foundation");
        modelBuilder.HasSequence<long>("pr_number_seq", "procurement").StartsAt(1);  // PR-001-11 (numeração)
        modelBuilder.HasSequence<long>("mov_number_seq", "materials").StartsAt(1);   // MMS-004 (movimentações)
        modelBuilder.HasSequence<long>("mr_number_seq", "materials").StartsAt(1);    // MMS-003 (solicitações)
        modelBuilder.HasSequence<long>("po_number_seq", "procurement").StartsAt(1);  // PO-001 (pedidos)
        modelBuilder.HasSequence<long>("rfq_number_seq", "procurement").StartsAt(1); // RFQ-001 (cotações)
        modelBuilder.HasSequence<long>("bid_number_seq", "procurement").StartsAt(1); // RFQ-001 (BIDs)

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("app_user");
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).HasColumnName("id");
            e.Property(u => u.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
            e.Property(u => u.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(u => u.PasswordHash).HasColumnName("password_hash").IsRequired();
            e.Property(u => u.Role).HasColumnName("role").HasMaxLength(50).IsRequired();
            e.Property(u => u.Modules).HasColumnName("modules").HasMaxLength(300);
            e.Property(u => u.CostCenters).HasColumnName("cost_centers").HasMaxLength(2000);
            e.Property(u => u.DirectorId).HasColumnName("director_id");
            e.Property(u => u.Active).HasColumnName("active");
            e.Property(u => u.MustChangePassword).HasColumnName("must_change_password");
            e.Property(u => u.PasswordChangedAt).HasColumnName("password_changed_at");
            e.Property(u => u.CreatedAt).HasColumnName("created_at");
            e.Property(u => u.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<Catalog.CatalogItem>(e =>
        {
            e.ToTable("catalog_item", "materials"); // MMS-002 MVP (modelo completo em MMS-002-11)
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasColumnName("id");
            e.Property(i => i.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
            e.Property(i => i.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
            e.Property(i => i.Family).HasColumnName("family").HasMaxLength(120).IsRequired();
            e.Property(i => i.UnitOfMeasure).HasColumnName("unit_of_measure").HasMaxLength(10);
            e.Property(i => i.ReferencePrice).HasColumnName("reference_price").HasPrecision(18, 4);
            e.Property(i => i.StockControlled).HasColumnName("stock_controlled");
            e.Property(i => i.Purchasable).HasColumnName("purchasable");
            e.Property(i => i.MinimumQty).HasColumnName("minimum_qty").HasPrecision(18, 4);
            e.Property(i => i.ProductType).HasColumnName("product_type").HasMaxLength(30);
            e.Property(i => i.BaseCode).HasColumnName("base_code").HasMaxLength(50);
            e.Property(i => i.Size).HasColumnName("size").HasMaxLength(10);
            e.Property(i => i.ImageDocumentId).HasColumnName("image_document_id");
            e.Property(i => i.ImageFileName).HasColumnName("image_file_name").HasMaxLength(300);
            e.Property(i => i.Active).HasColumnName("active");
            e.Property(i => i.CreatedAt).HasColumnName("created_at");
            e.Property(i => i.UpdatedAt).HasColumnName("updated_at");
            e.Property(i => i.CreatedBy).HasColumnName("created_by");
            e.Property(i => i.Version).HasColumnName("version");
            e.HasIndex(i => i.Code).IsUnique();
            e.HasIndex(i => new { i.Family, i.Active });
            e.HasMany(i => i.Suppliers).WithOne().HasForeignKey(s => s.CatalogItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Catalog.ProductFamily>(e =>
        {
            e.ToTable("product_family", "materials");
            e.HasKey(f => f.Id);
            e.Property(f => f.Id).HasColumnName("id");
            e.Property(f => f.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
            e.Property(f => f.Notes).HasColumnName("notes").HasMaxLength(300);
            e.Property(f => f.Category).HasColumnName("category").HasMaxLength(120);
            e.Property(f => f.LeadRequestToQuote).HasColumnName("lead_request_to_quote");
            e.Property(f => f.LeadQuoteToApproval).HasColumnName("lead_quote_to_approval");
            e.Property(f => f.LeadApprovalToPo).HasColumnName("lead_approval_to_po");
            e.Property(f => f.LeadPoToDelivery).HasColumnName("lead_po_to_delivery");
            e.Ignore(f => f.LeadTotal);
            e.Property(f => f.Active).HasColumnName("active");
            e.Property(f => f.CreatedAt).HasColumnName("created_at");
            e.Property(f => f.UpdatedAt).HasColumnName("updated_at");
            e.Property(f => f.CreatedBy).HasColumnName("created_by");
            e.HasIndex(f => f.Name).IsUnique();
        });

        modelBuilder.Entity<Catalog.CatalogItemSupplier>(e =>
        {
            e.ToTable("catalog_item_supplier", "materials");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasColumnName("id");
            e.Property(s => s.CatalogItemId).HasColumnName("catalog_item_id");
            e.Property(s => s.SupplierName).HasColumnName("supplier_name").HasMaxLength(300).IsRequired();
            e.Property(s => s.TaxId).HasColumnName("tax_id").HasMaxLength(14);
            e.Property(s => s.Contact).HasColumnName("contact").HasMaxLength(200);
            e.Property(s => s.SupplierItemCode).HasColumnName("supplier_item_code").HasMaxLength(60);
            e.Property(s => s.LastPrice).HasColumnName("last_price").HasPrecision(18, 4);
            e.Property(s => s.CaNumber).HasColumnName("ca_number").HasMaxLength(30);
            e.Property(s => s.Notes).HasColumnName("notes").HasMaxLength(500);
            e.Property(s => s.SupplierId).HasColumnName("supplier_id");
            e.Property(s => s.CreatedAt).HasColumnName("created_at");
            e.HasIndex(s => s.CatalogItemId);
        });

        modelBuilder.Entity<Inventory.StorageLocation>(e =>
        {
            e.ToTable("storage_location", "materials");
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).HasColumnName("id");
            e.Property(l => l.Code).HasColumnName("code").HasMaxLength(30).IsRequired();
            e.Property(l => l.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(l => l.Active).HasColumnName("active");
            e.Property(l => l.CreatedAt).HasColumnName("created_at");
            e.Property(l => l.CreatedBy).HasColumnName("created_by");
            e.HasIndex(l => l.Code).IsUnique();
        });

        modelBuilder.Entity<Inventory.StockBalance>(e =>
        {
            e.ToTable("stock_balance", "materials"); // projeção — escrita só pelo InventoryService (MMS-P-08)
            e.HasKey(b => b.Id);
            e.Property(b => b.Id).HasColumnName("id");
            e.Property(b => b.CatalogItemId).HasColumnName("catalog_item_id");
            e.Property(b => b.LocationId).HasColumnName("location_id");
            e.Property(b => b.TotalQty).HasColumnName("total_qty").HasPrecision(18, 4);
            e.Property(b => b.ReservedQty).HasColumnName("reserved_qty").HasPrecision(18, 4);
            e.Property(b => b.LastMovementId).HasColumnName("last_movement_id");
            e.Property(b => b.LastMovementAt).HasColumnName("last_movement_at");
            e.Property(b => b.Version).HasColumnName("version").IsConcurrencyToken();
            e.Ignore(b => b.AvailableQty);
            e.HasIndex(b => new { b.CatalogItemId, b.LocationId }).IsUnique();
        });

        modelBuilder.Entity<Inventory.StockMovement>(e =>
        {
            e.ToTable("stock_movement", "materials"); // imutável (MMS-P-07)
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasColumnName("id");
            e.Property(m => m.Number).HasColumnName("number").HasMaxLength(30).IsRequired();
            e.Property(m => m.Type).HasColumnName("type").HasConversion<short>();
            e.Property(m => m.Origin).HasColumnName("origin").HasConversion<short>();
            e.Property(m => m.OriginReference).HasColumnName("origin_reference").HasMaxLength(100).IsRequired();
            e.Property(m => m.CatalogItemId).HasColumnName("catalog_item_id");
            e.Property(m => m.ItemCode).HasColumnName("item_code").HasMaxLength(50);
            e.Property(m => m.ItemDescription).HasColumnName("item_description").HasMaxLength(500);
            e.Property(m => m.UnitOfMeasure).HasColumnName("unit_of_measure").HasMaxLength(10);
            e.Property(m => m.LocationId).HasColumnName("location_id");
            e.Property(m => m.Quantity).HasColumnName("quantity").HasPrecision(18, 4);
            e.Property(m => m.BalanceBefore).HasColumnName("balance_before").HasPrecision(18, 4);
            e.Property(m => m.BalanceAfter).HasColumnName("balance_after").HasPrecision(18, 4);
            e.Property(m => m.MaterialRequisitionId).HasColumnName("material_requisition_id");
            e.Property(m => m.PerformedBy).HasColumnName("performed_by");
            e.Property(m => m.PerformedByLabel).HasColumnName("performed_by_label").HasMaxLength(200);
            e.Property(m => m.PerformedAt).HasColumnName("performed_at");
            e.HasIndex(m => m.Number).IsUnique();
            e.HasIndex(m => new { m.CatalogItemId, m.PerformedAt });
            e.HasIndex(m => new { m.LocationId, m.PerformedAt });
        });

        modelBuilder.Entity<Materials.MaterialRequisition>(e =>
        {
            e.ToTable("material_requisition", "materials");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnName("id");
            e.Property(r => r.Number).HasColumnName("number").HasMaxLength(30).IsRequired();
            e.Property(r => r.Status).HasColumnName("status").HasConversion<short>();
            e.Property(r => r.CostCenter).HasColumnName("cost_center").HasMaxLength(120).IsRequired();
            e.Property(r => r.Notes).HasColumnName("notes").HasMaxLength(1000);
            e.Property(r => r.RequesterId).HasColumnName("requester_id");
            e.Property(r => r.RequesterLabel).HasColumnName("requester_label").HasMaxLength(200);
            e.Property(r => r.FulfilledBy).HasColumnName("fulfilled_by");
            e.Property(r => r.FulfilledByLabel).HasColumnName("fulfilled_by_label").HasMaxLength(200);
            e.Property(r => r.FulfilledAt).HasColumnName("fulfilled_at");
            e.Property(r => r.CancelReason).HasColumnName("cancel_reason").HasMaxLength(500);
            e.Property(r => r.AssignedToId).HasColumnName("assigned_to_id");
            e.Property(r => r.AssignedToLabel).HasColumnName("assigned_to_label").HasMaxLength(200);
            e.Property(r => r.AssignedById).HasColumnName("assigned_by_id");
            e.Property(r => r.AssignedByLabel).HasColumnName("assigned_by_label").HasMaxLength(200);
            e.Property(r => r.AssignedAt).HasColumnName("assigned_at");
            e.Property(r => r.ApprovedById).HasColumnName("approved_by_id");
            e.Property(r => r.ApprovedByLabel).HasColumnName("approved_by_label").HasMaxLength(200);
            e.Property(r => r.ApprovedAt).HasColumnName("approved_at");
            e.Property(r => r.DecisionReason).HasColumnName("decision_reason").HasMaxLength(500);
            e.Property(r => r.PurchaseRequisitionId).HasColumnName("purchase_requisition_id");
            e.Property(r => r.PurchaseRequisitionNumber).HasColumnName("purchase_requisition_number").HasMaxLength(30);
            e.Property(r => r.CreatedAt).HasColumnName("created_at");
            e.Property(r => r.UpdatedAt).HasColumnName("updated_at");
            e.Property(r => r.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasIndex(r => r.Number).IsUnique();
            e.HasIndex(r => new { r.Status, r.CreatedAt });
            e.HasIndex(r => new { r.RequesterId, r.CreatedAt });
            e.HasMany(r => r.Items).WithOne().HasForeignKey(i => i.RequisitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Materials.MaterialRequisitionItem>(e =>
        {
            e.ToTable("material_requisition_item", "materials");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasColumnName("id");
            e.Property(i => i.RequisitionId).HasColumnName("requisition_id");
            e.Property(i => i.CatalogItemId).HasColumnName("catalog_item_id");
            e.Property(i => i.CatalogCode).HasColumnName("catalog_code").HasMaxLength(50);
            e.Property(i => i.Description).HasColumnName("description").HasMaxLength(500);
            e.Property(i => i.UnitOfMeasure).HasColumnName("unit_of_measure").HasMaxLength(10);
            e.Property(i => i.Quantity).HasColumnName("quantity").HasPrecision(18, 4);
            e.Property(i => i.ApprovedQuantity).HasColumnName("approved_quantity").HasPrecision(18, 4);
            e.Property(i => i.FulfilledQuantity).HasColumnName("fulfilled_quantity").HasPrecision(18, 4);
            e.Property(i => i.Status).HasColumnName("status").HasConversion<short>();
            e.Property(i => i.StockMovementId).HasColumnName("stock_movement_id");
            e.Property(i => i.CreatedAt).HasColumnName("created_at");
            e.Ignore(i => i.EffectiveQuantity);
            e.HasIndex(i => i.RequisitionId);
        });

        modelBuilder.Entity<PurchaseRequisition>(e =>
        {
            e.ToTable("purchase_requisition", "procurement");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnName("id");
            e.Property(r => r.Number).HasColumnName("number").HasMaxLength(30).IsRequired();
            e.Property(r => r.Kind).HasColumnName("kind").HasMaxLength(10);
            e.Property(r => r.Status).HasColumnName("status").HasConversion<short>();
            e.Property(r => r.Cycle).HasColumnName("cycle");
            e.Property(r => r.Priority).HasColumnName("priority").HasMaxLength(10);
            e.Property(r => r.UrgencyReason).HasColumnName("urgency_reason").HasMaxLength(500);
            e.Property(r => r.PriorityChangedByLabel).HasColumnName("priority_changed_by_label").HasMaxLength(200);
            e.Property(r => r.PriorityChangedAt).HasColumnName("priority_changed_at");
            e.Property(r => r.PriorityChangeReason).HasColumnName("priority_change_reason").HasMaxLength(500);
            e.Property(r => r.UrgencyImpact).HasColumnName("urgency_impact").HasMaxLength(500);
            e.Property(r => r.NeededBy).HasColumnName("needed_by");
            e.Property(r => r.Justification).HasColumnName("justification").HasMaxLength(2000).IsRequired();
            e.Property(r => r.CostCenter).HasColumnName("cost_center").HasMaxLength(120).IsRequired();
            e.Property(r => r.NeedType).HasColumnName("need_type").HasMaxLength(60);
            e.Property(r => r.DeliveryLocation).HasColumnName("delivery_location").HasMaxLength(200);
            e.Property(r => r.Company).HasColumnName("company").HasMaxLength(300);
            e.Property(r => r.Budget).HasColumnName("budget").HasPrecision(18, 4);
            e.Property(r => r.InternalNotes).HasColumnName("internal_notes").HasMaxLength(2000);
            e.Property(r => r.Currency).HasColumnName("currency").HasMaxLength(3);
            e.Property(r => r.RequesterId).HasColumnName("requester_id");
            e.Property(r => r.RequesterLabel).HasColumnName("requester_label").HasMaxLength(200);
            e.Property(r => r.DecisionReason).HasColumnName("decision_reason").HasMaxLength(1000);
            e.Property(r => r.DecidedById).HasColumnName("decided_by_id");
            e.Property(r => r.DecidedByLabel).HasColumnName("decided_by_label").HasMaxLength(200);
            e.Property(r => r.AssignedToId).HasColumnName("assigned_to_id");
            e.Property(r => r.AssignedToLabel).HasColumnName("assigned_to_label").HasMaxLength(200);
            e.Property(r => r.AssignedById).HasColumnName("assigned_by_id");
            e.Property(r => r.AssignedByLabel).HasColumnName("assigned_by_label").HasMaxLength(200);
            e.Property(r => r.AssignedAt).HasColumnName("assigned_at");
            e.Property(r => r.SubmittedAt).HasColumnName("submitted_at");
            e.Property(r => r.DecidedAt).HasColumnName("decided_at");
            e.Property(r => r.CreatedAt).HasColumnName("created_at");
            e.Property(r => r.UpdatedAt).HasColumnName("updated_at");
            e.Property(r => r.DeletedAt).HasColumnName("deleted_at");
            e.Property(r => r.DeletedBy).HasColumnName("deleted_by");
            e.Property(r => r.Version).HasColumnName("version").IsConcurrencyToken();
            e.Ignore(r => r.TotalEstimatedValue);
            e.HasIndex(r => r.Number).IsUnique();
            e.HasIndex(r => new { r.Status, r.SubmittedAt });
            e.HasIndex(r => new { r.RequesterId, r.CreatedAt });
            e.HasMany(r => r.Items).WithOne().HasForeignKey(i => i.RequisitionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(r => r.Attachments).WithOne().HasForeignKey(a => a.RequisitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RequisitionItem>(e =>
        {
            e.ToTable("purchase_requisition_item", "procurement");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasColumnName("id");
            e.Property(i => i.RequisitionId).HasColumnName("requisition_id");
            e.Property(i => i.CatalogItemId).HasColumnName("catalog_item_id");
            e.Property(i => i.CatalogCode).HasColumnName("catalog_code").HasMaxLength(50);
            e.Property(i => i.Family).HasColumnName("family").HasMaxLength(120);
            e.Property(i => i.Sequence).HasColumnName("sequence");
            e.Property(i => i.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
            e.Property(i => i.Quantity).HasColumnName("quantity").HasPrecision(18, 4);
            e.Property(i => i.UnitOfMeasure).HasColumnName("unit_of_measure").HasMaxLength(10);
            e.Property(i => i.EstimatedUnitPrice).HasColumnName("estimated_unit_price").HasPrecision(18, 4);
            e.Property(i => i.Notes).HasColumnName("notes").HasMaxLength(500);
            e.Property(i => i.CreatedAt).HasColumnName("created_at");
            e.HasIndex(i => i.RequisitionId);
        });

        modelBuilder.Entity<RequisitionAttachment>(e =>
        {
            e.ToTable("purchase_requisition_attachment", "procurement");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.RequisitionId).HasColumnName("requisition_id");
            e.Property(a => a.DocumentId).HasColumnName("document_id");
            e.Property(a => a.FileName).HasColumnName("file_name").HasMaxLength(260).IsRequired();
            e.Property(a => a.ContentType).HasColumnName("content_type").HasMaxLength(120);
            e.Property(a => a.SizeBytes).HasColumnName("size_bytes");
            e.Property(a => a.UploadedBy).HasColumnName("uploaded_by");
            e.Property(a => a.UploadedByLabel).HasColumnName("uploaded_by_label").HasMaxLength(200);
            e.Property(a => a.UploadedAt).HasColumnName("uploaded_at");
            e.HasIndex(a => a.RequisitionId);
        });

        modelBuilder.Entity<Domain.CostCenterApprover>(e =>
        {
            e.ToTable("cost_center_approver");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.CostCenterId).HasColumnName("cost_center_id");
            e.Property(a => a.UserId).HasColumnName("user_id");
            e.Property(a => a.UserName).HasColumnName("user_name").HasMaxLength(200);
            e.Property(a => a.Level).HasColumnName("level");
            e.Property(a => a.CreatedAt).HasColumnName("created_at");
            e.HasIndex(a => new { a.CostCenterId, a.Level });
        });

        modelBuilder.Entity<CostCenter>(e =>
        {
            e.ToTable("cost_center"); // schema foundation (master data mínimo)
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasColumnName("id");
            e.Property(c => c.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            e.Property(c => c.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(c => c.Region).HasColumnName("region").HasMaxLength(120);
            e.Property(c => c.CompanyId).HasColumnName("company_id");
            e.Property(c => c.ManagerUserId).HasColumnName("manager_user_id");
            e.Property(c => c.ManagerName).HasColumnName("manager_name").HasMaxLength(200);
            e.Property(c => c.Level1ValueLimit).HasColumnName("level1_value_limit").HasPrecision(18, 4);
            e.Property(c => c.Level2ValueLimit).HasColumnName("level2_value_limit").HasPrecision(18, 4);
            e.Property(c => c.ClientName).HasColumnName("client_name").HasMaxLength(200);
            e.Property(c => c.Active).HasColumnName("active");
            e.Property(c => c.CreatedAt).HasColumnName("created_at");
            e.Property(c => c.UpdatedAt).HasColumnName("updated_at");
            e.Property(c => c.CreatedBy).HasColumnName("created_by");
            e.Property(c => c.Version).HasColumnName("version");
            e.HasIndex(c => c.Code).IsUnique();
            e.HasMany(c => c.Approvers).WithOne().HasForeignKey(a => a.CostCenterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Supplier>(e =>
        {
            e.ToTable("supplier", "procurement"); // SUP-001 MVP
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasColumnName("id");
            e.Property(s => s.LegalName).HasColumnName("legal_name").HasMaxLength(300).IsRequired();
            e.Property(s => s.TradeName).HasColumnName("trade_name").HasMaxLength(300);
            // opcional desde o pré-cadastro (§7): o índice único do Postgres ignora
            // nulos, então vários fornecedores sem CNPJ convivem sem colidir
            e.Property(s => s.TaxId).HasColumnName("tax_id").HasMaxLength(14);
            e.Property(s => s.Email).HasColumnName("email").HasMaxLength(320);
            e.Property(s => s.Phone).HasColumnName("phone").HasMaxLength(40);
            e.Property(s => s.PortalKeyHash).HasColumnName("portal_key_hash").HasMaxLength(64);
            e.Property(s => s.HomologationStatus).HasColumnName("homologation_status")
                .HasMaxLength(20).HasDefaultValue(SupplierHomologation.Homologado);
            e.HasMany(s => s.Documents).WithOne().HasForeignKey(d => d.SupplierId).OnDelete(DeleteBehavior.Cascade);
            e.Property(s => s.Active).HasColumnName("active");
            e.Property(s => s.CreatedAt).HasColumnName("created_at");
            e.Property(s => s.UpdatedAt).HasColumnName("updated_at");
            e.Property(s => s.CreatedBy).HasColumnName("created_by");
            e.Property(s => s.Version).HasColumnName("version");
            e.Property(s => s.ContractNumber).HasColumnName("contract_number").HasMaxLength(60);
            e.Property(s => s.ContractValueLimit).HasColumnName("contract_value_limit").HasPrecision(18, 4);
            e.Ignore(s => s.ContractConsumed);
            e.Property(s => s.ContractValidFrom).HasColumnName("contract_valid_from");
            e.Property(s => s.ContractValidUntil).HasColumnName("contract_valid_until");
            e.Property(s => s.ContractNotes).HasColumnName("contract_notes").HasMaxLength(500);
            e.HasIndex(s => s.TaxId).IsUnique(); // SUP-BR-001
            e.HasIndex(s => new { s.Active, s.LegalName });
            e.HasMany(s => s.ContractItems).WithOne().HasForeignKey(i => i.SupplierId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SupplierDocument>(e =>
        {
            e.ToTable("supplier_document", "procurement"); // certidões com validade (V2-P2)
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).HasColumnName("id");
            e.Property(d => d.SupplierId).HasColumnName("supplier_id");
            e.Property(d => d.Type).HasColumnName("type").HasMaxLength(30);
            e.Property(d => d.Label).HasColumnName("label").HasMaxLength(200);
            e.Property(d => d.DocumentId).HasColumnName("document_id");
            e.Property(d => d.FileName).HasColumnName("file_name").HasMaxLength(300);
            e.Property(d => d.ValidUntil).HasColumnName("valid_until");
            e.Property(d => d.UploadedByLabel).HasColumnName("uploaded_by_label").HasMaxLength(200);
            e.Property(d => d.CreatedAt).HasColumnName("created_at");
            e.HasIndex(d => new { d.SupplierId, d.Type });
            e.HasIndex(d => d.ValidUntil);
        });

        modelBuilder.Entity<SupplierContractItem>(e =>
        {
            e.ToTable("supplier_contract_item", "procurement");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasColumnName("id");
            e.Property(i => i.SupplierId).HasColumnName("supplier_id");
            e.Property(i => i.CatalogItemId).HasColumnName("catalog_item_id");
            e.Property(i => i.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
            e.Property(i => i.CatalogCode).HasColumnName("catalog_code").HasMaxLength(50);
            e.Property(i => i.UnitOfMeasure).HasColumnName("unit_of_measure").HasMaxLength(10);
            e.Property(i => i.UnitPrice).HasColumnName("unit_price").HasPrecision(18, 4);
            e.Property(i => i.PaymentTerms).HasColumnName("payment_terms").HasMaxLength(200);
            e.Property(i => i.PaymentDays).HasColumnName("payment_days");
            e.Property(i => i.DeliveryDays).HasColumnName("delivery_days");
            e.Property(i => i.Notes).HasColumnName("notes").HasMaxLength(500);
            e.Property(i => i.CreatedAt).HasColumnName("created_at");
            e.HasIndex(i => new { i.SupplierId, i.CatalogItemId });
        });

        modelBuilder.Entity<PaymentMethod>(e =>
        {
            e.ToTable("payment_method", "procurement"); // forma: por onde o dinheiro sai
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasColumnName("id");
            e.Property(m => m.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
            e.Property(m => m.Active).HasColumnName("active");
            e.Property(m => m.CreatedAt).HasColumnName("created_at");
            e.Property(m => m.UpdatedAt).HasColumnName("updated_at");
            // o nome é a identidade do cadastro: é por ele que a proposta antiga
            // continua legível, e é ele que não pode repetir
            e.HasIndex(m => m.Name).IsUnique();
        });

        modelBuilder.Entity<ScoreWeights>(e =>
        {
            // uma linha só: a régua com que se compara proposta é da empresa, não do processo
            e.ToTable("score_weights", "procurement");
            e.HasKey(w => w.Id);
            e.Property(w => w.Id).HasColumnName("id");
            e.Property(w => w.Price).HasColumnName("price");
            e.Property(w => w.Delivery).HasColumnName("delivery");
            e.Property(w => w.Payment).HasColumnName("payment");
            e.Property(w => w.Otif).HasColumnName("otif");
            e.Property(w => w.Risk).HasColumnName("risk");
            e.Property(w => w.UpdatedAt).HasColumnName("updated_at");
            e.Property(w => w.UpdatedByLabel).HasColumnName("updated_by_label").HasMaxLength(200);
        });

        modelBuilder.Entity<PaymentTermOption>(e =>
        {
            e.ToTable("payment_term_option", "procurement"); // condição: quando se paga
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasColumnName("id");
            e.Property(c => c.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
            e.Property(c => c.Installments).HasColumnName("installments");
            e.Property(c => c.FirstDueDays).HasColumnName("first_due_days");
            e.Property(c => c.IsDefault).HasColumnName("is_default");
            e.Property(c => c.Active).HasColumnName("active");
            e.Property(c => c.CreatedAt).HasColumnName("created_at");
            e.Property(c => c.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(c => c.Name).IsUnique();
        });

        modelBuilder.Entity<ContractAdjustment>(e =>
        {
            e.ToTable("contract_adjustment", "procurement"); // pleitos de reajuste (V2-P4 — cost avoidance)
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.SupplierId).HasColumnName("supplier_id");
            e.Property(a => a.RequestedPercent).HasColumnName("requested_percent").HasPrecision(9, 4);
            e.Property(a => a.AgreedPercent).HasColumnName("agreed_percent").HasPrecision(9, 4);
            e.Property(a => a.BaseValue).HasColumnName("base_value").HasPrecision(18, 4);
            e.Property(a => a.CostAvoidance).HasColumnName("cost_avoidance").HasPrecision(18, 4);
            e.Property(a => a.AppliedToPrices).HasColumnName("applied_to_prices");
            e.Property(a => a.Notes).HasColumnName("notes").HasMaxLength(500);
            e.Property(a => a.CreatedBy).HasColumnName("created_by");
            e.Property(a => a.CreatedByLabel).HasColumnName("created_by_label").HasMaxLength(200);
            e.Property(a => a.CreatedAt).HasColumnName("created_at");
            e.HasIndex(a => a.SupplierId);
            e.HasIndex(a => a.CreatedAt);
        });

        modelBuilder.Entity<PurchaseOrder>(e =>
        {
            e.ToTable("purchase_order", "procurement"); // PO-001 MVP
            e.HasKey(o => o.Id);
            e.Property(o => o.Id).HasColumnName("id");
            e.Property(o => o.Number).HasColumnName("number").HasMaxLength(30).IsRequired();
            e.Property(o => o.Status).HasColumnName("status").HasConversion<short>();
            e.Property(o => o.SupplierId).HasColumnName("supplier_id");
            e.Property(o => o.SupplierName).HasColumnName("supplier_name").HasMaxLength(300);
            e.Property(o => o.SourcePrId).HasColumnName("source_pr_id");
            e.Property(o => o.SourcePrNumber).HasColumnName("source_pr_number").HasMaxLength(30);
            e.Property(o => o.QuotationId).HasColumnName("quotation_id");
            e.Property(o => o.QuotationNumber).HasColumnName("quotation_number").HasMaxLength(30);
            e.Property(o => o.Families).HasColumnName("families").HasMaxLength(500);
            e.Property(o => o.PaymentTerms).HasColumnName("payment_terms").HasMaxLength(200);
            e.Property(o => o.DeliveryDays).HasColumnName("delivery_days");
            e.Property(o => o.FreightValue).HasColumnName("freight_value").HasPrecision(18, 4);
            e.Property(o => o.PdfDocumentId).HasColumnName("pdf_document_id");
            e.Property(o => o.Notes).HasColumnName("notes").HasMaxLength(1000);
            e.Property(o => o.TotalValue).HasColumnName("total_value").HasPrecision(18, 4);
            e.Property(o => o.IssuedBy).HasColumnName("issued_by");
            e.Property(o => o.IssuedByLabel).HasColumnName("issued_by_label").HasMaxLength(200);
            e.Property(o => o.ReceivedBy).HasColumnName("received_by");
            e.Property(o => o.ReceivedByLabel).HasColumnName("received_by_label").HasMaxLength(200);
            e.Property(o => o.ReceivedAt).HasColumnName("received_at");
            e.Property(o => o.CancelReason).HasColumnName("cancel_reason").HasMaxLength(500);
            e.Property(o => o.ErpNumber).HasColumnName("erp_number").HasMaxLength(60);
            e.Property(o => o.ErpIssuedOn).HasColumnName("erp_issued_on");
            e.Property(o => o.PromisedDate).HasColumnName("promised_date");
            e.Ignore(o => o.OnTime); e.Ignore(o => o.InFull); e.Ignore(o => o.Otif);
            e.Property(o => o.ErpDocumentId).HasColumnName("erp_document_id");
            e.Property(o => o.ErpFileName).HasColumnName("erp_file_name").HasMaxLength(260);
            e.Property(o => o.NoErpReason).HasColumnName("no_erp_reason").HasMaxLength(500);
            e.Property(o => o.DeliveryCompletedAt).HasColumnName("delivery_completed_at");
            e.Ignore(o => o.HasPendingDelivery);
            e.Property(o => o.CreatedAt).HasColumnName("created_at");
            e.Property(o => o.UpdatedAt).HasColumnName("updated_at");
            e.Property(o => o.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasIndex(o => o.Number).IsUnique();
            e.HasIndex(o => new { o.Status, o.CreatedAt });
            e.HasIndex(o => o.SourcePrId);
            e.HasOne<Supplier>().WithMany().HasForeignKey(o => o.SupplierId);
            e.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(o => o.Invoices).WithOne().HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PurchaseOrderInvoice>(e =>
        {
            e.ToTable("purchase_order_invoice", "procurement");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasColumnName("id");
            e.Property(i => i.OrderId).HasColumnName("order_id");
            e.Property(i => i.Number).HasColumnName("number").HasMaxLength(60).IsRequired();
            e.Property(i => i.IssuedOn).HasColumnName("issued_on");
            e.Property(i => i.Value).HasColumnName("value").HasPrecision(18, 4);
            e.Property(i => i.DocumentId).HasColumnName("document_id");
            e.Property(i => i.FileName).HasColumnName("file_name").HasMaxLength(260);
            e.Property(i => i.CreatedBy).HasColumnName("created_by");
            e.Property(i => i.CreatedByLabel).HasColumnName("created_by_label").HasMaxLength(200);
            e.Property(i => i.CreatedAt).HasColumnName("created_at");
            e.HasIndex(i => i.OrderId);
        });

        modelBuilder.Entity<PurchaseOrderItem>(e =>
        {
            e.ToTable("purchase_order_item", "procurement");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasColumnName("id");
            e.Property(i => i.OrderId).HasColumnName("order_id");
            e.Property(i => i.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
            e.Property(i => i.UnitOfMeasure).HasColumnName("unit_of_measure").HasMaxLength(10);
            e.Property(i => i.Quantity).HasColumnName("quantity").HasPrecision(18, 4);
            e.Property(i => i.UnitPrice).HasColumnName("unit_price").HasPrecision(18, 4);
            e.Property(i => i.CatalogItemId).HasColumnName("catalog_item_id");
            e.Property(i => i.CatalogCode).HasColumnName("catalog_code").HasMaxLength(50);
            e.Property(i => i.ReceivedQuantity).HasColumnName("received_quantity").HasPrecision(18, 4);
            e.Property(i => i.RejectedQuantity).HasColumnName("rejected_quantity").HasPrecision(18, 4).HasDefaultValue(0m);
            e.Property(i => i.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(300);
            e.Property(i => i.LastPaidUnitPrice).HasColumnName("last_paid_unit_price").HasPrecision(18, 4);
            e.Property(i => i.ReferenceSaving).HasColumnName("reference_saving").HasPrecision(18, 4);
            e.Property(i => i.SourcePrNumber).HasColumnName("source_pr_number").HasMaxLength(30);
            e.Property(i => i.Family).HasColumnName("family").HasMaxLength(120);
            e.Property(i => i.CreatedAt).HasColumnName("created_at");
            e.HasIndex(i => i.OrderId);
        });

        modelBuilder.Entity<Quotation>(e =>
        {
            e.ToTable("quotation", "procurement"); // RFQ-001
            e.HasKey(q => q.Id);
            e.Property(q => q.Id).HasColumnName("id");
            e.Property(q => q.Number).HasColumnName("number").HasMaxLength(30).IsRequired();
            e.Property(q => q.Kind).HasColumnName("kind").HasConversion<short>();
            e.Property(q => q.Status).HasColumnName("status").HasConversion<short>();
            e.Property(q => q.SourcePrId).HasColumnName("source_pr_id");
            e.Property(q => q.SourcePrNumber).HasColumnName("source_pr_number").HasMaxLength(30);
            e.Property(q => q.CostCenter).HasColumnName("cost_center").HasMaxLength(120);
            e.Property(q => q.Justification).HasColumnName("justification").HasMaxLength(2000);
            e.Property(q => q.Deadline).HasColumnName("deadline");
            e.Property(q => q.Notes).HasColumnName("notes").HasMaxLength(1000);
            e.Property(q => q.WinnerSupplierId).HasColumnName("winner_supplier_id");
            e.Property(q => q.WinnerProposalId).HasColumnName("winner_proposal_id");
            e.Property(q => q.SelectionCriteria).HasColumnName("selection_criteria").HasMaxLength(500);
            e.Property(q => q.SelectionJustification).HasColumnName("selection_justification").HasMaxLength(2000);
            e.Property(q => q.SelectedBy).HasColumnName("selected_by");
            e.Property(q => q.SelectedByLabel).HasColumnName("selected_by_label").HasMaxLength(200);
            e.Property(q => q.SelectedAt).HasColumnName("selected_at");
            e.Property(q => q.ManagerApprovedBy).HasColumnName("manager_approved_by");
            e.Property(q => q.ManagerApprovedByLabel).HasColumnName("manager_approved_by_label").HasMaxLength(200);
            e.Property(q => q.ManagerApprovedAt).HasColumnName("manager_approved_at");
            e.Property(q => q.DirectorApprovedBy).HasColumnName("director_approved_by");
            e.Property(q => q.DirectorApprovedByLabel).HasColumnName("director_approved_by_label").HasMaxLength(200);
            e.Property(q => q.DirectorApprovedAt).HasColumnName("director_approved_at");
            e.Property(q => q.DecisionReason).HasColumnName("decision_reason").HasMaxLength(1000);
            e.Property(q => q.PurchaseOrderId).HasColumnName("purchase_order_id");
            e.Property(q => q.PurchaseOrderNumber).HasColumnName("purchase_order_number").HasMaxLength(30);
            e.Property(q => q.CreatedBy).HasColumnName("created_by");
            e.Property(q => q.CreatedByLabel).HasColumnName("created_by_label").HasMaxLength(200);
            e.Property(q => q.CreatedAt).HasColumnName("created_at");
            e.Property(q => q.UpdatedAt).HasColumnName("updated_at");
            e.Property(q => q.Version).HasColumnName("version").IsConcurrencyToken();
            e.Property(q => q.BaselineValue).HasColumnName("baseline_value").HasPrecision(18, 4);
            e.Property(q => q.NegotiatedValue).HasColumnName("negotiated_value").HasPrecision(18, 4);
            e.Property(q => q.SavingValue).HasColumnName("saving_value").HasPrecision(18, 4);
            e.Property(q => q.SavingPercent).HasColumnName("saving_percent").HasPrecision(9, 4);
            e.Property(q => q.CompetitionBaselineValue).HasColumnName("competition_baseline_value").HasPrecision(18, 4);
            e.Property(q => q.CompetitionSaving).HasColumnName("competition_saving").HasPrecision(18, 4);
            e.Property(q => q.BudgetBaselineValue).HasColumnName("budget_baseline_value").HasPrecision(18, 4);
            e.Property(q => q.BudgetSaving).HasColumnName("budget_saving").HasPrecision(18, 4);
            e.Property(q => q.NegotiationNotes).HasColumnName("negotiation_notes").HasMaxLength(1000);
            e.Property(q => q.NegotiatedByLabel).HasColumnName("negotiated_by_label").HasMaxLength(200);
            e.Property(q => q.NegotiatedAt).HasColumnName("negotiated_at");
            e.HasIndex(q => q.Number).IsUnique(); // RFQ-BR-002
            e.HasIndex(q => new { q.Status, q.CreatedAt });
            e.HasIndex(q => q.SourcePrId);
            e.HasMany(q => q.Items).WithOne().HasForeignKey(i => i.QuotationId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(q => q.Suppliers).WithOne().HasForeignKey(s => s.QuotationId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(q => q.Proposals).WithOne().HasForeignKey(p => p.QuotationId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(q => q.Awards).WithOne().HasForeignKey(a => a.QuotationId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(q => q.AwardList);   // leitura sem repetição da coleção acima, não é navegação
        });

        modelBuilder.Entity<QuotationAward>(e =>
        {
            e.ToTable("quotation_award", "procurement");   // adjudicação por família (multi-fornecedor)
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.QuotationId).HasColumnName("quotation_id");
            e.Property(a => a.Family).HasColumnName("family").HasMaxLength(120).IsRequired();
            e.Property(a => a.QuotationItemId).HasColumnName("quotation_item_id");
            e.Property(a => a.SupplierId).HasColumnName("supplier_id");
            e.Property(a => a.SupplierName).HasColumnName("supplier_name").HasMaxLength(300);
            e.Property(a => a.ProposalId).HasColumnName("proposal_id");
            e.Property(a => a.ProposalVersion).HasColumnName("proposal_version");
            e.Property(a => a.ItemsValue).HasColumnName("items_value").HasPrecision(18, 4);
            e.Property(a => a.TotalValue).HasColumnName("total_value").HasPrecision(18, 4);
            e.Property(a => a.Criteria).HasColumnName("criteria").HasMaxLength(500);
            e.Property(a => a.Justification).HasColumnName("justification").HasMaxLength(2000);
            e.Property(a => a.SelectedBy).HasColumnName("selected_by");
            e.Property(a => a.SelectedByLabel).HasColumnName("selected_by_label").HasMaxLength(200);
            e.Property(a => a.SelectedAt).HasColumnName("selected_at");
            e.Property(a => a.PurchaseOrderId).HasColumnName("purchase_order_id");
            e.Property(a => a.PurchaseOrderNumber).HasColumnName("purchase_order_number").HasMaxLength(30);
            // Um escopo é adjudicado uma vez só por processo. O escopo passou a ser
            // (família, item), porque a mesma família agora se divide entre fornecedores —
            // com `quotation_item_id` nulo significando a família inteira. Os nulos entram
            // na comparação (`NULLS NOT DISTINCT`): sem isso o banco deixaria passar duas
            // adjudicações da mesma família inteira, que é exatamente o que este índice
            // impedia antes.
            e.HasIndex(a => new { a.QuotationId, a.Family, a.QuotationItemId })
                .IsUnique().AreNullsDistinct(false);
        });

        modelBuilder.Entity<QuotationItem>(e =>
        {
            e.ToTable("quotation_item", "procurement");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasColumnName("id");
            e.Property(i => i.QuotationId).HasColumnName("quotation_id");
            e.Property(i => i.Sequence).HasColumnName("sequence");
            e.Property(i => i.CatalogItemId).HasColumnName("catalog_item_id");
            e.Property(i => i.CatalogCode).HasColumnName("catalog_code").HasMaxLength(50);
            e.Property(i => i.Description).HasColumnName("description").HasMaxLength(500);
            e.Property(i => i.Quantity).HasColumnName("quantity").HasPrecision(18, 4);
            e.Property(i => i.UnitOfMeasure).HasColumnName("unit_of_measure").HasMaxLength(10);
            e.Property(i => i.Family).HasColumnName("family").HasMaxLength(120);
            e.Property(i => i.SourcePrId).HasColumnName("source_pr_id");
            e.Property(i => i.SourcePrNumber).HasColumnName("source_pr_number").HasMaxLength(30);
            e.Property(i => i.SourcePrItemId).HasColumnName("source_pr_item_id");
            e.HasIndex(i => i.QuotationId);
            e.HasIndex(i => i.SourcePrId);
        });

        modelBuilder.Entity<QuotationSupplier>(e =>
        {
            e.ToTable("quotation_supplier", "procurement");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasColumnName("id");
            e.Property(s => s.QuotationId).HasColumnName("quotation_id");
            e.Property(s => s.SupplierId).HasColumnName("supplier_id");
            e.Property(s => s.SupplierName).HasColumnName("supplier_name").HasMaxLength(300);
            e.Property(s => s.TaxId).HasColumnName("tax_id").HasMaxLength(14);
            e.Property(s => s.InvitedBy).HasColumnName("invited_by");
            e.Property(s => s.InvitedByLabel).HasColumnName("invited_by_label").HasMaxLength(200);
            e.Property(s => s.InvitedAt).HasColumnName("invited_at");
            e.Property(s => s.ResponseDeadline).HasColumnName("response_deadline");
            e.Property(s => s.DeadlineExtensions).HasColumnName("deadline_extensions");
            e.Property(s => s.WaivedAt).HasColumnName("waived_at");
            e.Property(s => s.WaivedByLabel).HasColumnName("waived_by_label").HasMaxLength(200);
            e.Property(s => s.WaivedReason).HasColumnName("waived_reason").HasMaxLength(500);
            e.HasIndex(s => new { s.QuotationId, s.SupplierId }).IsUnique();
        });

        modelBuilder.Entity<Proposal>(e =>
        {
            e.ToTable("proposal", "procurement"); // imutável e versionada (RFQ-BR-004)
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.QuotationId).HasColumnName("quotation_id");
            e.Property(p => p.SupplierId).HasColumnName("supplier_id");
            e.Property(p => p.SupplierName).HasColumnName("supplier_name").HasMaxLength(300);
            e.Property(p => p.VersionNumber).HasColumnName("version_number");
            e.Property(p => p.TotalValue).HasColumnName("total_value").HasPrecision(18, 4);
            e.Property(p => p.DeliveryDays).HasColumnName("delivery_days");
            e.Property(p => p.PaymentTerms).HasColumnName("payment_terms").HasMaxLength(200);
            e.Property(p => p.PaymentMethodName).HasColumnName("payment_method_name").HasMaxLength(80);
            e.Property(p => p.PaymentDays).HasColumnName("payment_days");
            e.Property(p => p.FreightValue).HasColumnName("freight_value").HasPrecision(18, 4);
            e.Property(p => p.TaxValue).HasColumnName("tax_value").HasPrecision(18, 4);
            e.Property(p => p.OtherCosts).HasColumnName("other_costs").HasPrecision(18, 4);
            e.Property(p => p.DiscountValue).HasColumnName("discount_value").HasPrecision(18, 4);
            e.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3);
            e.Property(p => p.ValidUntil).HasColumnName("valid_until");
            e.Property(p => p.Notes).HasColumnName("notes").HasMaxLength(1000);
            e.Property(p => p.SubmittedVia).HasColumnName("submitted_via").HasMaxLength(10);
            e.Property(p => p.SubmittedByLabel).HasColumnName("submitted_by_label").HasMaxLength(200);
            e.Property(p => p.SubmittedAt).HasColumnName("submitted_at");
            e.Property(p => p.AttachmentDocumentId).HasColumnName("attachment_document_id");
            e.Property(p => p.AttachmentFileName).HasColumnName("attachment_file_name").HasMaxLength(300);
            e.HasIndex(p => new { p.QuotationId, p.SupplierId, p.VersionNumber }).IsUnique();
            e.HasMany(p => p.Items).WithOne().HasForeignKey(i => i.ProposalId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProposalItem>(e =>
        {
            e.ToTable("proposal_item", "procurement");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasColumnName("id");
            e.Property(i => i.ProposalId).HasColumnName("proposal_id");
            e.Property(i => i.QuotationItemId).HasColumnName("quotation_item_id");
            e.Property(i => i.UnitPrice).HasColumnName("unit_price").HasPrecision(18, 4);
            e.Property(i => i.Quantity).HasColumnName("quantity").HasPrecision(18, 4);
            e.HasIndex(i => i.ProposalId);
        });

        modelBuilder.Entity<ProcessEvent>(e =>
        {
            e.ToTable("process_event", "procurement"); // timeline imutável (RFQ-BR-009)
            e.HasKey(v => v.Id);
            e.Property(v => v.Id).HasColumnName("id");
            e.Property(v => v.QuotationId).HasColumnName("quotation_id");
            e.Property(v => v.EventType).HasColumnName("event_type").HasMaxLength(50);
            e.Property(v => v.Description).HasColumnName("description").HasMaxLength(600);
            e.Property(v => v.FromStatus).HasColumnName("from_status").HasConversion<short?>();
            e.Property(v => v.ToStatus).HasColumnName("to_status").HasConversion<short?>();
            e.Property(v => v.ActorId).HasColumnName("actor_id");
            e.Property(v => v.ActorLabel).HasColumnName("actor_label").HasMaxLength(200);
            e.Property(v => v.Note).HasColumnName("note").HasMaxLength(1000);
            e.Property(v => v.DocumentId).HasColumnName("document_id");
            e.Property(v => v.OccurredAt).HasColumnName("occurred_at");
            e.HasIndex(v => new { v.QuotationId, v.OccurredAt });
        });

        modelBuilder.Entity<StoredDocument>(e =>
        {
            e.ToTable("stored_document"); // schema foundation — infra documental mínima
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).HasColumnName("id");
            e.Property(d => d.FileName).HasColumnName("file_name").HasMaxLength(300).IsRequired();
            e.Property(d => d.ContentType).HasColumnName("content_type").HasMaxLength(120);
            e.Property(d => d.SizeBytes).HasColumnName("size_bytes");
            e.Property(d => d.Content).HasColumnName("content");
            e.Property(d => d.EntityType).HasColumnName("entity_type").HasMaxLength(40);
            e.Property(d => d.EntityId).HasColumnName("entity_id");
            e.Property(d => d.SupplierId).HasColumnName("supplier_id");
            e.Property(d => d.UploadedByLabel).HasColumnName("uploaded_by_label").HasMaxLength(200);
            e.Property(d => d.UploadedAt).HasColumnName("uploaded_at");
            e.HasIndex(d => new { d.EntityType, d.EntityId });
        });

        modelBuilder.Entity<Company>(e =>
        {
            e.ToTable("company"); // schema foundation — CNPJs do grupo
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasColumnName("id");
            e.Property(c => c.LegalName).HasColumnName("legal_name").HasMaxLength(300).IsRequired();
            e.Property(c => c.TaxId).HasColumnName("tax_id").HasMaxLength(14).IsRequired();
            e.Property(c => c.StateRegistration).HasColumnName("state_registration").HasMaxLength(30);
            e.Property(c => c.Address).HasColumnName("address").HasMaxLength(300);
            e.Property(c => c.District).HasColumnName("district").HasMaxLength(200);
            e.Property(c => c.City).HasColumnName("city").HasMaxLength(120);
            e.Property(c => c.State).HasColumnName("state").HasMaxLength(2);
            e.Property(c => c.Zip).HasColumnName("zip").HasMaxLength(10);
            e.Property(c => c.Phone).HasColumnName("phone").HasMaxLength(40);
            e.Property(c => c.Email).HasColumnName("email").HasMaxLength(320);
            e.Property(c => c.Active).HasColumnName("active");
            e.Property(c => c.CreatedAt).HasColumnName("created_at");
            e.Property(c => c.UpdatedAt).HasColumnName("updated_at");
            e.Property(c => c.Version).HasColumnName("version");
            e.HasIndex(c => c.TaxId).IsUnique();
        });

        modelBuilder.Entity<CompanyProfile>(e =>
        {
            e.ToTable("company_profile"); // schema foundation — cabeçalho da OC
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasColumnName("id");
            e.Property(c => c.LegalName).HasColumnName("legal_name").HasMaxLength(300);
            e.Property(c => c.Address).HasColumnName("address").HasMaxLength(300);
            e.Property(c => c.District).HasColumnName("district").HasMaxLength(200);
            e.Property(c => c.City).HasColumnName("city").HasMaxLength(120);
            e.Property(c => c.State).HasColumnName("state").HasMaxLength(2);
            e.Property(c => c.Zip).HasColumnName("zip").HasMaxLength(10);
            e.Property(c => c.TaxId).HasColumnName("tax_id").HasMaxLength(20);
            e.Property(c => c.StateRegistration).HasColumnName("state_registration").HasMaxLength(30);
            e.Property(c => c.Phone).HasColumnName("phone").HasMaxLength(40);
            e.Property(c => c.Email).HasColumnName("email").HasMaxLength(320);
            e.Property(c => c.DeliveryAddress).HasColumnName("delivery_address").HasMaxLength(400);
            e.Property(c => c.DeliveryTaxId).HasColumnName("delivery_tax_id").HasMaxLength(20);
            e.Property(c => c.StandardClauses).HasColumnName("standard_clauses").HasMaxLength(2000);
            e.Property(c => c.PaymentPolicy).HasColumnName("payment_policy").HasMaxLength(8000);
            e.Property(c => c.UpdatedAt).HasColumnName("updated_at");
            e.Property(c => c.UpdatedByLabel).HasColumnName("updated_by_label").HasMaxLength(200);
        });

        modelBuilder.Entity<Announcement>(e =>
        {
            e.ToTable("announcement"); // schema foundation — recado do administrador
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            e.Property(a => a.Body).HasColumnName("body").HasMaxLength(8000);
            e.Property(a => a.ImageDocumentId).HasColumnName("image_document_id");
            e.Property(a => a.ImageFileName).HasColumnName("image_file_name").HasMaxLength(260);
            e.Property(a => a.StartsOn).HasColumnName("starts_on");
            e.Property(a => a.EndsOn).HasColumnName("ends_on");
            e.Property(a => a.Active).HasColumnName("active");
            e.Property(a => a.CreatedBy).HasColumnName("created_by");
            e.Property(a => a.CreatedByLabel).HasColumnName("created_by_label").HasMaxLength(200);
            e.Property(a => a.CreatedAt).HasColumnName("created_at");
            e.Property(a => a.UpdatedAt).HasColumnName("updated_at");
            // a consulta de todo login é "o que está no ar hoje": ligado, dentro da vigência
            e.HasIndex(a => new { a.Active, a.StartsOn, a.EndsOn });
            e.HasMany(a => a.Dismissals).WithOne().HasForeignKey(d => d.AnnouncementId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AnnouncementDismissal>(e =>
        {
            e.ToTable("announcement_dismissal");
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).HasColumnName("id");
            e.Property(d => d.AnnouncementId).HasColumnName("announcement_id");
            e.Property(d => d.UserId).HasColumnName("user_id");
            e.Property(d => d.DismissedAt).HasColumnName("dismissed_at");
            // uma leitura por pessoa por comunicado: fechar duas vezes não cria dois registros
            e.HasIndex(d => new { d.AnnouncementId, d.UserId }).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_token");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasColumnName("id");
            e.Property(t => t.UserId).HasColumnName("user_id");
            e.Property(t => t.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
            e.Property(t => t.ExpiresAt).HasColumnName("expires_at");
            e.Property(t => t.CreatedAt).HasColumnName("created_at");
            e.Property(t => t.RevokedAt).HasColumnName("revoked_at");
            e.Property(t => t.ReplacedById).HasColumnName("replaced_by_id");
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(t => t.UserId);
        });
    }
}
