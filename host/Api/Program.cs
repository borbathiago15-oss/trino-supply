using Microsoft.AspNetCore.Authentication.JwtBearer;
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

// AuthN (JWT Bearer) — SEC-001/003. Parâmetros de validação via config "Jwt".
// TODO(GO-001 · sprint 1): Authority/Audience/chaves reais (rotação 90 dias — SEC-001 §5).
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Jwt:Authority"];
        options.Audience = builder.Configuration["Jwt:Audience"];
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
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
