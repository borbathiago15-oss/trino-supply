using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Auth;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
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
builder.Services.AddScoped<RequisitionService>();

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

auth.MapGet("/me", (ClaimsPrincipal principal, HttpContext ctx) =>
{
    var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
              ?? principal.FindFirstValue("sub");
    return Ok(new
    {
        id = sub,
        email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email"),
        name = principal.FindFirstValue("name"),
        role = principal.FindFirstValue(ClaimTypes.Role),
    }, ctx);
}).RequireAuthorization();

// ---- Gestão de usuários (exclusiva do SystemAdministrator) -------------------
var users = app.MapGroup("/api/v1/users")
    .RequireAuthorization(p => p.RequireRole(Roles.SystemAdministrator));

static object UserView(User u) => new
{
    id = u.Id, email = u.Email, name = u.Name, role = u.Role, active = u.Active,
    createdAt = u.CreatedAt, updatedAt = u.UpdatedAt,
};

static Guid ActorId(ClaimsPrincipal p) =>
    Guid.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue("sub"), out var id)
        ? id : Guid.Empty;

users.MapGet("/", async (UserService svc, HttpContext ctx) =>
    Ok(new { items = (await svc.ListAsync()).Select(UserView), roles = UserService.ValidRoles }, ctx));

users.MapPost("/", async (CreateUserRequest body, UserService svc, HttpContext ctx) =>
{
    var (user, error) = await svc.CreateAsync(body.Email, body.Name, body.Role, body.Password);
    return error is not null
        ? Error(ctx, error.Code == "IAM-ERR-014" ? 409 : 400, error.Code, error.Message)
        : Results.Json(new { data = UserView(user!), correlationId = CorrelationId(ctx) }, statusCode: 201);
});

users.MapPatch("/{id:guid}", async (Guid id, UpdateUserRequest body, UserService svc, ClaimsPrincipal principal, HttpContext ctx) =>
{
    var (user, error) = await svc.UpdateAsync(id, ActorId(principal), body.Name, body.Role, body.Active);
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

static object PrView(PurchaseRequisition r) => new
{
    id = r.Id,
    number = r.Number,
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
    costCenter = r.CostCenter,
    currency = r.Currency,
    requesterId = r.RequesterId,
    requesterLabel = r.RequesterLabel,
    totalEstimatedValue = r.TotalEstimatedValue,
    decisionReason = r.DecisionReason,
    decidedByLabel = r.DecidedByLabel,
    decidedAt = r.DecidedAt,
    submittedAt = r.SubmittedAt,
    items = r.Items.OrderBy(i => i.Sequence).Select(i => new
    {
        itemId = i.Id, sequence = i.Sequence, description = i.Description,
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
    return Ok(new { items = (await svc.ListAsync(actor, filter)).Select(PrView) }, ctx);
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
    var items = (body.Items ?? []).Select(i => new ItemInput(i.Description, i.Quantity, i.UnitOfMeasure, i.EstimatedUnitPrice, i.Notes)).ToList();
    var (pr, error) = await svc.CreateAsync(actor, body.Justification, body.CostCenter, body.Priority, body.NeededBy, items);
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
    var (pr, error) = await svc.AddItemAsync(actor, id, new ItemInput(body.Description, body.Quantity, body.UnitOfMeasure, body.EstimatedUnitPrice, body.Notes));
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
    return Ok(new { items = (await svc.PendingApprovalsAsync(actor)).Select(PrView) }, ctx);
}).RequireAuthorization();

app.MapFallbackToFile("index.html");

app.Run();

static object ToResponse(AuthTokens t) => new
{
    accessToken = t.AccessToken,
    tokenType = "Bearer",
    expiresIn = t.ExpiresInSeconds,
    refreshToken = t.RefreshToken,
    user = new { id = t.User.Id, email = t.User.Email, name = t.User.Name, role = t.User.Role },
};

public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record CreateUserRequest(string Email, string Name, string Role, string Password);
public record UpdateUserRequest(string? Name, string? Role, bool? Active);
public record ResetPasswordRequest(string NewPassword);
public record ItemRequest(string Description, decimal Quantity, string? UnitOfMeasure, decimal? EstimatedUnitPrice, string? Notes);
public record CreateRequisitionRequest(string Justification, string CostCenter, string? Priority, DateOnly? NeededBy, List<ItemRequest>? Items);
public record UpdateRequisitionRequest(string? Justification, string? CostCenter, string? Priority, DateOnly? NeededBy, bool? ClearNeededBy);
public record ReasonRequest(string? Reason);
public record DecisionRequest(string? Reason, string? Comments);

public partial class Program;
