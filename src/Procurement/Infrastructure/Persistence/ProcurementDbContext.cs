using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.Procurement.Domain;

namespace TrinoSupply.Procurement.Infrastructure.Persistence;

/// <summary>Contexto de persistência de Compras (schema <c>procurement</c>). RLS por conexão (SEC-004).</summary>
public sealed class ProcurementDbContext(DbContextOptions<ProcurementDbContext> options) : DbContext(options)
{
    public DbSet<PurchaseRequisition> Requisitions => Set<PurchaseRequisition>();

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
            e.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.DecidedBySubject).HasColumnName("decided_by_subject").HasMaxLength(200);
            e.Property(x => x.DecidedAt).HasColumnName("decided_at");
            e.Property(x => x.DecisionNote).HasColumnName("decision_note").HasMaxLength(500);
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasIndex(x => new { x.CompanyId, x.Status });
            e.Ignore(x => x.DomainEvents);

            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.RequisitionId);
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
            e.HasIndex(x => x.RequisitionId);
        });
    }
}
