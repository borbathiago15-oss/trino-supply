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
    public DbSet<Inventory.StorageLocation> StorageLocations => Set<Inventory.StorageLocation>();
    public DbSet<Inventory.StockBalance> StockBalances => Set<Inventory.StockBalance>();
    public DbSet<Inventory.StockMovement> StockMovements => Set<Inventory.StockMovement>();
    public DbSet<Materials.MaterialRequisition> MaterialRequisitions => Set<Materials.MaterialRequisition>();
    public DbSet<Materials.MaterialRequisitionItem> MaterialRequisitionItems => Set<Materials.MaterialRequisitionItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("foundation");
        modelBuilder.HasSequence<long>("pr_number_seq", "procurement").StartsAt(1);  // PR-001-11 (numeração)
        modelBuilder.HasSequence<long>("mov_number_seq", "materials").StartsAt(1);   // MMS-004 (movimentações)
        modelBuilder.HasSequence<long>("mr_number_seq", "materials").StartsAt(1);    // MMS-003 (solicitações)

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("app_user");
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).HasColumnName("id");
            e.Property(u => u.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
            e.Property(u => u.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(u => u.PasswordHash).HasColumnName("password_hash").IsRequired();
            e.Property(u => u.Role).HasColumnName("role").HasMaxLength(50).IsRequired();
            e.Property(u => u.Active).HasColumnName("active");
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
            e.Property(i => i.Active).HasColumnName("active");
            e.Property(i => i.CreatedAt).HasColumnName("created_at");
            e.Property(i => i.UpdatedAt).HasColumnName("updated_at");
            e.Property(i => i.CreatedBy).HasColumnName("created_by");
            e.Property(i => i.Version).HasColumnName("version");
            e.HasIndex(i => i.Code).IsUnique();
            e.HasIndex(i => new { i.Family, i.Active });
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
            e.Property(i => i.Status).HasColumnName("status").HasConversion<short>();
            e.Property(i => i.StockMovementId).HasColumnName("stock_movement_id");
            e.Property(i => i.CreatedAt).HasColumnName("created_at");
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
            e.Property(r => r.NeededBy).HasColumnName("needed_by");
            e.Property(r => r.Justification).HasColumnName("justification").HasMaxLength(2000).IsRequired();
            e.Property(r => r.CostCenter).HasColumnName("cost_center").HasMaxLength(120).IsRequired();
            e.Property(r => r.Currency).HasColumnName("currency").HasMaxLength(3);
            e.Property(r => r.RequesterId).HasColumnName("requester_id");
            e.Property(r => r.RequesterLabel).HasColumnName("requester_label").HasMaxLength(200);
            e.Property(r => r.DecisionReason).HasColumnName("decision_reason").HasMaxLength(1000);
            e.Property(r => r.DecidedById).HasColumnName("decided_by_id");
            e.Property(r => r.DecidedByLabel).HasColumnName("decided_by_label").HasMaxLength(200);
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
        });

        modelBuilder.Entity<RequisitionItem>(e =>
        {
            e.ToTable("purchase_requisition_item", "procurement");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasColumnName("id");
            e.Property(i => i.RequisitionId).HasColumnName("requisition_id");
            e.Property(i => i.CatalogItemId).HasColumnName("catalog_item_id");
            e.Property(i => i.CatalogCode).HasColumnName("catalog_code").HasMaxLength(50);
            e.Property(i => i.Sequence).HasColumnName("sequence");
            e.Property(i => i.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
            e.Property(i => i.Quantity).HasColumnName("quantity").HasPrecision(18, 4);
            e.Property(i => i.UnitOfMeasure).HasColumnName("unit_of_measure").HasMaxLength(10);
            e.Property(i => i.EstimatedUnitPrice).HasColumnName("estimated_unit_price").HasPrecision(18, 4);
            e.Property(i => i.Notes).HasColumnName("notes").HasMaxLength(500);
            e.Property(i => i.CreatedAt).HasColumnName("created_at");
            e.HasIndex(i => i.RequisitionId);
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
