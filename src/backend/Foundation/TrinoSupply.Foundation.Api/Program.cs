using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Auth;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
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

public partial class Program;
