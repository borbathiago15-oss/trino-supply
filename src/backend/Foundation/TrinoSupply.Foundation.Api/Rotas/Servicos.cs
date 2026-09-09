using Microsoft.AspNetCore.Identity;
using TrinoSupply.Foundation.Api.Auth;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Inventory;
using TrinoSupply.Foundation.Api.Materials;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// Os serviços de domínio que as rotas recebem por injeção.
///
/// Estão num método próprio para que o teste da tabela de rotas monte os
/// mesmos endpoints do app: o binder de minimal API só sabe distinguir um
/// serviço de um corpo de requisição olhando o container, então uma lista
/// diferente aqui daria uma tabela diferente da real.
/// </summary>
public static class Servicos
{
    public static IServiceCollection AddServicosDeDominio(this IServiceCollection servicos)
    {
        servicos.AddSingleton(TimeProvider.System);
        servicos.AddSingleton<TokenService>();
        servicos.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        servicos.AddScoped<AuthService>();
        servicos.AddScoped<UserService>();
        servicos.AddScoped<IPrNumberGenerator, PostgresPrNumberGenerator>();
        servicos.AddScoped<CatalogService>();
        servicos.AddScoped<CatalogImportService>();
        servicos.AddScoped<RequisitionService>();
        servicos.AddScoped<InventoryService>();
        servicos.AddScoped<MaterialRequisitionService>();
        servicos.AddScoped<SupplierService>();
        servicos.AddScoped<PurchaseOrderService>();
        servicos.AddScoped<CostCenterService>();
        servicos.AddScoped<CompanyService>();
        servicos.AddScoped<QuotationService>();
        servicos.AddScoped<TriageService>();
        servicos.AddScoped<PagamentoService>();
        servicos.AddScoped<HistoricoDePrecoService>();
        servicos.AddScoped<ScoreWeightsService>();
        servicos.AddScoped<PrazoDaEtapaService>();
        servicos.AddScoped<TorreDeControleService>();
        servicos.AddScoped<TrinoSupply.Foundation.Api.Comunicados.AnnouncementService>();
        servicos.AddScoped<TrinoSupply.Foundation.Api.Analytics.AnalyticsService>();
        servicos.AddScoped<TrinoSupply.Foundation.Api.Analytics.RelatorioExecutivoService>();
        servicos.AddScoped<TrinoSupply.Foundation.Api.Compliance.ComplianceService>();
        servicos.AddScoped<TrinoSupply.Foundation.Api.Insights.InsightsService>();
        return servicos;
    }
}
