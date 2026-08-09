using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.Materials.Domain;

namespace TrinoSupply.Materials.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência de Materiais (schema <c>materials</c>). Isolamento multi-tenant por
/// <c>company_id</c> + RLS no PostgreSQL, aplicado por conexão pelo TenantConnectionInterceptor (SEC-004).
/// </summary>
public sealed class MaterialsDbContext(DbContextOptions<MaterialsDbContext> options) : DbContext(options)
{
    public DbSet<Item> Items => Set<Item>();
    public DbSet<UnitOfMeasure> Units => Set<UnitOfMeasure>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("materials");

        b.Entity<UnitOfMeasure>(e =>
        {
            e.ToTable("unit_of_measure");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasConversion(id => id.Value, v => UnitId.From(v));
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.Property(x => x.Dimension).HasColumnName("dimension").HasMaxLength(50).IsRequired();
            e.Property(x => x.FactorToBase).HasColumnName("factor_to_base").HasColumnType("numeric(18,6)");
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            e.Ignore(x => x.DomainEvents);
        });

        b.Entity<Item>(e =>
        {
            e.ToTable("item");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasConversion(id => id.Value, v => ItemId.From(v));
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(x => x.BaseUnitId).HasColumnName("base_unit_id").HasConversion(id => id.Value, v => UnitId.From(v));
            e.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            e.Ignore(x => x.DomainEvents);
        });

        b.Entity<StockBalance>(e =>
        {
            e.ToTable("stock_balance");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("item_id").HasConversion(id => id.Value, v => ItemId.From(v));
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,6)");
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasIndex(x => x.CompanyId);
            e.Ignore(x => x.DomainEvents);
        });

        b.Entity<StockMovement>(e =>
        {
            e.ToTable("stock_movement");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.ItemId).HasColumnName("item_id").HasConversion(id => id.Value, v => ItemId.From(v));
            e.Property(x => x.Direction).HasColumnName("direction").HasConversion<short>();
            e.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,6)");
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            e.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(300);
            e.HasIndex(x => new { x.CompanyId, x.ItemId, x.OccurredAt });
        });
    }
}
