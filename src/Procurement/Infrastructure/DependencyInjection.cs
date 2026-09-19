using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TrinoSupply.Foundation.Infrastructure.Persistence;
using TrinoSupply.Procurement.Application;
using TrinoSupply.Procurement.Infrastructure.Persistence;

namespace TrinoSupply.Procurement.Infrastructure;

/// <summary>Registro da infraestrutura de Compras (ARC-004 §2). Requer AddFoundationInfrastructure antes.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddProcurementInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres");

        services.AddDbContext<ProcurementDbContext>((sp, options) =>
            options
                .UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__ef_migrations", "procurement"))
                .AddInterceptors(sp.GetRequiredService<TenantConnectionInterceptor>()));

        services.AddScoped<IPurchaseRequisitionService, PurchaseRequisitionService>();
        services.AddScoped<ICostCenterService, CostCenterService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IPayingCompanyService, PayingCompanyService>();
        services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
        services.AddScoped<IGoodsReceiptService, GoodsReceiptService>();
        services.AddScoped<ISupplierScorecardService, SupplierScorecardService>();
        services.AddScoped<IQuotationService, QuotationService>();
        services.AddScoped<IInvoiceMatchService, InvoiceMatchService>();
        return services;
    }
}
