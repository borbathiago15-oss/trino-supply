using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Auth;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Inventory;
using TrinoSupply.Foundation.Api.Materials;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;
using static TrinoSupply.Foundation.Api.Rotas.Api;
using static TrinoSupply.Foundation.Api.Rotas.Vistas;
using static TrinoSupply.Foundation.Api.Rotas.Anexos;
using TrinoSupply.Foundation.Api.Rotas;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuração -----------------------------------------------------------
var jwtOptions = new JwtOptions();
builder.Configuration.GetSection(JwtOptions.SectionName).Bind(jwtOptions);
jwtOptions.Secret = builder.Configuration["JWT_SECRET"] ?? jwtOptions.Secret;

if (string.IsNullOrWhiteSpace(jwtOptions.Secret) || jwtOptions.Secret.Length < 32)
{
    if (builder.Environment.IsDevelopment())
    {
        jwtOptions.Secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
        Console.WriteLine("AVISO: JWT_SECRET não definido — usando segredo efêmero (apenas Development).");
    }
    else
    {
        throw new InvalidOperationException(
            "JWT_SECRET é obrigatório em produção (mínimo 32 caracteres). Defina a variável de ambiente e reinicie.");
    }
}

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddServicosDeDominio();

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(ConnectionStringFactory.Resolve(builder.Configuration)));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = TokenService.BuildValidationParameters(jwtOptions));
builder.Services.AddAuthorization();

// Rate limit da autenticação (SEC-003), por IP e por rota:
// - "auth": 10 tentativas/min, para o login — é o que barra força bruta de senha.
// - "auth-refresh": 60/min, para a renovação de token. Ela é legítima e frequente
//   (várias abas, recargas), então o limite serve só contra abuso.
// Consultas de sessão (/me) e o logout ficam de fora: limitar essas rotas
// derrubava a sessão de quem apenas navegava entre as telas.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    // a troca de senha é rota autenticada: limitar por IP puniria o escritório
    // inteiro atrás do mesmo IP — quem entra depois de dez logins não conseguiria
    // definir a própria senha. O limite aqui é por usuário, que é o que protege
    // contra tentativa de adivinhar a senha atual (SEC-004).
    o.AddPolicy("auth-senha", ctx => RateLimitPartition.GetFixedWindowLimiter(
        QuemEsta(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    // SEC-B: fora da autenticação, o que custa caro é upload e relatório. Os dois
    // limites são por usuário, não por IP, para o escritório inteiro atrás do
    // mesmo IP não dividir a mesma cota.
    //
    // As cotas são folgadas de propósito. O que se quer barrar é a repetição
    // automática, não o uso humano: apertar até encostar no uso normal troca uma
    // proteção contra abuso por uma tela que quebra na mão de quem trabalha.
    o.AddPolicy("upload", ctx => RateLimitPartition.GetFixedWindowLimiter(
        QuemEsta(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("relatorio", ctx => RateLimitPartition.GetFixedWindowLimiter(
        QuemEsta(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("auth-refresh", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
});

// Descrição OpenAPI nativa (/openapi/v1.json): base para gerar os tipos do frontend React.
builder.Services.AddOpenApi();

var app = builder.Build();

// ---- Migração + seed do admin ----------------------------------------------
var seedOk = true;
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
    seedOk = await AdminSeeder.SeedAsync(db, hasher, app.Configuration, app.Logger);
}

// primeiro de tudo: o que quebrar depois daqui responde no envelope da API e vai para o log
app.UsarEnvelopeDeFalha();

// e logo em seguida a segurança do transporte e do navegador — antes dos arquivos
// estáticos, para o HTML e os anexos saírem com os mesmos cabeçalhos que o JSON
app.UsarHttpsAtrasDoProxy(app.Environment.IsProduction());
app.UsarCabecalhosDeSeguranca();

app.UseDefaultFiles();
// o SPA é um arquivo só: o navegador precisa revalidar o HTML a cada carga, senão
// uma versão antiga fica presa no cache depois do deploy (assets seguem cacheáveis)
var staticFiles = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers.CacheControl = "no-cache, must-revalidate";
    },
};
app.UseStaticFiles(staticFiles);
app.UseRateLimiter();
if (!app.Environment.IsProduction() || app.Configuration["OPENAPI_ENABLED"] == "1")
    app.MapOpenApi();
app.UseAuthentication();
app.UseAuthorization();

// ---- Senha provisória: a sessão só anda depois da troca (SEC-004) -----------
// Fica no pipeline, e não em cada rota, para uma tela nova não nascer com o furo.
// Liberado apenas o mínimo para trocar a senha e para sair.
string[] rotasComSenhaProvisoria =
[
    "/api/v1/auth/change-password", "/api/v1/auth/me", "/api/v1/auth/logout",
    "/api/v1/auth/refresh", "/api/v1/auth/login",
];
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true
        && ctx.User.FindFirstValue(TokenService.SenhaProvisoria) == "1"
        && ctx.Request.Path.StartsWithSegments("/api")
        && !rotasComSenhaProvisoria.Any(r => ctx.Request.Path.Equals(r, StringComparison.OrdinalIgnoreCase)))
    {
        await Results.Json(new
        {
            error = new
            {
                code = "IAM-ERR-022",
                message = "Sua senha ainda é provisória: defina uma nova senha para continuar.",
                correlationId = ctx.TraceIdentifier,
            },
        }, statusCode: 403).ExecuteAsync(ctx);
        return;
    }
    await next();
});

// ---- Envelope de resposta (convenção da suíte: data/error + correlationId) --
// ---- Endpoints ---------------------------------------------------------------
// O healthcheck do Railway aponta para cá. Recebia o AppDbContext e não perguntava nada
// a ele: respondia "healthy" com o banco fora do ar, que é a única coisa que este
// endpoint precisava saber. Agora ele fala com o banco e diz 503 quando não alcança —
// quem pergunta se está tudo bem recebe a resposta verdadeira.
// que commit está no ar, e desde quando — é o que torna verificável a entrega que
// não passa pelo navegador (ver VersaoImplantada)
var commit = VersaoImplantada.Commit();
var iniciadoEm = DateTimeOffset.UtcNow;

app.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
{
    var banco = await db.Database.CanConnectAsync(ct);
    return Results.Json(new
    {
        status = banco ? "healthy" : "degraded",
        service = "trino-supply-foundation",
        database = banco ? "up" : "down",
        setupComplete = seedOk,
        commit,
        commitShort = VersaoImplantada.Curto(commit),
        startedAt = iniciadoEm,
        timestamp = DateTimeOffset.UtcNow,
    }, statusCode: banco ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

// ---- Autenticação: login, refresh, sessão e troca de senha ------------------
app.MapAutenticacao(seedOk);

// ---- Gestão de usuários (exclusiva do SystemAdministrator) -------------------
app.MapUsuarios();

// ---- MMS-002 — Catálogo de itens, famílias e locais de entrega ---------------
app.MapCatalogo();

// ---- PR-001 — Requisição de Compra, decisão e anexos ------------------------
app.MapSolicitacoes();

// ---- Central de avisos: o que está parado, por papel ------------------------
app.MapAvisos();

// Comunicados do administrador: o recado que aparece ao abrir o sistema
app.MapComunicados();

// ---- Torre de Controle: uma linha por item de compra -------------------------
app.MapTorre();

// ---- MMS-004/005 — Estoque e MMS-003 — Solicitação de Material --------------
app.MapEstoque();

// ---- SUP-001 — Fornecedores, homologação, certidões e contrato ---------------
app.MapFornecedores();

// ---- PO-001 — Pedido de Compra, anexos da O.C./NF e o PDF -------------------
app.MapPedidos();

// ---- Cadastros: centros de custo, triagem de demandas e empresas -------------
app.MapCadastros();

// ---- Dashboards analíticos ---------------------------------------------------
app.MapAnalytics();

// ==== RFQ-001 — cotação, aprovações, O.C. e Portal do Fornecedor ============
app.MapCotacoes();

// ==== Documentos: download autorizado por papel e vínculo ====================
app.MapDocumentos();

app.MapGet("/app/{**resto}", (string? resto, HttpContext ctx) =>
    Results.Redirect($"/{resto}{ctx.Request.QueryString}")).AllowAnonymous();
app.MapGet("/app", () => Results.Redirect("/")).AllowAnonymous();

// Frontend React: qualquer rota que não seja arquivo cai no index.html dele, e o
// roteador do navegador assume dali. O `nonfile` impede que o fallback engula o
// próprio JS — sem ele, o bundle voltaria como HTML.
app.MapFallbackToFile("{*path:nonfile}", "index.html", staticFiles);

app.Run();

// ---- Autorização por módulo (cadastro do usuário) ----------------------------
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record CreateUserRequest(string Email, string Name, string Role, string Password, List<string>? Modules,
    List<string>? CostCenters, Guid? DirectorId);
public record UpdateUserRequest(string? Name, string? Role, bool? Active, List<string>? Modules,
    List<string>? CostCenters, Guid? DirectorId, bool? ClearDirector);
public record ResetPasswordRequest(string NewPassword);
public record ItemRequest(string? Description, decimal Quantity, string? UnitOfMeasure, decimal? EstimatedUnitPrice, string? Notes, Guid? CatalogItemId);
public record CreateRequisitionRequest(string Justification, string CostCenter, string? Priority, DateOnly? NeededBy, List<ItemRequest>? Items, string? Kind,
    string? NeedType, string? DeliveryLocation, string? Company, string? InternalNotes,
    string? UrgencyReason = null, string? UrgencyImpact = null, decimal? Budget = null);
public interface IFamilyLeadTimes
{
    int? LeadRequestToQuote { get; }
    int? LeadQuoteToApproval { get; }
    int? LeadApprovalToPo { get; }
    int? LeadPoToDelivery { get; }
    /// <summary>Formulário das famílias: manda as quatro etapas, então vazio limpa a meta.</summary>
    bool? ApplyLeadTimes { get; }
}
public record ProductFamilyRequest(string? Name, string? Notes, string? Category = null,
    int? LeadRequestToQuote = null, int? LeadQuoteToApproval = null,
    int? LeadApprovalToPo = null, int? LeadPoToDelivery = null, bool? ApplyLeadTimes = null) : IFamilyLeadTimes;
public record UpdateProductFamilyRequest(string? Name, string? Notes, bool? Active, string? Category = null,
    bool? ClearCategory = null,
    int? LeadRequestToQuote = null, int? LeadQuoteToApproval = null,
    int? LeadApprovalToPo = null, int? LeadPoToDelivery = null, bool? ApplyLeadTimes = null) : IFamilyLeadTimes;
public record ItemSupplierRequest(string? SupplierName, string? TaxId, string? Contact,
    string? SupplierItemCode, decimal? LastPrice, string? Notes, Guid? SupplierId = null,
    string? CaNumber = null);
public record CreateCatalogItemRequest(string? Code, string Description, string Family, string? UnitOfMeasure,
    decimal? ReferencePrice, bool? StockControlled, decimal? MinimumQty, List<ItemSupplierRequest>? Suppliers,
    bool? Purchasable, string? ProductType);
public record UpdateCatalogItemRequest(string? Description, string? Family, string? UnitOfMeasure,
    decimal? ReferencePrice, bool? Active, bool? StockControlled, decimal? MinimumQty, bool? ClearMinimum,
    List<ItemSupplierRequest>? Suppliers, bool? Purchasable, string? ProductType);
public record CreateLocationRequest(string Code, string Name);
public record MaterialLineRequest(Guid ItemId, decimal Quantity);
public record ApproveMaterialRequest(List<MaterialLineRequest>? Items, string? Notes);
public record ErpOrderRequest(string? ErpNumber, DateOnly? IssuedOn, string? NoErpReason);
public record InvoiceRequest(string? Number, DateOnly? IssuedOn, decimal? Value);
public record DeliveryLineRequest(Guid ItemId, decimal Quantity, decimal? Rejected = null);
public record DeliveryRequest(Guid? LocationId, List<DeliveryLineRequest>? Items, bool? CloseRemaining, string? CloseReason,
    string? RejectReason = null);
public record EntryRequest(Guid CatalogItemId, Guid LocationId, decimal Quantity, string OriginReference, string? Origin);
public record IssueRequest(Guid CatalogItemId, Guid LocationId, decimal Quantity, string OriginReference);
public record MaterialItemRequest(Guid CatalogItemId, decimal Quantity);
public record CreateMaterialRequisitionRequest(string CostCenter, string? Notes, List<MaterialItemRequest>? Items);
public record FulfillRequest(List<MaterialLineRequest>? Items);
public record HomologationRequest(string? Status);
public record SupplierContractItemRequest(Guid? CatalogItemId, string? Description, string? CatalogCode,
    string? UnitOfMeasure, decimal UnitPrice, string? PaymentTerms, int? PaymentDays, int? DeliveryDays, string? Notes);
public record SupplierContractRequest(string? Number, DateOnly? ValidFrom, DateOnly? ValidUntil, string? Notes,
    List<SupplierContractItemRequest>? Items, decimal? ValueLimit = null);
public record ContractAdjustmentRequest(decimal? RequestedPercent, decimal? AgreedPercent, string? Notes,
    bool? ApplyToPrices = null);
public record CreateSupplierRequest(string LegalName, string? TradeName, string? TaxId, string? Email, string? Phone);
public record UpdateSupplierRequest(string? TradeName, string? Email, string? Phone, bool? Active, string? TaxId = null);
public record PoItemRequest(string Description, decimal Quantity, string? UnitOfMeasure, decimal? UnitPrice, Guid? CatalogItemId);
public record CreatePurchaseOrderRequest(Guid SupplierId, string? Notes, List<PoItemRequest>? Items, Guid? SourcePrId);
public record ReceiveOrderRequest(Guid LocationId);
public record CreateCostCenterRequest(string? Code, string Name, string? Region, Guid? ManagerUserId, string? ClientName, Guid? CompanyId, IReadOnlyList<Guid>? Level1UserIds = null, IReadOnlyList<Guid>? Level2UserIds = null, decimal? Level1ValueLimit = null, decimal? Level2ValueLimit = null);
public record UpdateCostCenterRequest(string? Name, string? Region, Guid? ManagerUserId, string? ClientName, bool? Active, Guid? CompanyId, IReadOnlyList<Guid>? Level1UserIds = null, IReadOnlyList<Guid>? Level2UserIds = null, decimal? Level1ValueLimit = null, decimal? Level2ValueLimit = null, bool? ClearValueLimits = null);
public record AssignTicketRequest(string? Kind, Guid Id, Guid? ResponsibleId);
public record AssignBatchRequest(List<AssignBatchItem>? Items, Guid? ResponsibleId);
public record AssignBatchItem(string? Kind, Guid Id);
public record ChangePriorityRequest(Guid Id, string? Priority, string? Reason, string? Impact);
public record CreateCompanyRequest(string LegalName, string TaxId, string? StateRegistration, string Address,
    string? District, string City, string State, string Zip, string? Phone, string? Email);
public record UpdateCompanyRequest(string? LegalName, string? StateRegistration, string? Address, string? District,
    string? City, string? State, string? Zip, string? Phone, string? Email, bool? Active);
public record CreateQuotationRequest(Guid? PrId, string? Kind, DateOnly? Deadline, string? Notes,
    List<Guid>? PrItemIds = null);
public record InviteSuppliersRequest(List<Guid>? SupplierIds);
public record ProposalItemRequest(Guid QuotationItemId, decimal UnitPrice, decimal? Quantity);
public record InternalProposalRequest(Guid SupplierId, int? DeliveryDays, string? PaymentTerms, decimal? FreightValue,
    DateOnly? ValidUntil, string? Notes, List<ProposalItemRequest>? Items, decimal? DiscountValue, string? Currency,
    int? PaymentDays = null, decimal? TaxValue = null, decimal? OtherCosts = null);
public record SelectWinnerRequest(Guid ProposalId, List<string>? Criteria, string Justification,
    List<AwardRequest>? Awards = null);
/// <summary>Escolha do vencedor de uma família (compra dividida entre vários fornecedores).</summary>
public record AwardRequest(string Family, Guid ProposalId, List<string>? Criteria, string? Justification);
public record QuotationDecisionRequest(string Decision, string? Reason);
public record RegisterPoRequest(string? ErpNumber, DateOnly? IssuedOn, string? Notes,
    string? OverLimitJustification = null, Guid? SupplierId = null, string? NoErpReason = null);
public record NegotiationRequest(Guid SupplierId, decimal? ClosedValue, decimal? DiscountPercent, string? Notes);
public record PortalLoginRequest(string TaxId, string AccessKey);
public record PortalProposalRequest(int? DeliveryDays, string? PaymentTerms, decimal? FreightValue, DateOnly? ValidUntil, string? Notes, List<ProposalItemRequest>? Items,
    decimal? TaxValue = null, decimal? OtherCosts = null);
public record CompanyProfileRequest(string LegalName, string Address, string? District, string City, string State, string Zip, string TaxId, string? StateRegistration, string? Phone, string? Email, string? DeliveryAddress, string? DeliveryTaxId, string? StandardClauses, string? PaymentPolicy);
public record UpdateRequisitionRequest(string? Justification, string? CostCenter, string? Priority, DateOnly? NeededBy, bool? ClearNeededBy,
    string? UrgencyReason = null, string? UrgencyImpact = null, decimal? Budget = null, bool? ClearBudget = null);
public record ReasonRequest(string? Reason);
public record DecisionRequest(string? Reason, string? Comments);

public partial class Program;
