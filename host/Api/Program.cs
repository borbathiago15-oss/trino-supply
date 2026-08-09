using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using TrinoSupply.Api.Auth;
using TrinoSupply.Api.Iam;
using TrinoSupply.Api.Multitenancy;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.BuildingBlocks.Security;
using TrinoSupply.Api.Materials;
using TrinoSupply.Api.Procurement;
using TrinoSupply.Foundation.Application.Auth;
using TrinoSupply.Foundation.Infrastructure;
using TrinoSupply.Materials.Infrastructure;
using TrinoSupply.Procurement.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// --- Serviços ---------------------------------------------------------------
builder.Services.AddFoundationInfrastructure(builder.Configuration);
builder.Services.AddMaterialsInfrastructure(builder.Configuration);
builder.Services.AddProcurementInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();

// Multi-tenant: o tenant vem do JWT (FD-001-01). Sobrepõe o NullTenantContext do host de infra.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// AuthN (JWT Bearer) — SEC-001/003. IdP LOCAL: o próprio Trino emite e valida tokens usando um
// key-ring (rotação de chaves — SEC-001). FAIL-CLOSED: só aceitamos tokens efetivamente validados
// (assinatura por kid + emissor + audiência + expiração). Sem chaves nem Authority → nada é aceito.
var keyRing = JwtKeyRing.FromConfig(builder.Configuration);
var jwtAuthority = builder.Configuration["Jwt:Authority"];

if (!keyRing.HasKeys && string.IsNullOrWhiteSpace(jwtAuthority))
{
    // Sem confiança configurada: em produção é erro fatal (não subir "aberto" por engano).
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "AuthN não configurada: defina Jwt:Keys (IdP local) ou Jwt:Authority (OIDC) — SEC-001/SEC-004. " +
            "Recusando iniciar para não expor a API sem validação de token.");
    }
    // Em dev, seguimos; sem chaves todo token protegido resulta em 401 (fail-closed).
}

builder.Services.AddSingleton(keyRing);
builder.Services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = string.IsNullOrWhiteSpace(jwtAuthority) ? null : jwtAuthority;
        options.Audience = keyRing.Audience;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.MapInboundClaims = false; // preserva o claim 'sub' com o nome original

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidateAudience = !string.IsNullOrWhiteSpace(keyRing.Audience),
            ValidAudience = keyRing.Audience,
            ValidateIssuer = keyRing.HasKeys || !string.IsNullOrWhiteSpace(jwtAuthority),
            ValidIssuer = keyRing.HasKeys ? keyRing.Issuer : jwtAuthority,
            ClockSkew = TimeSpan.FromSeconds(30),
            // Todas as chaves do ring validam (janela de rotação); a assinatura escolhe por kid.
            IssuerSigningKeys = keyRing.HasKeys ? keyRing.ValidationKeys : null
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// --- Pipeline ---------------------------------------------------------------
app.UseAuthentication();
app.UseAuthorization();

// Health/readiness (aberto) para orquestração de containers e Cloudflare (ARC-003 §2, OPS-001 §5).
app.MapHealthChecks("/health");

var v1 = app.MapGroup("/api/v1");

v1.MapGet("/health", () => Results.Ok(new
{
    service = "trino-supply-api",
    status = "ok",
    schemaVersion = "0.0.0-skeleton"
}));

// Ecoa o tenant resolvido do JWT — prova a fatia multi-tenant (protegido por AuthN).
v1.MapGet("/whoami", (ITenantContext tenant) =>
        tenant.HasTenant
            ? Results.Ok(new { companyId = tenant.CompanyId.Value })
            : Results.Unauthorized())
    .RequireAuthorization();

// AuthN (IdP local): login, refresh, logout.
app.MapAuthEndpoints();

// IAM (FD-001-01): provisionamento de empresa + gestão de usuários (deny-by-default).
app.MapIamEndpoints();

// Materiais (MMS-002): catálogo de itens e unidades + conversão.
app.MapMaterialsEndpoints();

// Compras (PR-001): requisição + aprovação com SoD; ponte da reposição.
app.MapProcurementEndpoints();

app.Run();

// Exposto para testes de integração (WebApplicationFactory).
public partial class Program;
