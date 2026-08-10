using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.Procurement.Domain;

namespace TrinoSupply.Procurement.Infrastructure.Persistence;

/// <summary>Contexto de persistência de Compras (schema <c>procurement</c>). RLS por conexão (SEC-004).</summary>
public sealed class ProcurementDbContext(DbContextOptions<ProcurementDbContext> options) : DbContext(options)
{
    public DbSet<PurchaseRequisition> Requisitions => Set<PurchaseRequisition>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PayingCompany> PayingCompanies => Set<PayingCompany>();
    public DbSet<PurchaseOrder> Orders => Set<PurchaseOrder>();

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
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasIndex(x => new { x.CompanyId, x.RequisitionId }).IsUnique(); // um pedido por requisição
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
    }
}
