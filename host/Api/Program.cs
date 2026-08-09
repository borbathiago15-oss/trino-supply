using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using TrinoSupply.Api.Multitenancy;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.Foundation.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// --- Serviços ---------------------------------------------------------------
builder.Services.AddFoundationInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();

// Multi-tenant: o tenant vem do JWT (FD-001-01). Sobrepõe o NullTenantContext do host de infra.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();

// AuthN (JWT Bearer) — SEC-001/003. FAIL-CLOSED: só aceitamos tokens efetivamente validados
// (assinatura + emissor + audiência + expiração). Sem um provedor de identidade (Authority) OU
// uma chave simétrica de desenvolvimento configurada, NENHUM token é aceito — nunca validação frouxa.
var jwtAuthority = builder.Configuration["Jwt:Authority"];
var jwtAudience = builder.Configuration["Jwt:Audience"];
var jwtDevKey = builder.Configuration["Jwt:DevSigningKey"];

if (string.IsNullOrWhiteSpace(jwtAuthority) && string.IsNullOrWhiteSpace(jwtDevKey))
{
    // Sem confiança configurada: em produção é erro fatal (não subir "aberto" por engano).
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "AuthN não configurada: defina Jwt:Authority (produção) — SEC-001/SEC-004. " +
            "Recusando iniciar para não expor a API sem validação de token.");
    }
    // Em dev, seguimos, mas sem chaves de assinatura todo token protegido resulta em 401 (fail-closed).
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = string.IsNullOrWhiteSpace(jwtAuthority) ? null : jwtAuthority;
        options.Audience = jwtAudience;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidateAudience = !string.IsNullOrWhiteSpace(jwtAudience),
            ValidAudience = jwtAudience,
            ValidateIssuer = !string.IsNullOrWhiteSpace(jwtAuthority),
            ValidIssuer = jwtAuthority,
            ClockSkew = TimeSpan.FromSeconds(30),
            // Chave simétrica só para DEV/testes; em produção as chaves vêm do Authority (OIDC/JWKS).
            IssuerSigningKey = string.IsNullOrWhiteSpace(jwtDevKey)
                ? null
                : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtDevKey))
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

app.Run();

// Exposto para testes de integração (WebApplicationFactory).
public partial class Program;
