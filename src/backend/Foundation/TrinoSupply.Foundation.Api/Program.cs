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
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<IPrNumberGenerator, PostgresPrNumberGenerator>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<CatalogImportService>();
builder.Services.AddScoped<RequisitionService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<MaterialRequisitionService>();
builder.Services.AddScoped<SupplierService>();
builder.Services.AddScoped<PurchaseOrderService>();
builder.Services.AddScoped<CostCenterService>();
builder.Services.AddScoped<CompanyService>();
builder.Services.AddScoped<QuotationService>();
builder.Services.AddScoped<TriageService>();
builder.Services.AddScoped<TrinoSupply.Foundation.Api.Analytics.AnalyticsService>();

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(ConnectionStringFactory.Resolve(builder.Configuration)));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = TokenService.BuildValidationParameters(jwtOptions));
builder.Services.AddAuthorization();

// Rate limit da rota de login (SEC-003): 10 tentativas/min por IP
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

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

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// ---- Envelope de resposta (convenção da suíte: data/error + correlationId) --
static IResult Ok(object data, HttpContext ctx) =>
    Results.Json(new { data, correlationId = CorrelationId(ctx) });

static IResult Error(HttpContext ctx, int status, string code, string message) =>
    Results.Json(new { error = new { code, message, correlationId = CorrelationId(ctx) } }, statusCode: status);

static string CorrelationId(HttpContext ctx) =>
    ctx.Request.Headers.TryGetValue("X-Correlation-Id", out var v) && !string.IsNullOrWhiteSpace(v)
        ? v.ToString()
        : ctx.TraceIdentifier;

// ---- Endpoints ---------------------------------------------------------------
app.MapGet("/health", (AppDbContext db) => Results.Json(new
{
    status = "healthy",
    service = "trino-supply-foundation",
    setupComplete = seedOk,
    timestamp = DateTimeOffset.UtcNow,
}));

var auth = app.MapGroup("/api/v1/auth").RequireRateLimiting("auth");

auth.MapPost("/login", async (LoginRequest body, AuthService svc, HttpContext ctx) =>
{
    if (!seedOk)
        return Error(ctx, 503, "IAM-ERR-503",
            "Sistema não inicializado: defina ADMIN_EMAIL e ADMIN_PASSWORD e reinicie o serviço.");
    if (string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Password))
        return Error(ctx, 400, "IAM-ERR-400", "Informe e-mail e senha.");

    var tokens = await svc.LoginAsync(body.Email, body.Password);
    return tokens is null
        ? Error(ctx, 401, "IAM-ERR-001", "E-mail ou senha inválidos.")
        : Ok(ToResponse(tokens), ctx);
});

auth.MapPost("/refresh", async (RefreshRequest body, AuthService svc, HttpContext ctx) =>
{
    if (string.IsNullOrWhiteSpace(body.RefreshToken))
        return Error(ctx, 400, "IAM-ERR-400", "Informe o refresh token.");
    var tokens = await svc.RefreshAsync(body.RefreshToken);
    return tokens is null
        ? Error(ctx, 401, "IAM-ERR-002", "Refresh token inválido, expirado ou revogado. Faça login novamente.")
        : Ok(ToResponse(tokens), ctx);
});

auth.MapPost("/logout", async (RefreshRequest body, AuthService svc, HttpContext ctx) =>
{
    if (!string.IsNullOrWhiteSpace(body.RefreshToken)) await svc.LogoutAsync(body.RefreshToken);
    return Ok(new { message = "Sessão encerrada." }, ctx);
});

auth.MapGet("/me", async (ClaimsPrincipal principal, AppDbContext db, HttpContext ctx) =>
{
    var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
              ?? principal.FindFirstValue("sub");
    var costCenters = Array.Empty<string>();
    if (Guid.TryParse(sub, out var uid))
        costCenters = (await db.Users.Where(u => u.Id == uid).Select(u => u.CostCenters).FirstOrDefaultAsync() ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return Ok(new
    {
        id = sub,
        email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email"),
        name = principal.FindFirstValue("name"),
        role = principal.FindFirstValue(ClaimTypes.Role),
        modules = (principal.FindFirstValue("modules") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries),
        costCenters,
    }, ctx);
}).RequireAuthorization();

// ---- Gestão de usuários (exclusiva do SystemAdministrator) -------------------
var users = app.MapGroup("/api/v1/users")
    .RequireAuthorization(p => p.RequireRole(Roles.SystemAdministrator));

static object UserView(User u) => new
{
    id = u.Id, email = u.Email, name = u.Name, role = u.Role, active = u.Active,
    modules = AppModules.EffectiveFor(u), customModules = u.Modules is not null,
    costCenters = (u.CostCenters ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
    directorId = u.DirectorId,
    createdAt = u.CreatedAt, updatedAt = u.UpdatedAt,
};

static Guid ActorId(ClaimsPrincipal p) =>
    Guid.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue("sub"), out var id)
        ? id : Guid.Empty;

users.MapGet("/", async (UserService svc, HttpContext ctx) =>
    Ok(new { items = (await svc.ListAsync()).Select(UserView), roles = UserService.ValidRoles, availableModules = AppModules.All }, ctx));

users.MapPost("/", async (CreateUserRequest body, UserService svc, HttpContext ctx) =>
{
    var (user, error) = await svc.CreateAsync(body.Email, body.Name, body.Role, body.Password,
        body.Modules, body.CostCenters, body.DirectorId);
    return error is not null
        ? Error(ctx, error.Code == "IAM-ERR-014" ? 409 : 400, error.Code, error.Message)
        : Results.Json(new { data = UserView(user!), correlationId = CorrelationId(ctx) }, statusCode: 201);
});

users.MapPatch("/{id:guid}", async (Guid id, UpdateUserRequest body, UserService svc, ClaimsPrincipal principal, HttpContext ctx) =>
{
    var (user, error) = await svc.UpdateAsync(id, ActorId(principal), body.Name, body.Role, body.Active,
        body.Modules, body.CostCenters, body.DirectorId, body.ClearDirector == true);
    return error is not null
        ? Error(ctx, error.Code == "IAM-ERR-404" ? 404 : 422, error.Code, error.Message)
        : Ok(UserView(user!), ctx);
});

users.MapPost("/{id:guid}/reset-password", async (Guid id, ResetPasswordRequest body, UserService svc, HttpContext ctx) =>
{
    var error = await svc.ResetPasswordAsync(id, body.NewPassword);
    return error is not null
        ? Error(ctx, error.Code == "IAM-ERR-404" ? 404 : 400, error.Code, error.Message)
        : Ok(new { message = "Senha redefinida. As sessões do usuário foram encerradas." }, ctx);
});

// ---- MMS-002 — Catálogo de Itens (MVP: famílias + itens) ---------------------
static object CatalogView(CatalogItem i) => new
{
    id = i.Id, code = i.Code, description = i.Description, family = i.Family,
    unitOfMeasure = i.UnitOfMeasure, referencePrice = i.ReferencePrice, active = i.Active,
    stockControlled = i.StockControlled, purchasable = i.Purchasable, minimumQty = i.MinimumQty,
    productType = i.ProductType, productTypeLabel = i.ProductType is null ? null : ProductTypes.LabelOf(i.ProductType),
    caNumber = i.CaNumber, fispqDocumentId = i.FispqDocumentId, fispqFileName = i.FispqFileName,
    baseCode = i.BaseCode, size = i.Size,
    compliancePending = (ProductTypes.RequiresFispq(i.ProductType) && i.FispqDocumentId is null)
                        || (ProductTypes.RequiresCa(i.ProductType) && string.IsNullOrWhiteSpace(i.CaNumber)),
    suppliers = i.Suppliers.OrderBy(s => s.SupplierName).Select(s => new
    {
        id = s.Id, supplierName = s.SupplierName, taxId = s.TaxId, contact = s.Contact,
        supplierItemCode = s.SupplierItemCode, lastPrice = s.LastPrice, notes = s.Notes,
    }),
};

var catalogGroup = app.MapGroup("/api/v1/items").RequireAuthorization();
catalogGroup.AddEndpointFilter(RejectSupplierRole());

catalogGroup.MapGet("/families", async (CatalogService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = p.FindFirstValue(ClaimTypes.Role) ?? "";
    return Ok(new { families = await svc.FamiliesAsync(onlyActive: !CatalogService.CanMaintain(role)) }, ctx);
});

// números do catálogo (a tela de produtos é por busca e não baixa o acervo inteiro)
catalogGroup.MapGet("/summary", async (CatalogService svc, HttpContext ctx) =>
{
    var s = await svc.SummaryAsync();
    return Ok(new
    {
        total = s.Total, active = s.Active, inactive = s.Inactive, compliancePending = s.CompliancePending,
        families = s.Families.Select(f => new { family = f.Family, count = f.Count }),
    }, ctx);
});

catalogGroup.MapGet("/", async (CatalogService svc, ClaimsPrincipal p, HttpContext ctx, string? family, string? q, bool? all, bool? stock) =>
{
    var role = p.FindFirstValue(ClaimTypes.Role) ?? "";
    var includeInactive = all == true && CatalogService.CanMaintain(role);
    var items = await svc.ListAsync(family, q, includeInactive, stock == true);
    return Ok(new { items = items.Select(CatalogView) }, ctx);
});

// importação de produtos por planilha (.xlsx/.csv) — preview e confirmação
app.MapPost("/api/v1/items/import", async (HttpRequest request, CatalogImportService svc,
    ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!CatalogService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.Produtos))
        return Error(ctx, 403, "IC-ERR-900", "Seu usuário não mantém o catálogo.");
    if (!request.HasFormContentType) return Error(ctx, 400, "IMP-ERR-001", "Envie a planilha como multipart/form-data.");

    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    if (file is null || file.Length == 0) return Error(ctx, 400, "IMP-ERR-001", "Nenhuma planilha enviada.");
    if (file.Length > StoredDocument.MaxSizeBytes) return Error(ctx, 400, "DOC-ERR-002", "Arquivo acima de 10 MB.");

    List<string[]> rows;
    try
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        rows = SpreadsheetReader.Read(ms, file.FileName);
    }
    catch (Exception)
    {
        return Error(ctx, 400, "IMP-ERR-002", "Não consegui ler a planilha: envie um arquivo .xlsx ou .csv válido.");
    }

    string? Field(string name) => form.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.ToString() : null;
    var options = new ImportOptions(
        Family: Field("family") ?? "",
        ProductType: Field("productType"),
        StockControlled: Field("stockControlled") == "true",
        Purchasable: Field("purchasable") != "false",
        MinimumQty: decimal.TryParse(Field("minimumQty"), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var min) ? min : null,
        Unit: Field("unit"),
        Sizes: (Field("sizes") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    var commit = Field("commit") == "true";
    var (result, error) = await svc.ImportAsync(ActorId(p), rows, options, commit);
    if (error is not null) return Error(ctx, 400, error.Code, error.Message);

    // as linhas com problema aparecem primeiro: são elas que o usuário precisa corrigir na planilha
    var problems = result!.Rows.Where(r => r.Status != "NOVO").ToList();
    var shown = problems.Take(400)
        .Concat(result.Rows.Where(r => r.Status == "NOVO").Take(Math.Max(0, 400 - problems.Count)))
        .ToList();

    return Ok(new
    {
        fileName = file.FileName,
        totalLines = result.TotalLines, toCreate = result.ToCreate,
        duplicates = result.Duplicates, errors = result.Errors, committed = result.Committed,
        warnings = result.Warnings,
        rowsTruncated = result.Rows.Count > shown.Count,
        rows = shown.Select(r => new
        {
            line = r.Line, code = r.Code, description = r.Description,
            size = r.Size, status = r.Status, message = r.Message,
        }),
        sizeSuggestions = new { letters = CatalogImportService.LetterSizes, numbers = CatalogImportService.NumberSizes },
    }, ctx);
}).RequireAuthorization().AddEndpointFilter(RejectSupplierRole());

// tipos de produto (lista fixa do catálogo, com as exigências de conformidade)
app.MapGet("/api/v1/product-types", (HttpContext ctx) => Ok(new
{
    items = ProductTypes.All.Select(t => new
    {
        key = t.Key, label = t.Label,
        requiresCa = ProductTypes.RequiresCa(t.Key),
        requiresFispq = ProductTypes.RequiresFispq(t.Key),
    }),
}, ctx)).RequireAuthorization().AddEndpointFilter(RejectSupplierRole());

// FISPQ do produto químico (multipart) — sem ela o item não pode ser solicitado
app.MapPost("/api/v1/items/{id:guid}/fispq", async (Guid id, HttpRequest request, AppDbContext db,
    CatalogService svc, TimeProvider clock, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!CatalogService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.Produtos))
        return Error(ctx, 403, "IC-ERR-900", "Seu usuário não mantém o catálogo.");
    var item = await db.CatalogItems.SingleOrDefaultAsync(i => i.Id == id);
    if (item is null) return Error(ctx, 404, "IC-ERR-404", "Item não encontrado.");
    if (!request.HasFormContentType) return Error(ctx, 400, "DOC-ERR-001", "Envie o arquivo como multipart/form-data.");
    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    if (file is null || file.Length == 0) return Error(ctx, 400, "DOC-ERR-001", "Nenhum arquivo enviado.");
    if (file.Length > StoredDocument.MaxSizeBytes) return Error(ctx, 400, "DOC-ERR-002", "Arquivo acima de 10 MB.");
    if (!StoredDocument.AllowedContentTypes.Contains(file.ContentType))
        return Error(ctx, 400, "DOC-ERR-003", "Formato não permitido: envie a FISPQ em PDF, imagem ou documento Office.");

    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    var doc = new StoredDocument
    {
        FileName = Path.GetFileName(file.FileName), ContentType = file.ContentType, SizeBytes = file.Length,
        Content = ms.ToArray(), EntityType = "FISPQ", EntityId = item.Id,
        UploadedByLabel = p.FindFirstValue("name") ?? "Cadastro", UploadedAt = clock.GetUtcNow(),
    };
    db.StoredDocuments.Add(doc);
    await db.SaveChangesAsync();
    var (updated, error) = await svc.AttachFispqAsync(id, doc.Id, doc.FileName);
    return error is not null ? Error(ctx, 400, error.Code, error.Message)
        : Ok(new { documentId = doc.Id, fileName = doc.FileName, item = CatalogView(updated!) }, ctx);
}).RequireAuthorization().AddEndpointFilter(RejectSupplierRole());

// ---- famílias de produtos (cadastro próprio: evita a mesma família escrita de vários jeitos)
static object FamilyView(ProductFamily f) => new
{
    id = f.Id, name = f.Name, notes = f.Notes, active = f.Active,
};

var families = app.MapGroup("/api/v1/product-families").RequireAuthorization();
families.AddEndpointFilter(RejectSupplierRole());

families.MapGet("/", async (CatalogService svc, ClaimsPrincipal p, HttpContext ctx, bool? all) =>
{
    var includeInactive = all == true && CatalogService.CanMaintain(RoleOf(p));
    return Ok(new { items = (await svc.ListFamiliesAsync(includeInactive)).Select(FamilyView) }, ctx);
});

families.MapPost("/", async (ProductFamilyRequest body, CatalogService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!CatalogService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.Produtos))
        return Error(ctx, 403, "IC-ERR-900", "Seu usuário não mantém o catálogo.");
    var (family, error) = await svc.CreateFamilyAsync(ActorId(p), body.Name ?? "", body.Notes);
    return error is not null ? Error(ctx, error.Code == "IC-ERR-021" ? 409 : 400, error.Code, error.Message)
        : Results.Json(new { data = FamilyView(family!), correlationId = CorrelationId(ctx) }, statusCode: 201);
});

families.MapPatch("/{id:guid}", async (Guid id, UpdateProductFamilyRequest body, CatalogService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!CatalogService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.Produtos))
        return Error(ctx, 403, "IC-ERR-900", "Seu usuário não mantém o catálogo.");
    var (family, error) = await svc.UpdateFamilyAsync(id, body.Name, body.Notes, body.Active);
    return error is not null ? Error(ctx, error.Code == "IC-ERR-404" ? 404 : 400, error.Code, error.Message)
        : Ok(FamilyView(family!), ctx);
});

// locais de entrega para os formulários de SC (sem dados de estoque; aberto a papéis internos)
app.MapGet("/api/v1/delivery-locations", async (AppDbContext db, HttpContext ctx) =>
    Ok(new
    {
        items = await db.StorageLocations.Where(l => l.Active).OrderBy(l => l.Code)
            .Select(l => new { id = l.Id, code = l.Code, name = l.Name }).ToListAsync(),
    }, ctx)).RequireAuthorization().AddEndpointFilter(RejectSupplierRole());

// grade da Solicitação em Lote: saldo, previsão de entrada, consumo médio e cobertura por produto
catalogGroup.MapGet("/batch-view", async (TrinoSupply.Foundation.Api.Analytics.AnalyticsService svc,
    HttpContext ctx, string? family, string? q, Guid? locationId) =>
    Ok(await svc.BatchViewAsync(family, q, locationId), ctx));

catalogGroup.MapPost("/", async (CreateCatalogItemRequest body, CatalogService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = p.FindFirstValue(ClaimTypes.Role) ?? "";
    if (!CatalogService.CanMaintain(role))
        return Error(ctx, 403, "IC-ERR-001", "Somente o gestor de suprimentos ou o administrador mantêm o catálogo.");
    if (!ModulesOf(p).Contains(AppModules.Produtos))
        return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para o cadastro de produtos.");
    var suppliers = body.Suppliers?.Select(x => new ItemSupplierInput(
        x.SupplierName ?? "", x.TaxId, x.Contact, x.SupplierItemCode, x.LastPrice, x.Notes)).ToList();
    var (item, error) = await svc.CreateAsync(ActorId(p), body.Code, body.Description, body.Family,
        body.UnitOfMeasure, body.ReferencePrice, body.StockControlled == true, body.MinimumQty, suppliers,
        body.Purchasable ?? true, body.ProductType, body.CaNumber);
    return error is not null
        ? Error(ctx, error.Code == "IC-ERR-010" ? 409 : 400, error.Code, error.Message)
        : Results.Json(new { data = CatalogView(item!), correlationId = CorrelationId(ctx) }, statusCode: 201);
});

catalogGroup.MapPatch("/{id:guid}", async (Guid id, UpdateCatalogItemRequest body, CatalogService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = p.FindFirstValue(ClaimTypes.Role) ?? "";
    if (!CatalogService.CanMaintain(role))
        return Error(ctx, 403, "IC-ERR-001", "Somente o gestor de suprimentos ou o administrador mantêm o catálogo.");
    if (!ModulesOf(p).Contains(AppModules.Produtos))
        return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para o cadastro de produtos.");
    var suppliers = body.Suppliers?.Select(x => new ItemSupplierInput(
        x.SupplierName ?? "", x.TaxId, x.Contact, x.SupplierItemCode, x.LastPrice, x.Notes)).ToList();
    var (item, error) = await svc.UpdateAsync(id, body.Description, body.Family, body.UnitOfMeasure,
        body.ReferencePrice, body.Active, body.StockControlled, body.MinimumQty, body.ClearMinimum == true, suppliers,
        body.Purchasable, body.ProductType, body.CaNumber);
    return error is not null
        ? Error(ctx, error.Code == "IC-ERR-404" ? 404 : 400, error.Code, error.Message)
        : Ok(CatalogView(item!), ctx);
});

// ---- PR-001 — Requisição de Compra (MVP conforme PR-001-03/13) --------------
static Actor? BuildActor(ClaimsPrincipal p)
{
    var id = ActorId(p);
    if (id == Guid.Empty) return null;
    var actor = new Actor(id,
        p.FindFirstValue("name") ?? p.FindFirstValue(ClaimTypes.Email) ?? "Usuário",
        p.FindFirstValue(ClaimTypes.Role) ?? "");
    return actor.CanAccessModule ? actor : null;
}

static object PrView(PurchaseRequisition r, ApproverHint? approver = null) => new
{
    id = r.Id,
    number = r.Number,
    kind = r.Kind,
    status = r.Status switch
    {
        RequisitionStatus.Draft => "DRAFT",
        RequisitionStatus.Submitted => "SUBMITTED",
        RequisitionStatus.InApproval => "IN_APPROVAL",
        RequisitionStatus.Approved => "APPROVED",
        RequisitionStatus.Rejected => "REJECTED",
        RequisitionStatus.Returned => "RETURNED",
        _ => "CANCELLED",
    },
    cycle = r.Cycle,
    priority = r.Priority,
    neededBy = r.NeededBy,
    justification = r.Justification,
    needType = r.NeedType, deliveryLocation = r.DeliveryLocation,
    company = r.Company, internalNotes = r.InternalNotes,
    costCenter = r.CostCenter,
    currency = r.Currency,
    requesterId = r.RequesterId,
    requesterLabel = r.RequesterLabel,
    totalEstimatedValue = r.TotalEstimatedValue,
    decisionReason = r.DecisionReason,
    decidedByLabel = r.DecidedByLabel,
    approverLabel = approver?.ApproverLabel,
    approvalIssue = approver?.Issue,
    assignedToLabel = r.AssignedToLabel,
    decidedAt = r.DecidedAt,
    submittedAt = r.SubmittedAt,
    items = r.Items.OrderBy(i => i.Sequence).Select(i => new
    {
        itemId = i.Id, sequence = i.Sequence, description = i.Description,
        catalogCode = i.CatalogCode,
        quantity = i.Quantity, unitOfMeasure = i.UnitOfMeasure,
        estimatedUnitPrice = i.EstimatedUnitPrice,
        estimatedTotal = i.Quantity * (i.EstimatedUnitPrice ?? 0),
        notes = i.Notes,
    }),
    version = r.Version,
    createdAt = r.CreatedAt,
    updatedAt = r.UpdatedAt,
};

static IResult PrError(HttpContext ctx, UserError e) => Error(ctx, e.Code switch
{
    "PR-ERR-404" => 404,
    "PR-ERR-001" => 403,
    "PR-ERR-040" => 409,
    "PR-ERR-041" or "PR-ERR-042" or "PR-ERR-043" or "PR-ERR-050" => 422,
    _ => 400,
}, e.Code, e.Message);

var prs = app.MapGroup("/api/v1/purchase-requisitions").RequireAuthorization();
prs.AddEndpointFilter(RequireModules(AppModules.Solicitacoes, AppModules.Aprovacao));

prs.MapGet("/", async (RequisitionService svc, ClaimsPrincipal p, HttpContext ctx, string? status) =>
{
    if (BuildActor(p) is not { } actor) return Error(ctx, 403, "PR-ERR-001", "Seu papel não acessa o módulo de requisições.");
    RequisitionStatus? filter = status?.ToUpperInvariant() switch
    {
        null or "" => null,
        "DRAFT" => RequisitionStatus.Draft,
        "IN_APPROVAL" => RequisitionStatus.InApproval,
        "APPROVED" => RequisitionStatus.Approved,
        "REJECTED" => RequisitionStatus.Rejected,
        "RETURNED" => RequisitionStatus.Returned,
        "CANCELLED" => RequisitionStatus.Cancelled,
        _ => null,
    };
    var items = await svc.ListAsync(actor, filter);
    var hints = await svc.ApproverHintsAsync(items);
    return Ok(new { items = items.Select(r => PrView(r, svc.HintFor(hints, r))) }, ctx);
});

prs.MapGet("/{id:guid}", async (Guid id, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (BuildActor(p) is not { } actor) return Error(ctx, 403, "PR-ERR-001", "Seu papel não acessa o módulo de requisições.");
    var pr = await svc.GetAsync(actor, id);
    return pr is null ? Error(ctx, 404, "PR-ERR-404", "Requisição não encontrada.") : Ok(PrView(pr), ctx);
});

prs.MapPost("/", async (CreateRequisitionRequest body, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (BuildActor(p) is not { CanCreate: true } actor)
        return Error(ctx, 403, "PR-ERR-001", "Seu papel não cria requisições.");
    var items = (body.Items ?? []).Select(i => new ItemInput(i.Description ?? "", i.Quantity, i.UnitOfMeasure, i.EstimatedUnitPrice, i.Notes, i.CatalogItemId)).ToList();
    var (pr, error) = await svc.CreateAsync(actor, body.Justification, body.CostCenter, body.Priority, body.NeededBy, items, body.Kind,
        new RequisitionService.ScHeaderInput(body.NeedType, body.DeliveryLocation, body.Company, body.InternalNotes));
    return error is not null ? PrError(ctx, error)
        : Results.Json(new { data = PrView(pr!), correlationId = CorrelationId(ctx) }, statusCode: 201);
});

prs.MapPatch("/{id:guid}", async (Guid id, UpdateRequisitionRequest body, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (BuildActor(p) is not { } actor) return Error(ctx, 403, "PR-ERR-001", "Seu papel não acessa o módulo de requisições.");
    var (pr, error) = await svc.UpdateHeaderAsync(actor, id, body.Justification, body.CostCenter, body.Priority, body.NeededBy, body.ClearNeededBy == true);
    return error is not null ? PrError(ctx, error) : Ok(PrView(pr!), ctx);
});

prs.MapDelete("/{id:guid}", async (Guid id, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (BuildActor(p) is not { } actor) return Error(ctx, 403, "PR-ERR-001", "Seu papel não acessa o módulo de requisições.");
    var error = await svc.DeleteDraftAsync(actor, id);
    return error is not null ? PrError(ctx, error) : Results.NoContent();
});

prs.MapPost("/{id:guid}/submit", async (Guid id, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (BuildActor(p) is not { } actor) return Error(ctx, 403, "PR-ERR-001", "Seu papel não acessa o módulo de requisições.");
    var (pr, error) = await svc.SubmitAsync(actor, id);
    return error is not null ? PrError(ctx, error) : Ok(PrView(pr!), ctx);
});

prs.MapPost("/{id:guid}/cancel", async (Guid id, ReasonRequest body, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (BuildActor(p) is not { } actor) return Error(ctx, 403, "PR-ERR-001", "Seu papel não acessa o módulo de requisições.");
    var (pr, error) = await svc.CancelAsync(actor, id, body.Reason);
    return error is not null ? PrError(ctx, error) : Ok(PrView(pr!), ctx);
});

prs.MapPost("/{id:guid}/items", async (Guid id, ItemRequest body, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (BuildActor(p) is not { } actor) return Error(ctx, 403, "PR-ERR-001", "Seu papel não acessa o módulo de requisições.");
    var (pr, error) = await svc.AddItemAsync(actor, id, new ItemInput(body.Description ?? "", body.Quantity, body.UnitOfMeasure, body.EstimatedUnitPrice, body.Notes, body.CatalogItemId));
    return error is not null ? PrError(ctx, error)
        : Results.Json(new { data = PrView(pr!), correlationId = CorrelationId(ctx) }, statusCode: 201);
});

prs.MapDelete("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (BuildActor(p) is not { } actor) return Error(ctx, 403, "PR-ERR-001", "Seu papel não acessa o módulo de requisições.");
    var (pr, error) = await svc.RemoveItemAsync(actor, id, itemId);
    return error is not null ? PrError(ctx, error) : Ok(PrView(pr!), ctx);
});

// ---- decisão (Approver/SupplyManager/Admin, com SoD no serviço) --------------
prs.MapPost("/{id:guid}/approve", async (Guid id, DecisionRequest body, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (BuildActor(p) is not { CanDecide: true } actor)
        return Error(ctx, 403, "PR-ERR-001", "Seu papel não aprova requisições.");
    var (pr, error) = await svc.ApproveAsync(actor, id, body.Comments);
    return error is not null ? PrError(ctx, error) : Ok(PrView(pr!), ctx);
});

prs.MapPost("/{id:guid}/reject", async (Guid id, DecisionRequest body, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (BuildActor(p) is not { CanDecide: true } actor)
        return Error(ctx, 403, "PR-ERR-001", "Seu papel não decide requisições.");
    var (pr, error) = await svc.RejectAsync(actor, id, body.Reason);
    return error is not null ? PrError(ctx, error) : Ok(PrView(pr!), ctx);
});

prs.MapPost("/{id:guid}/return", async (Guid id, DecisionRequest body, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (BuildActor(p) is not { CanDecide: true } actor)
        return Error(ctx, 403, "PR-ERR-001", "Seu papel não decide requisições.");
    var (pr, error) = await svc.ReturnAsync(actor, id, body.Reason);
    return error is not null ? PrError(ctx, error) : Ok(PrView(pr!), ctx);
});

app.MapGet("/api/v1/approvals/pending", async (RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (BuildActor(p) is not { CanDecide: true } actor)
        return Error(ctx, 403, "PR-ERR-001", "Seu papel não possui fila de aprovação.");
    var pending = await svc.PendingApprovalsAsync(actor);
    var hints = await svc.ApproverHintsAsync(pending);
    return Ok(new { items = pending.Select(r => PrView(r, svc.HintFor(hints, r))) }, ctx);
}).RequireAuthorization().AddEndpointFilter(RequireModules(AppModules.Aprovacao));

// ---- Dashboard — central de avisos (atrasos, aprovações, fila, demandas) -----
app.MapGet("/api/v1/dashboard", async (AppDbContext db, RequisitionService prSvc, ClaimsPrincipal p, HttpContext ctx, TimeProvider clock) =>
{
    var role = RoleOf(p);
    var uid = ActorId(p);
    var mods = ModulesOf(p);
    var actor = new Actor(uid, p.FindFirstValue("name") ?? "Usuário", role);
    var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
    var alerts = new List<object>();

    // Requisições com data de necessidade vencida e ainda não concluídas (escopo do papel)
    if (mods.Contains(AppModules.Solicitacoes) || mods.Contains(AppModules.Aprovacao))
    {
        var overdueQ = db.Requisitions.Where(r =>
            r.NeededBy != null && r.NeededBy < today &&
            (r.Status == RequisitionStatus.Submitted || r.Status == RequisitionStatus.InApproval
             || r.Status == RequisitionStatus.Approved));
        if (!actor.SeesAll) overdueQ = overdueQ.Where(r => r.RequesterId == uid);
        var overdue = await overdueQ.CountAsync();
        if (overdue > 0) alerts.Add(new
        {
            kind = "ATRASO", severity = "alta", count = overdue, view = "pr-mine",
            text = $"{overdue} pedido(s) com data de necessidade vencida e ainda não concluído(s).",
        });
    }

    // Fila de aprovação: solicitações legadas em aprovação (fluxo antigo, sem preço)
    if (mods.Contains(AppModules.Aprovacao) && actor.CanDecide)
    {
        var pending = (await prSvc.PendingApprovalsAsync(actor)).Count;
        if (pending > 0) alerts.Add(new
        {
            kind = "APROVACAO", severity = "media", count = pending, view = "pr-approvals",
            text = $"{pending} solicitação(ões) do fluxo anterior aguardando a sua autorização.",
        });
    }

    // Meus pedidos aguardando aprovação / devolvidos para ajuste
    if (mods.Contains(AppModules.Solicitacoes) && actor.CanCreate)
    {
        var waiting = await db.Requisitions.CountAsync(r => r.RequesterId == uid
            && (r.Status == RequisitionStatus.Submitted || r.Status == RequisitionStatus.InApproval));
        if (waiting > 0) alerts.Add(new
        {
            kind = "AGUARDANDO", severity = "info", count = waiting, view = "pr-mine",
            text = $"{waiting} pedido(s) seu(s) em andamento com Suprimentos.",
        });
        var returned = await db.Requisitions.CountAsync(r => r.RequesterId == uid && r.Status == RequisitionStatus.Returned);
        if (returned > 0) alerts.Add(new
        {
            kind = "DEVOLVIDO", severity = "alta", count = returned, view = "pr-mine",
            text = $"{returned} pedido(s) devolvido(s) para ajuste — revise e reenvie.",
        });
    }

    // Fila do almoxarifado
    if (mods.Contains(AppModules.Estoque) && InventoryService.CanOperate(role))
    {
        var queue = await db.MaterialRequisitions.CountAsync(r => r.Status == MaterialRequisitionStatus.Submitted);
        if (queue > 0) alerts.Add(new
        {
            kind = "ALMOXARIFADO", severity = "media", count = queue, view = "wh-queue",
            text = $"{queue} solicitação(ões) de material aguardando atendimento.",
        });
    }

    // Minhas demandas (tickets designados na triagem)
    {
        var linkedPos = db.PurchaseOrders.Where(o => o.SourcePrId != null && o.Status != PurchaseOrderStatus.Cancelled).Select(o => o.SourcePrId!.Value);
        var mine = await db.Requisitions.CountAsync(r => r.DeletedAt == null && r.AssignedToId == uid
            && (r.Status == RequisitionStatus.Submitted || r.Status == RequisitionStatus.Approved)
            && !linkedPos.Contains(r.Id));
        mine += await db.MaterialRequisitions.CountAsync(r => r.AssignedToId == uid
            && (r.Status == MaterialRequisitionStatus.Submitted || r.Status == MaterialRequisitionStatus.PurchaseRoute));
        if (mine > 0) alerts.Add(new
        {
            kind = "MINHAS_DEMANDAS", severity = "alta", count = mine, view = "triage",
            text = $"{mine} demanda(s) designada(s) a você aguardando continuidade.",
        });
    }

    // Demandas de compra + pedidos emitidos há mais de 7 dias sem recebimento
    if (mods.Contains(AppModules.Compras) && PurchaseOrderService.CanManage(role))
    {
        var linked = db.PurchaseOrders.Where(o => o.SourcePrId != null && o.Status != PurchaseOrderStatus.Cancelled).Select(o => o.SourcePrId!.Value);
        var demands = await db.Requisitions.CountAsync(r =>
            (r.Status == RequisitionStatus.Submitted || r.Status == RequisitionStatus.Approved) && !linked.Contains(r.Id));
        var routeItems = await db.MaterialRequisitionItems.CountAsync(i => i.Status == MaterialItemStatus.PurchaseRoute);
        if (demands + routeItems > 0) alerts.Add(new
        {
            kind = "DEMANDA", severity = "media", count = demands + routeItems, view = "buy-demands",
            text = $"{demands} requisição(ões) aprovada(s) e {routeItems} item(ns) em rota de compra aguardando pedido.",
        });
        if (TriageService.CanTriage(role))
        {
            var untriaged = await db.Requisitions.CountAsync(r => r.DeletedAt == null
                && (r.Status == RequisitionStatus.Submitted || r.Status == RequisitionStatus.Approved)
                && r.AssignedToId == null && !linked.Contains(r.Id));
            untriaged += await db.MaterialRequisitions.CountAsync(r => r.AssignedToId == null
                && (r.Status == MaterialRequisitionStatus.Submitted || r.Status == MaterialRequisitionStatus.PurchaseRoute));
            if (untriaged > 0) alerts.Add(new
            {
                kind = "TRIAGEM", severity = "media", count = untriaged, view = "triage",
                text = $"{untriaged} demanda(s) sem responsável designado na triagem.",
            });
        }
        var lateLimit = clock.GetUtcNow().AddDays(-7);
        var latePos = await db.PurchaseOrders.CountAsync(o => o.Status == PurchaseOrderStatus.Issued && o.CreatedAt < lateLimit);
        if (latePos > 0) alerts.Add(new
        {
            kind = "PO_ATRASO", severity = "alta", count = latePos, view = "buy-orders",
            text = $"{latePos} pedido(s) de compra emitido(s) há mais de 7 dias sem recebimento.",
        });
    }

    // Processo de cotação (RFQ-001): cada etapa avisa o responsável da vez
    if (mods.Contains(AppModules.Compras) || mods.Contains(AppModules.Aprovacao))
    {
        if (QuotationService.CanConduct(role))
        {
            var open = await db.Quotations.CountAsync(q => q.Status == QuotationStatus.Open);
            if (open > 0) alerts.Add(new
            {
                kind = "COTACAO_ABERTA", severity = "info", count = open, view = "quotations",
                text = $"{open} cotação(ões) aberta(s) aguardando propostas.",
            });
            var analysis = await db.Quotations.CountAsync(q => q.Status == QuotationStatus.Analysis);
            if (analysis > 0) alerts.Add(new
            {
                kind = "COTACAO_ANALISE", severity = "media", count = analysis, view = "quotations",
                text = $"{analysis} cotação(ões) em análise aguardando a escolha do fornecedor.",
            });
            var toIssue = await db.Quotations.CountAsync(q => q.Status == QuotationStatus.ApprovedForIssue);
            if (toIssue > 0) alerts.Add(new
            {
                kind = "OC_EMITIR", severity = "alta", count = toIssue, view = "quotations",
                text = $"{toIssue} processo(s) aprovado(s) aguardando a emissão da ordem de compra.",
            });
        }
        if (QuotationService.CanApproveAsManager(role))
        {
            var mgrQuery = db.Quotations.Where(q => q.Status == QuotationStatus.AwaitingManager && q.SelectedBy != uid);
            if (role == Roles.Approver)
            {
                // Pleno: só processos dos CCs sob a sua gerência
                var managedCodes = await db.CostCenters.Where(c => c.Active && c.ManagerUserId == uid)
                    .Select(c => c.Code).ToListAsync();
                mgrQuery = managedCodes.Count == 0
                    ? mgrQuery.Where(_ => false)
                    : mgrQuery.Where(q => managedCodes.Contains(q.CostCenter.ToUpper()));
            }
            var mgr = await mgrQuery.CountAsync();
            if (mgr > 0) alerts.Add(new
            {
                kind = "APROVACAO_GERENTE", severity = "media", count = mgr, view = "quotations",
                text = $"{mgr} processo(s) de compra aguardando a sua aprovação gerencial.",
            });
        }
        if (QuotationService.CanApproveAsDirector(role))
        {
            var awaiting = await db.Quotations.Where(q => q.Status == QuotationStatus.AwaitingDirector
                && q.SelectedBy != uid && q.ManagerApprovedBy != uid)
                .Select(q => new { q.CostCenter, q.ManagerApprovedBy }).ToListAsync();
            var dir = awaiting.Count;
            if (role == Roles.Director && dir > 0)
            {
                // Diretor: só processos roteados a ele (via gerente→diretor) ou sem roteamento
                var ccCodes = awaiting.Select(a => a.CostCenter.ToUpperInvariant()).Distinct().ToList();
                var ccManagers = await db.CostCenters
                    .Where(c => c.Active && ccCodes.Contains(c.Code) && c.ManagerUserId != null)
                    .ToDictionaryAsync(c => c.Code, c => c.ManagerUserId!.Value);
                var managerIds = awaiting.Select(a => a.ManagerApprovedBy).OfType<Guid>()
                    .Concat(ccManagers.Values).Distinct().ToList();
                var directorOf = await db.Users.Where(u => managerIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => u.DirectorId);
                dir = awaiting.Count(a =>
                {
                    var managerId = a.ManagerApprovedBy
                        ?? (ccManagers.TryGetValue(a.CostCenter.ToUpperInvariant(), out var m) ? m : (Guid?)null);
                    var linked = managerId is { } mid && directorOf.TryGetValue(mid, out var d) ? d : null;
                    return linked is null || linked == uid;
                });
            }
            if (dir > 0) alerts.Add(new
            {
                kind = "APROVACAO_DIRETOR", severity = "media", count = dir, view = "quotations",
                text = $"{dir} processo(s) de compra aguardando a aprovação da diretoria.",
            });
        }
    }

    return Ok(new { alerts, generatedAt = clock.GetUtcNow() }, ctx);
}).RequireAuthorization();

// ---- MMS-004 — Estoque (MVP) + MMS-005 — entrada por recebimento -------------
static string RoleOf(ClaimsPrincipal p) => p.FindFirstValue(ClaimTypes.Role) ?? "";

static object MovementView(StockMovement m) => new
{
    id = m.Id, number = m.Number,
    type = m.Type == MovementType.Entry ? "ENTRADA" : "SAIDA",
    origin = m.Origin switch
    {
        MovementOrigin.Receiving => "RECEBIMENTO",
        MovementOrigin.Return => "DEVOLUCAO",
        MovementOrigin.InitialLoad => "CARGA_INICIAL",
        MovementOrigin.Fulfillment => "ATENDIMENTO",
        _ => "CONSUMO",
    },
    originReference = m.OriginReference,
    itemCode = m.ItemCode, itemDescription = m.ItemDescription, unitOfMeasure = m.UnitOfMeasure,
    locationId = m.LocationId, quantity = m.Quantity,
    balanceBefore = m.BalanceBefore, balanceAfter = m.BalanceAfter,
    performedByLabel = m.PerformedByLabel, performedAt = m.PerformedAt,
    materialRequisitionId = m.MaterialRequisitionId,
};

var inv = app.MapGroup("/api/v1/inventory").RequireAuthorization();
inv.AddEndpointFilter(RequireModules(AppModules.Estoque, AppModules.Compras));

inv.MapGet("/locations", async (InventoryService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!InventoryService.CanView(RoleOf(p))) return Error(ctx, 403, "IV-ERR-900", "Seu papel não acessa o estoque.");
    return Ok(new { items = (await svc.LocationsAsync()).Select(l => new { id = l.Id, code = l.Code, name = l.Name }) }, ctx);
});

inv.MapPost("/locations", async (CreateLocationRequest body, InventoryService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = RoleOf(p);
    if (role is not (Roles.WarehouseSupervisor or Roles.SupplyManager or Roles.SystemAdministrator))
        return Error(ctx, 403, "IV-ERR-900", "Somente supervisor, gestor ou administrador gerenciam locais.");
    var (location, error) = await svc.CreateLocationAsync(ActorId(p), body.Code, body.Name);
    return error is not null ? Error(ctx, 400, error.Code, error.Message)
        : Results.Json(new { data = new { id = location!.Id, code = location.Code, name = location.Name }, correlationId = CorrelationId(ctx) }, statusCode: 201);
});

inv.MapGet("/balances", async (InventoryService svc, ClaimsPrincipal p, HttpContext ctx, Guid? itemId, Guid? locationId) =>
{
    if (!InventoryService.CanView(RoleOf(p))) return Error(ctx, 403, "IV-ERR-900", "Seu papel não acessa o estoque.");
    var rows = await svc.BalancesAsync(itemId, locationId);
    return Ok(new
    {
        items = rows.Select(r => new
        {
            itemId = r.item.Id, itemCode = r.item.Code, itemDescription = r.item.Description,
            family = r.item.Family, unitOfMeasure = r.item.UnitOfMeasure,
            locationId = r.location.Id, locationCode = r.location.Code,
            totalQty = r.balance.TotalQty, reservedQty = r.balance.ReservedQty, availableQty = r.balance.AvailableQty,
            lastMovementAt = r.balance.LastMovementAt,
        }),
    }, ctx);
});

inv.MapGet("/movements", async (InventoryService svc, ClaimsPrincipal p, HttpContext ctx, Guid? itemId, Guid? locationId) =>
{
    if (!InventoryService.CanView(RoleOf(p))) return Error(ctx, 403, "IV-ERR-900", "Seu papel não acessa o estoque.");
    return Ok(new { items = (await svc.MovementsAsync(itemId, locationId)).Select(MovementView) }, ctx);
});

// Entrada de material (MMS-005 MVP: recebimento conferido / devolução / carga inicial)
inv.MapPost("/entries", async (EntryRequest body, InventoryService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = RoleOf(p);
    if (!InventoryService.CanOperate(role)) return Error(ctx, 403, "IV-ERR-900", "Seu papel não registra entradas.");
    var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
    var origin = body.Origin?.ToUpperInvariant() switch
    {
        "DEVOLUCAO" => MovementOrigin.Return,
        "CARGA_INICIAL" => MovementOrigin.InitialLoad,
        _ => MovementOrigin.Receiving,
    };
    var (movement, error) = await svc.RegisterEntryAsync(actor, body.CatalogItemId, body.LocationId, body.Quantity, origin, body.OriginReference);
    return error is not null ? Error(ctx, error.Code == "IV-ERR-020" ? 422 : 400, error.Code, error.Message)
        : Results.Json(new { data = MovementView(movement!), correlationId = CorrelationId(ctx) }, statusCode: 201);
});

// Saída manual de material (consumo) — nunca deixa o saldo negativo (MMS-RG-04)
inv.MapPost("/issues", async (IssueRequest body, InventoryService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = RoleOf(p);
    if (!InventoryService.CanOperate(role)) return Error(ctx, 403, "IV-ERR-900", "Seu papel não registra saídas.");
    var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
    var (movement, error) = await svc.RegisterIssueAsync(actor, body.CatalogItemId, body.LocationId, body.Quantity, MovementOrigin.Consumption, body.OriginReference);
    return error is not null ? Error(ctx, error.Code == "IV-ERR-020" ? 422 : 400, error.Code, error.Message)
        : Results.Json(new { data = MovementView(movement!), correlationId = CorrelationId(ctx) }, statusCode: 201);
});

// ---- MMS-003 — Solicitação de Material (MVP) ---------------------------------
static object MrView(MaterialRequisition r) => new
{
    id = r.Id, number = r.Number,
    status = r.Status switch
    {
        MaterialRequisitionStatus.Submitted => "AGUARDANDO_ALMOXARIFADO",
        MaterialRequisitionStatus.Fulfilled => "ATENDIDA",
        MaterialRequisitionStatus.PartiallyFulfilled => "ATENDIDA_PARCIAL",
        MaterialRequisitionStatus.PurchaseRoute => "ROTA_DE_COMPRA",
        _ => "CANCELADA",
    },
    costCenter = r.CostCenter, notes = r.Notes,
    requesterId = r.RequesterId, requesterLabel = r.RequesterLabel,
    fulfilledByLabel = r.FulfilledByLabel, fulfilledAt = r.FulfilledAt, cancelReason = r.CancelReason,
    assignedToId = r.AssignedToId, assignedToLabel = r.AssignedToLabel,
    assignedByLabel = r.AssignedByLabel, assignedAt = r.AssignedAt,
    items = r.Items.Select(i => new
    {
        itemId = i.Id, catalogCode = i.CatalogCode, description = i.Description,
        unitOfMeasure = i.UnitOfMeasure, quantity = i.Quantity,
        status = i.Status switch
        {
            MaterialItemStatus.Fulfilled => "ENTREGUE",
            MaterialItemStatus.PurchaseRoute => "ROTA_DE_COMPRA",
            _ => "PENDENTE",
        },
    }),
    createdAt = r.CreatedAt,
};

var mrs = app.MapGroup("/api/v1/material-requisitions").RequireAuthorization();
mrs.AddEndpointFilter(RequireModules(AppModules.Material, AppModules.Estoque));

mrs.MapGet("/", async (MaterialRequisitionService svc, ClaimsPrincipal p, HttpContext ctx, bool? queue, bool? mine) =>
{
    var role = RoleOf(p);
    var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
    if (queue == true && !MaterialRequisitionService.CanFulfill(role))
        return Error(ctx, 403, "MR-ERR-001", "Seu papel não acessa a fila do almoxarifado.");
    if (queue != true && !(MaterialRequisitionService.CanRequest(role) || MaterialRequisitionService.CanSeeAll(role)))
        return Error(ctx, 403, "MR-ERR-001", "Seu papel não acessa solicitações de material.");
    var list = await svc.ListAsync(actor, queue == true);
    if (mine == true) list = list.Where(r => r.AssignedToId == actor.Id).ToList();
    return Ok(new { items = list.Select(MrView) }, ctx);
});

mrs.MapPost("/", async (CreateMaterialRequisitionRequest body, MaterialRequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = RoleOf(p);
    if (!MaterialRequisitionService.CanRequest(role))
        return Error(ctx, 403, "MR-ERR-001", "Seu papel não cria solicitações de material.");
    var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
    var items = (body.Items ?? []).Select(i => new MaterialItemInput(i.CatalogItemId, i.Quantity)).ToList();
    var (mr, error) = await svc.CreateAsync(actor, body.CostCenter, body.Notes, items);
    return error is not null ? Error(ctx, 400, error.Code, error.Message)
        : Results.Json(new { data = MrView(mr!), correlationId = CorrelationId(ctx) }, statusCode: 201);
});

mrs.MapPost("/{id:guid}/fulfill", async (Guid id, FulfillRequest body, MaterialRequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = RoleOf(p);
    if (!MaterialRequisitionService.CanFulfill(role))
        return Error(ctx, 403, "MR-ERR-001", "Seu papel não atende solicitações.");
    var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
    var (mr, error) = await svc.FulfillAsync(actor, id, body.LocationId);
    return error is not null
        ? Error(ctx, error.Code switch { "MR-ERR-404" => 404, "MR-ERR-040" => 409, _ => 422 }, error.Code, error.Message)
        : Ok(MrView(mr!), ctx);
});

mrs.MapPost("/{id:guid}/cancel", async (Guid id, ReasonRequest body, MaterialRequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", RoleOf(p));
    var (mr, error) = await svc.CancelAsync(actor, id, body.Reason);
    return error is not null
        ? Error(ctx, error.Code switch { "MR-ERR-404" => 404, "MR-ERR-040" => 409, "MR-ERR-001" => 403, _ => 400 }, error.Code, error.Message)
        : Ok(MrView(mr!), ctx);
});

// ---- SUP-001 — Fornecedores (MVP) --------------------------------------------
static object SupplierView(Supplier s) => new
{
    id = s.Id, legalName = s.LegalName, tradeName = s.TradeName, taxId = s.TaxId,
    email = s.Email, phone = s.Phone, active = s.Active,
};

var sup = app.MapGroup("/api/v1/suppliers").RequireAuthorization();
sup.AddEndpointFilter(RequireModules(AppModules.Fornecedores, AppModules.Compras));

sup.MapGet("/", async (SupplierService svc, ClaimsPrincipal p, HttpContext ctx, bool? all) =>
{
    var role = RoleOf(p);
    if (!SupplierService.CanView(role)) return Error(ctx, 403, "SUP-ERR-900", "Seu papel não acessa fornecedores.");
    var includeInactive = all == true && SupplierService.CanMaintain(role);
    return Ok(new { items = (await svc.ListAsync(includeInactive)).Select(SupplierView) }, ctx);
});

sup.MapPost("/", async (CreateSupplierRequest body, SupplierService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!SupplierService.CanMaintain(RoleOf(p)))
        return Error(ctx, 403, "SUP-ERR-900", "Seu papel não mantém o cadastro de fornecedores.");
    var (supplier, error) = await svc.CreateAsync(ActorId(p), body.LegalName, body.TradeName, body.TaxId, body.Email, body.Phone);
    return error is not null ? Error(ctx, 400, error.Code, error.Message)
        : Results.Json(new { data = SupplierView(supplier!), correlationId = CorrelationId(ctx) }, statusCode: 201);
});

// gera a chave do Portal do Fornecedor (mostrada uma única vez; persiste só o hash)
sup.MapPost("/{id:guid}/portal-key", async (Guid id, SupplierService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!SupplierService.CanMaintain(RoleOf(p)))
        return Error(ctx, 403, "SUP-ERR-900", "Seu papel não gera chaves do portal.");
    var (key, error) = await svc.GeneratePortalKeyAsync(id);
    return error is not null ? Error(ctx, 404, error.Code, error.Message)
        : Ok(new { accessKey = key, message = "Guarde a chave: ela não será exibida novamente." }, ctx);
});

sup.MapPatch("/{id:guid}", async (Guid id, UpdateSupplierRequest body, SupplierService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!SupplierService.CanMaintain(RoleOf(p)))
        return Error(ctx, 403, "SUP-ERR-900", "Seu papel não mantém o cadastro de fornecedores.");
    var (supplier, error) = await svc.UpdateAsync(id, body.TradeName, body.Email, body.Phone, body.Active);
    return error is not null ? Error(ctx, error.Code == "SUP-ERR-404" ? 404 : 400, error.Code, error.Message)
        : Ok(SupplierView(supplier!), ctx);
});

// ---- PO-001 — Pedido de Compra (MVP) -----------------------------------------
static object PoView(PurchaseOrder o) => new
{
    id = o.Id, number = o.Number,
    status = o.Status switch
    {
        PurchaseOrderStatus.Issued => "EMITIDO",
        PurchaseOrderStatus.Received => "RECEBIDO",
        _ => "CANCELADO",
    },
    supplierId = o.SupplierId, supplierName = o.SupplierName,
    sourcePrNumber = o.SourcePrNumber, quotationNumber = o.QuotationNumber,
    paymentTerms = o.PaymentTerms, deliveryDays = o.DeliveryDays, freightValue = o.FreightValue,
    notes = o.Notes, totalValue = o.TotalValue,
    issuedByLabel = o.IssuedByLabel, receivedByLabel = o.ReceivedByLabel, receivedAt = o.ReceivedAt,
    cancelReason = o.CancelReason, createdAt = o.CreatedAt,
    items = o.Items.Select(i => new
    {
        description = i.Description, unitOfMeasure = i.UnitOfMeasure, quantity = i.Quantity,
        unitPrice = i.UnitPrice, catalogCode = i.CatalogCode, catalogItemId = i.CatalogItemId,
    }),
};

var pos = app.MapGroup("/api/v1/purchase-orders").RequireAuthorization();
pos.AddEndpointFilter(RequireModules(AppModules.Compras));

pos.MapGet("/", async (PurchaseOrderService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!PurchaseOrderService.CanView(RoleOf(p)))
        return Error(ctx, 403, "PO-ERR-900", "Seu papel não acessa pedidos de compra.");
    return Ok(new { items = (await svc.ListAsync()).Select(PoView) }, ctx);
});

pos.MapGet("/demands", async (PurchaseOrderService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!PurchaseOrderService.CanManage(RoleOf(p)))
        return Error(ctx, 403, "PO-ERR-900", "Seu papel não acessa as demandas de compra.");
    var (prs, mrItems) = await svc.DemandsAsync();
    return Ok(new
    {
        approvedRequisitions = prs.Select(r => new
        {
            id = r.Id, number = r.Number, requesterLabel = r.RequesterLabel,
            costCenter = r.CostCenter, justification = r.Justification,
            totalEstimatedValue = r.TotalEstimatedValue, decidedAt = r.DecidedAt,
            items = r.Items.Select(i => new
            {
                description = i.Description, quantity = i.Quantity,
                unitOfMeasure = i.UnitOfMeasure, estimatedUnitPrice = i.EstimatedUnitPrice,
                catalogCode = i.CatalogCode, catalogItemId = i.CatalogItemId,
            }),
        }),
        purchaseRouteItems = mrItems.Select(x => new
        {
            materialRequisitionNumber = x.mr.Number, requesterLabel = x.mr.RequesterLabel,
            costCenter = x.mr.CostCenter, catalogItemId = x.item.CatalogItemId,
            catalogCode = x.item.CatalogCode, description = x.item.Description,
            unitOfMeasure = x.item.UnitOfMeasure, quantity = x.item.Quantity,
        }),
    }, ctx);
});

pos.MapPost("/", async (CreatePurchaseOrderRequest body, PurchaseOrderService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = RoleOf(p);
    if (!PurchaseOrderService.CanManage(role))
        return Error(ctx, 403, "PO-ERR-900", "Seu papel não emite pedidos de compra.");
    var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
    var items = (body.Items ?? []).Select(i =>
        new PoItemInput(i.Description, i.Quantity, i.UnitOfMeasure, i.UnitPrice, i.CatalogItemId)).ToList();
    var (order, error) = await svc.CreateAsync(actor, body.SupplierId, body.Notes, items, body.SourcePrId);
    return error is not null
        ? Error(ctx, error.Code switch { "PO-ERR-021" => 422, "PO-ERR-022" => 409, _ => 400 }, error.Code, error.Message)
        : Results.Json(new { data = PoView(order!), correlationId = CorrelationId(ctx) }, statusCode: 201);
});

pos.MapPost("/{id:guid}/receive", async (Guid id, ReceiveOrderRequest body, PurchaseOrderService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = RoleOf(p);
    if (!PurchaseOrderService.CanManage(role))
        return Error(ctx, 403, "PO-ERR-900", "Seu papel não registra recebimentos de pedido.");
    var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
    var (order, error) = await svc.ReceiveAsync(actor, id, body.LocationId);
    return error is not null
        ? Error(ctx, error.Code switch { "PO-ERR-404" => 404, "PO-ERR-040" => 409, _ => 400 }, error.Code, error.Message)
        : Ok(PoView(order!), ctx);
});

pos.MapPost("/{id:guid}/cancel", async (Guid id, ReasonRequest body, PurchaseOrderService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = RoleOf(p);
    if (!PurchaseOrderService.CanManage(role))
        return Error(ctx, 403, "PO-ERR-900", "Seu papel não cancela pedidos de compra.");
    var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
    var (order, error) = await svc.CancelAsync(actor, id, body.Reason);
    return error is not null
        ? Error(ctx, error.Code switch { "PO-ERR-404" => 404, "PO-ERR-040" => 409, _ => 400 }, error.Code, error.Message)
        : Ok(PoView(order!), ctx);
});

// ---- Centros de custo (master data mínimo — dimensões dos dashboards) --------
static object CcView(CostCenter c) => new
{
    id = c.Id, code = c.Code, name = c.Name, region = c.Region,
    companyId = c.CompanyId,
    managerUserId = c.ManagerUserId, managerName = c.ManagerName,
    clientName = c.ClientName, active = c.Active,
};

// usuários ativos (id + nome + papel) para pickers de vínculo — sem dados sensíveis
app.MapGet("/api/v1/users/pickers", async (AppDbContext db, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!CostCenterService.CanMaintain(RoleOf(p)) && RoleOf(p) != Roles.SystemAdministrator)
        return Error(ctx, 403, "IAM-ERR-018", "Seu papel não acessa a lista de usuários.");
    return Ok(new
    {
        items = await db.Users.Where(u => u.Active).OrderBy(u => u.Name)
            .Select(u => new { id = u.Id, name = u.Name, role = u.Role }).ToListAsync(),
    }, ctx);
}).RequireAuthorization().AddEndpointFilter(RejectSupplierRole());

var ccs = app.MapGroup("/api/v1/cost-centers").RequireAuthorization();
ccs.AddEndpointFilter(RejectSupplierRole());

// listagem aberta a autenticados: os formulários de requisição/solicitação usam o picker
ccs.MapGet("/", async (CostCenterService svc, ClaimsPrincipal p, HttpContext ctx, bool? all) =>
{
    var includeInactive = all == true && CostCenterService.CanMaintain(RoleOf(p));
    return Ok(new { items = (await svc.ListAsync(includeInactive)).Select(CcView) }, ctx);
});

ccs.MapPost("/", async (CreateCostCenterRequest body, CostCenterService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!CostCenterService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.CentrosCusto))
        return Error(ctx, 403, "CC-ERR-900", "Seu usuário não mantém centros de custo.");
    var (cc, error) = await svc.CreateAsync(ActorId(p), body.Code, body.Name, body.Region, body.ManagerUserId, body.ClientName, body.CompanyId);
    return error is not null ? Error(ctx, 400, error.Code, error.Message)
        : Results.Json(new { data = CcView(cc!), correlationId = CorrelationId(ctx) }, statusCode: 201);
});

ccs.MapPatch("/{id:guid}", async (Guid id, UpdateCostCenterRequest body, CostCenterService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!CostCenterService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.CentrosCusto))
        return Error(ctx, 403, "CC-ERR-900", "Seu usuário não mantém centros de custo.");
    var (cc, error) = await svc.UpdateAsync(id, body.Name, body.Region, body.ManagerUserId, body.ClientName, body.Active, body.CompanyId);
    return error is not null ? Error(ctx, error.Code == "CC-ERR-404" ? 404 : 400, error.Code, error.Message)
        : Ok(CcView(cc!), ctx);
});

// ---- Triagem de demandas (tickets) ------------------------------------------
static object TicketView(TriageTicket t) => new
{
    kind = t.Kind, id = t.Id, number = t.Number, costCenter = t.CostCenter,
    requesterLabel = t.RequesterLabel, summary = t.Summary, estimatedValue = t.EstimatedValue,
    openedAt = t.OpenedAt, status = t.Status,
    assignedToId = t.AssignedToId, assignedToLabel = t.AssignedToLabel,
    assignedByLabel = t.AssignedByLabel, assignedAt = t.AssignedAt,
};

var triage = app.MapGroup("/api/v1/triage").RequireAuthorization();
triage.AddEndpointFilter(RejectSupplierRole());

triage.MapGet("/", async (TriageService svc, ClaimsPrincipal p, HttpContext ctx, string? filter) =>
{
    if (BuildActor(p) is not { } actor) return Error(ctx, 403, "TRI-ERR-900", "Seu papel não acessa a triagem.");
    var scope = (filter ?? "TODAS").Trim().ToUpperInvariant();
    if (scope != "MINHAS" && !TriageService.CanTriage(RoleOf(p)) && !PurchaseOrderService.CanManage(RoleOf(p)))
        return Error(ctx, 403, "TRI-ERR-900", "Seu papel não acessa o painel de demandas.");
    return Ok(new { items = (await svc.ListAsync(actor, scope)).Select(TicketView) }, ctx);
});

triage.MapGet("/responsibles", async (TriageService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!TriageService.CanTriage(RoleOf(p)))
        return Error(ctx, 403, "TRI-ERR-900", "Seu papel não distribui demandas.");
    return Ok(new { items = await svc.ResponsiblesAsync() }, ctx);
});

triage.MapPost("/assign", async (AssignTicketRequest body, TriageService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (BuildActor(p) is not { } actor) return Error(ctx, 403, "TRI-ERR-900", "Seu papel não acessa a triagem.");
    var (ticket, error) = await svc.AssignAsync(actor, body.Kind ?? "", body.Id, body.ResponsibleId);
    return error is not null
        ? Error(ctx, error.Code switch { "TRI-ERR-404" => 404, "TRI-ERR-900" => 403, "TRI-ERR-020" => 409, _ => 400 },
                error.Code, error.Message)
        : Ok(TicketView(ticket!), ctx);
});

// ---- Empresas do grupo (CNPJs) — cada CC pode apontar para um CNPJ -----------
static object CompanyView(Company c) => new
{
    id = c.Id, legalName = c.LegalName, taxId = c.TaxId, stateRegistration = c.StateRegistration,
    address = c.Address, district = c.District, city = c.City, state = c.State, zip = c.Zip,
    phone = c.Phone, email = c.Email, active = c.Active,
};

var companies = app.MapGroup("/api/v1/companies").RequireAuthorization();
companies.AddEndpointFilter(RejectSupplierRole());

// listagem aberta a autenticados internos: SC/CC usam o picker de empresa
companies.MapGet("/", async (CompanyService svc, ClaimsPrincipal p, HttpContext ctx, bool? all) =>
{
    var includeInactive = all == true && CompanyService.CanMaintain(RoleOf(p));
    return Ok(new { items = (await svc.ListAsync(includeInactive)).Select(CompanyView) }, ctx);
});

companies.MapPost("/", async (CreateCompanyRequest body, CompanyService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!CompanyService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.CentrosCusto))
        return Error(ctx, 403, "EMP-ERR-900", "Seu usuário não mantém o cadastro de CNPJs do grupo.");
    var (company, error) = await svc.CreateAsync(body.LegalName, body.TaxId, body.StateRegistration,
        body.Address, body.District, body.City, body.State, body.Zip, body.Phone, body.Email);
    return error is not null ? Error(ctx, error.Code == "EMP-ERR-012" ? 409 : 400, error.Code, error.Message)
        : Results.Json(new { data = CompanyView(company!), correlationId = CorrelationId(ctx) }, statusCode: 201);
});

companies.MapPatch("/{id:guid}", async (Guid id, UpdateCompanyRequest body, CompanyService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!CompanyService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.CentrosCusto))
        return Error(ctx, 403, "EMP-ERR-900", "Seu usuário não mantém o cadastro de CNPJs do grupo.");
    var (company, error) = await svc.UpdateAsync(id, body.LegalName, body.StateRegistration, body.Address,
        body.District, body.City, body.State, body.Zip, body.Phone, body.Email, body.Active);
    return error is not null ? Error(ctx, error.Code == "EMP-ERR-404" ? 404 : 400, error.Code, error.Message)
        : Ok(CompanyView(company!), ctx);
});

// ---- Dashboards analíticos ---------------------------------------------------
var analytics = app.MapGroup("/api/v1/analytics").RequireAuthorization();

analytics.MapGet("/supply", async (TrinoSupply.Foundation.Api.Analytics.AnalyticsService svc,
    ClaimsPrincipal p, HttpContext ctx, TimeProvider clock,
    DateOnly? from, DateOnly? to, Guid? supplierId, Guid? buyerId, Guid? requesterId,
    string? family, string? costCenter, string? region, string? manager, string? client) =>
{
    if (!TrinoSupply.Foundation.Api.Analytics.AnalyticsService.CanViewSupply(RoleOf(p)))
        return Error(ctx, 403, "AN-ERR-900", "Seu papel não acessa o dashboard de suprimentos.");
    var mods = ModulesOf(p);
    if (!mods.Contains(AppModules.Solicitacoes) && !mods.Contains(AppModules.Aprovacao) && !mods.Contains(AppModules.Compras))
        return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para este módulo.");
    var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
    var f = from ?? new DateOnly(today.Year, today.Month, 1).AddMonths(-11);
    var t = to ?? today;
    if (t < f) (f, t) = (t, f);
    return Ok(await svc.SupplyAsync(f, t, supplierId, buyerId, requesterId, family, costCenter, region, manager, client), ctx);
});

analytics.MapGet("/stock", async (TrinoSupply.Foundation.Api.Analytics.AnalyticsService svc,
    ClaimsPrincipal p, HttpContext ctx, Guid? locationId, string? family, int? months) =>
{
    if (!InventoryService.CanView(RoleOf(p)))
        return Error(ctx, 403, "AN-ERR-900", "Seu papel não acessa o dashboard de estoque.");
    var mods = ModulesOf(p);
    if (!mods.Contains(AppModules.Estoque) && !mods.Contains(AppModules.Compras))
        return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para este módulo.");
    return Ok(await svc.StockAsync(locationId, family, months ?? 6), ctx);
});

// ==== RFQ-001 — Processo fechado de compras (cotação → aprovações → OC) ======
static string QKindLabel(QuotationKind k) => k switch
{
    QuotationKind.Bid => "BID", QuotationKind.Service => "SERVICO", _ => "COMPRA",
};
static string QStatusLabel(QuotationStatus s) => s switch
{
    QuotationStatus.Open => "COTACAO_ABERTA",
    QuotationStatus.Analysis => "EM_ANALISE",
    QuotationStatus.AwaitingManager => "AGUARDANDO_GERENTE",
    QuotationStatus.AwaitingDirector => "AGUARDANDO_DIRETOR",
    QuotationStatus.ApprovedForIssue => "APROVADO_PARA_EMISSAO",
    QuotationStatus.PoIssued => "OC_EMITIDA",
    QuotationStatus.Rejected => "REJEITADO",
    _ => "CANCELADA",
};

static object ProposalView(Proposal p, Quotation q) => new
{
    id = p.Id, supplierId = p.SupplierId, supplierName = p.SupplierName,
    version = p.VersionNumber, totalValue = p.TotalValue, deliveryDays = p.DeliveryDays,
    paymentTerms = p.PaymentTerms, freightValue = p.FreightValue, validUntil = p.ValidUntil,
    notes = p.Notes, submittedVia = p.SubmittedVia, submittedByLabel = p.SubmittedByLabel,
    submittedAt = p.SubmittedAt, attachmentDocumentId = p.AttachmentDocumentId,
    attachmentFileName = p.AttachmentFileName,
    isLatest = q.Proposals.Where(x => x.SupplierId == p.SupplierId).Max(x => x.VersionNumber) == p.VersionNumber,
    isWinner = q.WinnerProposalId == p.Id,
    items = p.Items.Select(i => new { quotationItemId = i.QuotationItemId, unitPrice = i.UnitPrice, quantity = i.Quantity }),
};

static object QuotationView(Quotation q) => new
{
    id = q.Id, number = q.Number, kind = QKindLabel(q.Kind), status = QStatusLabel(q.Status),
    sourcePrId = q.SourcePrId, sourcePrNumber = q.SourcePrNumber, costCenter = q.CostCenter,
    justification = q.Justification, deadline = q.Deadline, notes = q.Notes,
    createdByLabel = q.CreatedByLabel, createdAt = q.CreatedAt, decisionReason = q.DecisionReason,
    items = q.Items.OrderBy(i => i.Sequence).Select(i => new
    {
        id = i.Id, sequence = i.Sequence, catalogCode = i.CatalogCode,
        description = i.Description, quantity = i.Quantity, unitOfMeasure = i.UnitOfMeasure,
    }),
    suppliers = q.Suppliers.Select(s => new
    {
        supplierId = s.SupplierId, supplierName = s.SupplierName, taxId = s.TaxId,
        invitedAt = s.InvitedAt, invitedByLabel = s.InvitedByLabel,
        hasProposal = q.Proposals.Any(p => p.SupplierId == s.SupplierId),
    }),
    proposals = q.Proposals.OrderBy(p => p.SupplierName).ThenByDescending(p => p.VersionNumber)
        .Select(p => ProposalView(p, q)),
    selection = q.SelectedAt is null ? null : new
    {
        winnerSupplierId = q.WinnerSupplierId, winnerProposalId = q.WinnerProposalId,
        criteria = q.SelectionCriteria, justification = q.SelectionJustification,
        byLabel = q.SelectedByLabel, at = q.SelectedAt,
    },
    managerApproval = q.ManagerApprovedAt is null ? null : new { byLabel = q.ManagerApprovedByLabel, at = q.ManagerApprovedAt },
    directorApproval = q.DirectorApprovedAt is null ? null : new { byLabel = q.DirectorApprovedByLabel, at = q.DirectorApprovedAt },
    purchaseOrderId = q.PurchaseOrderId, purchaseOrderNumber = q.PurchaseOrderNumber,
};

var rfq = app.MapGroup("/api/v1/quotations").RequireAuthorization();
rfq.AddEndpointFilter(RejectSupplierRole());
rfq.AddEndpointFilter(RequireModules(AppModules.Compras, AppModules.Aprovacao));

rfq.MapGet("/", async (QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!QuotationService.CanView(RoleOf(p))) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não acessa cotações.");
    return Ok(new { items = (await svc.ListAsync()).Select(QuotationView) }, ctx);
});

// Central de Aprovação: processos de compra aguardando a MINHA alçada, já com preços
rfq.MapGet("/my-approvals", async (QuotationService svc, AppDbContext db, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = RoleOf(p);
    var uid = ActorId(p);
    if (!QuotationService.CanApproveAsManager(role) && !QuotationService.CanApproveAsDirector(role))
        return Ok(new { items = Array.Empty<object>() }, ctx);

    var all = await svc.ListAsync();
    var mine = new List<Quotation>();

    if (QuotationService.CanApproveAsManager(role))
    {
        var awaiting = all.Where(q => q.Status == QuotationStatus.AwaitingManager && q.SelectedBy != uid).ToList();
        if (role == Roles.Approver)
        {
            var managed = await db.CostCenters.Where(c => c.Active && c.ManagerUserId == uid)
                .Select(c => c.Code.ToUpper()).ToListAsync();
            awaiting = awaiting.Where(q => managed.Contains(q.CostCenter.ToUpperInvariant())).ToList();
        }
        mine.AddRange(awaiting);
    }
    if (QuotationService.CanApproveAsDirector(role))
        mine.AddRange(all.Where(q => q.Status == QuotationStatus.AwaitingDirector
                                     && q.SelectedBy != uid && q.ManagerApprovedBy != uid));

    return Ok(new { items = mine.DistinctBy(q => q.Id).Select(QuotationView) }, ctx);
});

// fila de Suprimentos: PRs aprovadas aguardando cotação
rfq.MapGet("/queue", async (QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!QuotationService.CanView(RoleOf(p))) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não acessa a fila de suprimentos.");
    var (ready, blocked) = await svc.QueueAsync();
    static object QueueItem(PurchaseRequisition r, string? blockReason) => new
    {
        id = r.Id, number = r.Number, requesterLabel = r.RequesterLabel, costCenter = r.CostCenter,
        justification = r.Justification, totalEstimatedValue = r.TotalEstimatedValue,
        neededBy = r.NeededBy, decidedAt = r.DecidedAt,
        assignedToId = r.AssignedToId, assignedToLabel = r.AssignedToLabel,
        blockReason,
        items = r.Items.Select(i => new { description = i.Description, quantity = i.Quantity, unitOfMeasure = i.UnitOfMeasure }),
    };
    return Ok(new
    {
        items = ready.Select(r => QueueItem(r, null))
            .Concat(blocked.Select(b => QueueItem(b.Pr, b.Reason))),
    }, ctx);
});

rfq.MapGet("/{id:guid}", async (Guid id, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!QuotationService.CanView(RoleOf(p))) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não acessa cotações.");
    var q = await svc.GetAsync(id);
    return q is null ? Error(ctx, 404, "RFQ-ERR-404", "Cotação não encontrada.") : Ok(QuotationView(q), ctx);
});

rfq.MapGet("/{id:guid}/timeline", async (Guid id, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!QuotationService.CanView(RoleOf(p))) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não acessa cotações.");
    var events = await svc.TimelineAsync(id);
    return Ok(new
    {
        items = events.Select(e => new
        {
            eventType = e.EventType, description = e.Description,
            fromStatus = e.FromStatus is null ? null : QStatusLabel(e.FromStatus.Value),
            toStatus = e.ToStatus is null ? null : QStatusLabel(e.ToStatus.Value),
            actorLabel = e.ActorLabel, note = e.Note, documentId = e.DocumentId, occurredAt = e.OccurredAt,
        }),
    }, ctx);
});

rfq.MapPost("/", async (CreateQuotationRequest body, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = RoleOf(p);
    if (!QuotationService.CanConduct(role)) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não abre cotações.");
    var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
    var kind = body.Kind?.ToUpperInvariant() switch
    {
        "BID" => QuotationKind.Bid, "SERVICO" => QuotationKind.Service, _ => QuotationKind.Purchase,
    };
    var (q, error) = await svc.CreateFromPrAsync(actor, body.PrId, kind, body.Deadline, body.Notes);
    return error is not null ? Error(ctx, 422, error.Code, error.Message)
        : Results.Json(new { data = QuotationView(q!), correlationId = CorrelationId(ctx) }, statusCode: 201);
});

rfq.MapPost("/{id:guid}/suppliers", async (Guid id, InviteSuppliersRequest body, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = RoleOf(p);
    if (!QuotationService.CanConduct(role)) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não convida fornecedores.");
    var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
    var (q, error) = await svc.InviteSuppliersAsync(actor, id, body.SupplierIds ?? []);
    return error is not null ? Error(ctx, error.Code == "RFQ-ERR-010" ? 400 : 409, error.Code, error.Message) : Ok(QuotationView(q!), ctx);
});

// registro interno de proposta recebida fora do portal (e-mail/telefone)
rfq.MapPost("/{id:guid}/proposals", async (Guid id, InternalProposalRequest body, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = RoleOf(p);
    if (!QuotationService.CanConduct(role)) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não registra propostas.");
    var input = new ProposalInput(body.DeliveryDays, body.PaymentTerms, body.FreightValue, body.ValidUntil, body.Notes,
        (body.Items ?? []).Select(i => new ProposalItemInput(i.QuotationItemId, i.UnitPrice, i.Quantity)).ToList());
    var (proposal, error) = await svc.SubmitProposalAsync(id, body.SupplierId, input, "INTERNO", p.FindFirstValue("name") ?? "Usuário");
    if (error is not null) return Error(ctx, error.Code == "RFQ-ERR-020" ? 409 : 400, error.Code, error.Message);
    var q = await svc.GetAsync(id);
    return Ok(QuotationView(q!), ctx);
});

rfq.MapPost("/{id:guid}/close", async (Guid id, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = RoleOf(p);
    if (!QuotationService.CanConduct(role)) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não encerra cotações.");
    var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
    var (q, error) = await svc.CloseForAnalysisAsync(actor, id);
    return error is not null ? Error(ctx, error.Code == "RFQ-ERR-021" ? 422 : 409, error.Code, error.Message) : Ok(QuotationView(q!), ctx);
});

rfq.MapPost("/{id:guid}/select-winner", async (Guid id, SelectWinnerRequest body, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = RoleOf(p);
    if (!QuotationService.CanConduct(role)) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não seleciona fornecedores.");
    var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
    var criteria = body.Criteria is { Count: > 0 } ? string.Join(", ", body.Criteria) : null;
    var (q, error) = await svc.SelectWinnerAsync(actor, id, body.ProposalId, criteria, body.Justification);
    return error is not null ? Error(ctx, error.Code == "RFQ-ERR-020" ? 409 : 422, error.Code, error.Message) : Ok(QuotationView(q!), ctx);
});

rfq.MapPost("/{id:guid}/manager-decision", async (Guid id, QuotationDecisionRequest body, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = RoleOf(p);
    if (!QuotationService.CanApproveAsManager(role))
        return Error(ctx, 403, "RFQ-ERR-900", "A aprovação gerencial cabe ao gestor de suprimentos ou administrador.");
    var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
    var (q, error) = await svc.ManagerDecisionAsync(actor, id, body.Decision, body.Reason);
    return error is not null
        ? Error(ctx, error.Code switch { "RFQ-ERR-020" => 409, "RFQ-ERR-030" => 422, _ => 400 }, error.Code, error.Message)
        : Ok(QuotationView(q!), ctx);
});

rfq.MapPost("/{id:guid}/director-decision", async (Guid id, QuotationDecisionRequest body, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = RoleOf(p);
    if (!QuotationService.CanApproveAsDirector(role))
        return Error(ctx, 403, "RFQ-ERR-900", "A aprovação da diretoria cabe ao diretor ou administrador.");
    var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
    var (q, error) = await svc.DirectorDecisionAsync(actor, id, body.Decision, body.Reason);
    return error is not null
        ? Error(ctx, error.Code switch { "RFQ-ERR-020" => 409, "RFQ-ERR-030" => 422, _ => 400 }, error.Code, error.Message)
        : Ok(QuotationView(q!), ctx);
});

rfq.MapPost("/{id:guid}/issue-po", async (Guid id, IssuePoRequest body, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = RoleOf(p);
    if (!QuotationService.CanConduct(role)) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não emite ordens de compra.");
    var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
    var (order, error) = await svc.IssuePurchaseOrderAsync(actor, id, body.Notes);
    return error is not null ? Error(ctx, 409, error.Code, error.Message)
        : Results.Json(new { data = PoView(order!), correlationId = CorrelationId(ctx) }, statusCode: 201);
});

rfq.MapPost("/{id:guid}/cancel", async (Guid id, ReasonRequest body, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = RoleOf(p);
    if (!QuotationService.CanConduct(role)) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não cancela cotações.");
    var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
    var (q, error) = await svc.CancelAsync(actor, id, body.Reason);
    return error is not null ? Error(ctx, error.Code == "RFQ-ERR-020" ? 409 : 400, error.Code, error.Message) : Ok(QuotationView(q!), ctx);
});

// ==== Portal do Fornecedor (RFQ-001 §5) ======================================
var portal = app.MapGroup("/api/v1/portal");

portal.MapPost("/login", async (PortalLoginRequest body, SupplierService svc, TokenService tokens, TimeProvider clock, HttpContext ctx) =>
{
    var supplier = await svc.PortalLoginAsync(body.TaxId, body.AccessKey);
    if (supplier is null)
        return Error(ctx, 401, "RFQ-ERR-051", "CNPJ/CPF ou chave de acesso inválidos, ou fornecedor sem acesso ao portal.");
    var token = tokens.CreateSupplierToken(supplier.Id, supplier.TradeName ?? supplier.LegalName, clock.GetUtcNow());
    return Ok(new
    {
        accessToken = token, tokenType = "Bearer", expiresIn = 3600,
        supplier = new { id = supplier.Id, name = supplier.TradeName ?? supplier.LegalName, taxId = supplier.TaxId },
    }, ctx);
}).RequireRateLimiting("auth");

static Guid? PortalSupplierId(ClaimsPrincipal p) =>
    p.FindFirstValue(ClaimTypes.Role) == "Supplier" && Guid.TryParse(p.FindFirstValue("supplierId"), out var id) ? id : null;

// o fornecedor só enxerga as próprias cotações — nunca dados de outros fornecedores
static object PortalQuotationView(Quotation q, Guid supplierId) => new
{
    id = q.Id, number = q.Number, kind = QKindLabel(q.Kind),
    open = q.Status == QuotationStatus.Open,
    status = q.Status == QuotationStatus.Open ? "ABERTA" : "ENCERRADA",
    deadline = q.Deadline, notes = q.Notes, createdAt = q.CreatedAt,
    items = q.Items.OrderBy(i => i.Sequence).Select(i => new
    {
        id = i.Id, sequence = i.Sequence, description = i.Description,
        quantity = i.Quantity, unitOfMeasure = i.UnitOfMeasure,
    }),
    myProposals = q.Proposals.Where(p => p.SupplierId == supplierId)
        .OrderByDescending(p => p.VersionNumber)
        .Select(p => new
        {
            id = p.Id, version = p.VersionNumber, totalValue = p.TotalValue,
            deliveryDays = p.DeliveryDays, paymentTerms = p.PaymentTerms, freightValue = p.FreightValue,
            validUntil = p.ValidUntil, notes = p.Notes, submittedAt = p.SubmittedAt,
            attachmentDocumentId = p.AttachmentDocumentId, attachmentFileName = p.AttachmentFileName,
            items = p.Items.Select(i => new { quotationItemId = i.QuotationItemId, unitPrice = i.UnitPrice, quantity = i.Quantity }),
        }),
};

portal.MapGet("/quotations", async (AppDbContext db, ClaimsPrincipal p, HttpContext ctx, string? number) =>
{
    if (PortalSupplierId(p) is not { } sid) return Error(ctx, 403, "RFQ-ERR-050", "Acesso exclusivo do Portal do Fornecedor.");
    var ids = await db.QuotationSuppliers.Where(s => s.SupplierId == sid).Select(s => s.QuotationId).ToListAsync();
    var query = db.Quotations.Include(q => q.Items).Include(q => q.Proposals).ThenInclude(x => x.Items)
        .Where(q => ids.Contains(q.Id));
    if (!string.IsNullOrWhiteSpace(number)) query = query.Where(q => q.Number.Contains(number.Trim().ToUpperInvariant()));
    var list = await query.OrderByDescending(q => q.CreatedAt).Take(100).ToListAsync();
    return Ok(new { items = list.Select(q => PortalQuotationView(q, sid)) }, ctx);
}).RequireAuthorization();

portal.MapGet("/quotations/{id:guid}", async (Guid id, AppDbContext db, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (PortalSupplierId(p) is not { } sid) return Error(ctx, 403, "RFQ-ERR-050", "Acesso exclusivo do Portal do Fornecedor.");
    if (!await db.QuotationSuppliers.AnyAsync(s => s.QuotationId == id && s.SupplierId == sid))
        return Error(ctx, 404, "RFQ-ERR-404", "Cotação não encontrada."); // isolamento: 404, nunca vaza existência
    var q = await db.Quotations.Include(x => x.Items).Include(x => x.Proposals).ThenInclude(x => x.Items)
        .SingleAsync(x => x.Id == id);
    return Ok(PortalQuotationView(q, sid), ctx);
}).RequireAuthorization();

portal.MapPost("/quotations/{id:guid}/proposal", async (Guid id, PortalProposalRequest body, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (PortalSupplierId(p) is not { } sid) return Error(ctx, 403, "RFQ-ERR-050", "Acesso exclusivo do Portal do Fornecedor.");
    var input = new ProposalInput(body.DeliveryDays, body.PaymentTerms, body.FreightValue, body.ValidUntil, body.Notes,
        (body.Items ?? []).Select(i => new ProposalItemInput(i.QuotationItemId, i.UnitPrice, i.Quantity)).ToList());
    var (proposal, error) = await svc.SubmitProposalAsync(id, sid, input, "PORTAL", p.FindFirstValue("name") ?? "Fornecedor");
    return error is not null
        ? Error(ctx, error.Code switch { "RFQ-ERR-050" => 403, "RFQ-ERR-020" => 409, _ => 400 }, error.Code, error.Message)
        : Results.Json(new
        {
            data = new { id = proposal!.Id, version = proposal.VersionNumber, totalValue = proposal.TotalValue },
            correlationId = CorrelationId(ctx),
        }, statusCode: 201);
}).RequireAuthorization();

portal.MapPost("/proposals/{proposalId:guid}/attachment", async (Guid proposalId, HttpRequest request, AppDbContext db, TimeProvider clock, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (PortalSupplierId(p) is not { } sid) return Error(ctx, 403, "RFQ-ERR-050", "Acesso exclusivo do Portal do Fornecedor.");
    var proposal = await db.Proposals.SingleOrDefaultAsync(x => x.Id == proposalId && x.SupplierId == sid);
    if (proposal is null) return Error(ctx, 404, "RFQ-ERR-404", "Proposta não encontrada.");
    if (!request.HasFormContentType) return Error(ctx, 400, "DOC-ERR-001", "Envie o arquivo como multipart/form-data.");
    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    if (file is null || file.Length == 0) return Error(ctx, 400, "DOC-ERR-001", "Nenhum arquivo enviado.");
    if (file.Length > StoredDocument.MaxSizeBytes) return Error(ctx, 400, "DOC-ERR-002", "Arquivo acima de 10 MB.");
    if (!StoredDocument.AllowedContentTypes.Contains(file.ContentType))
        return Error(ctx, 400, "DOC-ERR-003", "Formato não permitido: envie PDF, imagem (PNG/JPG) ou Office (XLSX/DOCX).");
    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    var doc = new StoredDocument
    {
        FileName = Path.GetFileName(file.FileName), ContentType = file.ContentType, SizeBytes = file.Length,
        Content = ms.ToArray(), EntityType = "PROPOSAL", EntityId = proposal.Id, SupplierId = sid,
        UploadedByLabel = p.FindFirstValue("name") ?? "Fornecedor", UploadedAt = clock.GetUtcNow(),
    };
    db.StoredDocuments.Add(doc);
    proposal.AttachmentDocumentId = doc.Id;
    proposal.AttachmentFileName = doc.FileName;
    await db.SaveChangesAsync();
    return Ok(new { documentId = doc.Id, fileName = doc.FileName }, ctx);
}).RequireAuthorization();

// anexo da proposta registrada internamente: o PDF/planilha que o fornecedor enviou por fora
app.MapPost("/api/v1/quotations/{id:guid}/proposals/{proposalId:guid}/attachment",
    async (Guid id, Guid proposalId, HttpRequest request, AppDbContext db, QuotationService svc,
           TimeProvider clock, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (!QuotationService.CanConduct(RoleOf(p)))
        return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não conduz o processo de cotação.");
    var proposal = await db.Proposals.SingleOrDefaultAsync(x => x.Id == proposalId && x.QuotationId == id);
    if (proposal is null) return Error(ctx, 404, "RFQ-ERR-404", "Proposta não encontrada neste processo.");
    if (!request.HasFormContentType) return Error(ctx, 400, "DOC-ERR-001", "Envie o arquivo como multipart/form-data.");
    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    if (file is null || file.Length == 0) return Error(ctx, 400, "DOC-ERR-001", "Nenhum arquivo enviado.");
    if (file.Length > StoredDocument.MaxSizeBytes) return Error(ctx, 400, "DOC-ERR-002", "Arquivo acima de 10 MB.");
    if (!StoredDocument.AllowedContentTypes.Contains(file.ContentType))
        return Error(ctx, 400, "DOC-ERR-003", "Formato não permitido: envie PDF, planilha (XLSX/XLS/CSV), imagem ou DOCX.");

    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    var doc = new StoredDocument
    {
        FileName = Path.GetFileName(file.FileName), ContentType = file.ContentType, SizeBytes = file.Length,
        Content = ms.ToArray(), EntityType = "PROPOSAL", EntityId = proposal.Id, SupplierId = proposal.SupplierId,
        UploadedByLabel = p.FindFirstValue("name") ?? "Suprimentos", UploadedAt = clock.GetUtcNow(),
    };
    db.StoredDocuments.Add(doc);
    proposal.AttachmentDocumentId = doc.Id;
    proposal.AttachmentFileName = doc.FileName;

    // registra no histórico do processo: a cotação recebida ficou arquivada
    var q = await svc.GetAsync(id);
    if (q is not null)
        svc.RecordAttachmentEvent(q, new Actor(ActorId(p), p.FindFirstValue("name") ?? "Suprimentos", RoleOf(p)),
            proposal.SupplierName, doc.FileName);
    await db.SaveChangesAsync();
    return Ok(new { documentId = doc.Id, fileName = doc.FileName }, ctx);
}).RequireAuthorization().AddEndpointFilter(RejectSupplierRole());

// ==== Documentos (download autorizado por papel/vínculo) =====================
app.MapGet("/api/v1/documents/{id:guid}", async (Guid id, AppDbContext db, ClaimsPrincipal p, HttpContext ctx) =>
{
    var doc = await db.StoredDocuments.SingleOrDefaultAsync(d => d.Id == id);
    if (doc is null) return Error(ctx, 404, "DOC-ERR-404", "Documento não encontrado.");
    var role = RoleOf(p);
    if (role == "Supplier")
    {
        if (PortalSupplierId(p) != doc.SupplierId) return Error(ctx, 404, "DOC-ERR-404", "Documento não encontrado.");
    }
    else if (!QuotationService.CanView(role) && role != Roles.Auditor)
        return Error(ctx, 403, "DOC-ERR-900", "Seu papel não acessa documentos do processo.");
    return Results.File(doc.Content, doc.ContentType, doc.FileName);
}).RequireAuthorization();

// ==== Cadastro da Empresa (cabeçalho da OC) ==================================
app.MapGet("/api/v1/company", async (AppDbContext db, HttpContext ctx) =>
{
    var c = await db.CompanyProfiles.FirstOrDefaultAsync();
    return Ok(c is null ? new { } : (object)c, ctx);
}).RequireAuthorization().AddEndpointFilter(RejectSupplierRole());

app.MapPut("/api/v1/company", async (CompanyProfileRequest body, AppDbContext db, TimeProvider clock, ClaimsPrincipal p, HttpContext ctx) =>
{
    if (RoleOf(p) != Roles.SystemAdministrator)
        return Error(ctx, 403, "IAM-ERR-018", "Somente o administrador mantém o cadastro da empresa.");
    var c = await db.CompanyProfiles.FirstOrDefaultAsync();
    if (c is null) { c = new CompanyProfile(); db.CompanyProfiles.Add(c); }
    c.LegalName = body.LegalName.Trim();
    c.Address = body.Address.Trim();
    c.District = body.District?.Trim();
    c.City = body.City.Trim();
    c.State = body.State.Trim().ToUpperInvariant();
    c.Zip = body.Zip.Trim();
    c.TaxId = body.TaxId.Trim();
    c.StateRegistration = body.StateRegistration?.Trim();
    c.Phone = body.Phone?.Trim();
    c.Email = body.Email?.Trim();
    c.DeliveryAddress = body.DeliveryAddress?.Trim();
    c.DeliveryTaxId = body.DeliveryTaxId?.Trim();
    c.StandardClauses = body.StandardClauses?.Trim();
    c.PaymentPolicy = body.PaymentPolicy?.Trim();
    c.UpdatedAt = clock.GetUtcNow();
    c.UpdatedByLabel = p.FindFirstValue("name") ?? "Administrador";
    await db.SaveChangesAsync();
    return Ok(c, ctx);
}).RequireAuthorization();

// ==== PDF da Ordem de Compra (modelo oficial) ================================
app.MapGet("/api/v1/purchase-orders/{id:guid}/pdf", async (Guid id, AppDbContext db, QuotationService qsvc, TimeProvider clock, ClaimsPrincipal p, HttpContext ctx) =>
{
    var role = RoleOf(p);
    if (!PurchaseOrderService.CanView(role) && !QuotationService.CanView(role))
        return Error(ctx, 403, "PO-ERR-900", "Seu papel não acessa a OC.");
    var order = await db.PurchaseOrders.Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == id);
    if (order is null) return Error(ctx, 404, "PO-ERR-404", "Pedido não encontrado.");
    var supplier = await db.Suppliers.SingleAsync(s => s.Id == order.SupplierId);
    var company = await db.CompanyProfiles.FirstOrDefaultAsync();
    var quotation = order.QuotationId is null ? null : await qsvc.GetAsync(order.QuotationId.Value);

    // CNPJ da empresa do CC de origem prevalece no cabeçalho (fallback: perfil padrão)
    var ccCode = quotation?.CostCenter
        ?? (order.SourcePrId is null ? null
            : await db.Requisitions.Where(r => r.Id == order.SourcePrId).Select(r => r.CostCenter).FirstOrDefaultAsync());
    if (!string.IsNullOrWhiteSpace(ccCode))
    {
        var normalizedCc = ccCode.Trim().ToUpperInvariant();
        var companyId = await db.CostCenters.Where(c => c.Code == normalizedCc)
            .Select(c => c.CompanyId).FirstOrDefaultAsync();
        var ccCompany = companyId is null ? null
            : await db.Companies.SingleOrDefaultAsync(c => c.Id == companyId && c.Active);
        if (ccCompany is not null)
            company = new CompanyProfile
            {
                LegalName = ccCompany.LegalName,
                TaxId = ccCompany.TaxId.Length == 14
                    ? $"{ccCompany.TaxId[..2]}.{ccCompany.TaxId[2..5]}.{ccCompany.TaxId[5..8]}/{ccCompany.TaxId[8..12]}-{ccCompany.TaxId[12..]}"
                    : ccCompany.TaxId,
                StateRegistration = ccCompany.StateRegistration ?? company?.StateRegistration,
                Address = ccCompany.Address, District = ccCompany.District,
                City = ccCompany.City, State = ccCompany.State, Zip = ccCompany.Zip,
                Phone = ccCompany.Phone ?? company?.Phone, Email = ccCompany.Email ?? company?.Email,
                DeliveryAddress = company?.DeliveryAddress, DeliveryTaxId = company?.DeliveryTaxId,
                StandardClauses = company?.StandardClauses, PaymentPolicy = company?.PaymentPolicy,
            };
    }

    var pdf = PurchaseOrderPdf.Generate(order, supplier, company, quotation);
    var doc = new StoredDocument
    {
        FileName = $"{order.Number}.pdf", ContentType = "application/pdf", SizeBytes = pdf.Length,
        Content = pdf, EntityType = "PURCHASE_ORDER_PDF", EntityId = order.Id,
        UploadedByLabel = p.FindFirstValue("name") ?? "Sistema", UploadedAt = clock.GetUtcNow(),
    };
    db.StoredDocuments.Add(doc);
    order.PdfDocumentId = doc.Id;
    if (quotation is not null)
        qsvc.RecordPdfEvent(quotation, new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role), doc.Id, order.Number);
    await db.SaveChangesAsync();
    return Results.File(pdf, "application/pdf", $"{order.Number}.pdf");
}).RequireAuthorization().AddEndpointFilter(RejectSupplierRole());

app.MapGet("/portal", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "portal.html"), "text/html")).AllowAnonymous();

app.MapFallbackToFile("index.html");

app.Run();

// ---- Autorização por módulo (cadastro do usuário) ----------------------------
static string[] ModulesOf(ClaimsPrincipal p)
{
    var claim = p.FindFirst("modules");
    if (claim is null) // token antigo sem o claim: cai no padrão do papel
        return AppModules.DefaultsFor(p.FindFirstValue(ClaimTypes.Role) ?? "");
    return claim.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

// O token do Portal do Fornecedor (papel "Supplier") nunca acessa módulos internos (RFQ-001 §5)
static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> RejectSupplierRole() =>
    async (ic, next) =>
    {
        if (ic.HttpContext.User.FindFirstValue(ClaimTypes.Role) == "Supplier")
            return Error(ic.HttpContext, 403, "RFQ-ERR-050", "Acesso restrito ao Portal do Fornecedor.");
        return await next(ic);
    };

static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> RequireModules(params string[] modules) =>
    async (ic, next) =>
    {
        var granted = ModulesOf(ic.HttpContext.User);
        if (modules.Any(granted.Contains)) return await next(ic);
        return Error(ic.HttpContext, 403, "IAM-ERR-018", "Seu usuário não tem autorização para este módulo. Fale com o administrador.");
    };

static object ToResponse(AuthTokens t) => new
{
    accessToken = t.AccessToken,
    tokenType = "Bearer",
    expiresIn = t.ExpiresInSeconds,
    refreshToken = t.RefreshToken,
    user = new { id = t.User.Id, email = t.User.Email, name = t.User.Name, role = t.User.Role, modules = AppModules.EffectiveFor(t.User) },
};

public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record CreateUserRequest(string Email, string Name, string Role, string Password, List<string>? Modules,
    List<string>? CostCenters, Guid? DirectorId);
public record UpdateUserRequest(string? Name, string? Role, bool? Active, List<string>? Modules,
    List<string>? CostCenters, Guid? DirectorId, bool? ClearDirector);
public record ResetPasswordRequest(string NewPassword);
public record ItemRequest(string? Description, decimal Quantity, string? UnitOfMeasure, decimal? EstimatedUnitPrice, string? Notes, Guid? CatalogItemId);
public record CreateRequisitionRequest(string Justification, string CostCenter, string? Priority, DateOnly? NeededBy, List<ItemRequest>? Items, string? Kind,
    string? NeedType, string? DeliveryLocation, string? Company, string? InternalNotes);
public record ProductFamilyRequest(string? Name, string? Notes);
public record UpdateProductFamilyRequest(string? Name, string? Notes, bool? Active);
public record ItemSupplierRequest(string? SupplierName, string? TaxId, string? Contact,
    string? SupplierItemCode, decimal? LastPrice, string? Notes);
public record CreateCatalogItemRequest(string? Code, string Description, string Family, string? UnitOfMeasure,
    decimal? ReferencePrice, bool? StockControlled, decimal? MinimumQty, List<ItemSupplierRequest>? Suppliers,
    bool? Purchasable, string? ProductType, string? CaNumber);
public record UpdateCatalogItemRequest(string? Description, string? Family, string? UnitOfMeasure,
    decimal? ReferencePrice, bool? Active, bool? StockControlled, decimal? MinimumQty, bool? ClearMinimum,
    List<ItemSupplierRequest>? Suppliers, bool? Purchasable, string? ProductType, string? CaNumber);
public record CreateLocationRequest(string Code, string Name);
public record EntryRequest(Guid CatalogItemId, Guid LocationId, decimal Quantity, string OriginReference, string? Origin);
public record IssueRequest(Guid CatalogItemId, Guid LocationId, decimal Quantity, string OriginReference);
public record MaterialItemRequest(Guid CatalogItemId, decimal Quantity);
public record CreateMaterialRequisitionRequest(string CostCenter, string? Notes, List<MaterialItemRequest>? Items);
public record FulfillRequest(Guid LocationId);
public record CreateSupplierRequest(string LegalName, string? TradeName, string TaxId, string? Email, string? Phone);
public record UpdateSupplierRequest(string? TradeName, string? Email, string? Phone, bool? Active);
public record PoItemRequest(string Description, decimal Quantity, string? UnitOfMeasure, decimal? UnitPrice, Guid? CatalogItemId);
public record CreatePurchaseOrderRequest(Guid SupplierId, string? Notes, List<PoItemRequest>? Items, Guid? SourcePrId);
public record ReceiveOrderRequest(Guid LocationId);
public record CreateCostCenterRequest(string? Code, string Name, string? Region, Guid? ManagerUserId, string? ClientName, Guid? CompanyId);
public record UpdateCostCenterRequest(string? Name, string? Region, Guid? ManagerUserId, string? ClientName, bool? Active, Guid? CompanyId);
public record AssignTicketRequest(string? Kind, Guid Id, Guid? ResponsibleId);
public record CreateCompanyRequest(string LegalName, string TaxId, string? StateRegistration, string Address,
    string? District, string City, string State, string Zip, string? Phone, string? Email);
public record UpdateCompanyRequest(string? LegalName, string? StateRegistration, string? Address, string? District,
    string? City, string? State, string? Zip, string? Phone, string? Email, bool? Active);
public record CreateQuotationRequest(Guid PrId, string? Kind, DateOnly? Deadline, string? Notes);
public record InviteSuppliersRequest(List<Guid>? SupplierIds);
public record ProposalItemRequest(Guid QuotationItemId, decimal UnitPrice, decimal? Quantity);
public record InternalProposalRequest(Guid SupplierId, int? DeliveryDays, string? PaymentTerms, decimal? FreightValue, DateOnly? ValidUntil, string? Notes, List<ProposalItemRequest>? Items);
public record SelectWinnerRequest(Guid ProposalId, List<string>? Criteria, string Justification);
public record QuotationDecisionRequest(string Decision, string? Reason);
public record IssuePoRequest(string? Notes);
public record PortalLoginRequest(string TaxId, string AccessKey);
public record PortalProposalRequest(int? DeliveryDays, string? PaymentTerms, decimal? FreightValue, DateOnly? ValidUntil, string? Notes, List<ProposalItemRequest>? Items);
public record CompanyProfileRequest(string LegalName, string Address, string? District, string City, string State, string Zip, string TaxId, string? StateRegistration, string? Phone, string? Email, string? DeliveryAddress, string? DeliveryTaxId, string? StandardClauses, string? PaymentPolicy);
public record UpdateRequisitionRequest(string? Justification, string? CostCenter, string? Priority, DateOnly? NeededBy, bool? ClearNeededBy);
public record ReasonRequest(string? Reason);
public record DecisionRequest(string? Reason, string? Comments);

public partial class Program;
