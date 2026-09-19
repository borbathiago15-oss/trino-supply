using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TrinoSupply.BuildingBlocks.Abstractions;
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

// Licença Community do QuestPDF (uso gratuito) — necessária para gerar o PDF da OC.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// PaaS (Railway etc.): DATABASE_URL/PORT/nomes amigáveis → configuração. ANTES de registrar os
// serviços, pois as connection strings são lidas no registro (deploy/railway.md).
TrinoSupply.Api.Startup.CloudEnvironment.ApplyTo(builder);

// Observabilidade: logs estruturados em JSON com escopos (inclui a correlação de request). Uma linha
// por evento, amigável a coletores (OPS-001 §5). Em Development mantém-se legível via IncludeScopes.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o =>
{
    o.IncludeScopes = true;
    o.UseUtcTimestamp = true;
    o.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
});

// --- Serviços ---------------------------------------------------------------
builder.Services.AddFoundationInfrastructure(builder.Configuration);
builder.Services.AddMaterialsInfrastructure(builder.Configuration);
builder.Services.AddProcurementInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck<TrinoSupply.Api.Health.DatabaseHealthCheck>("postgres", tags: ["ready"]);

// Métricas (OPS-001 §observabilidade): OpenTelemetry expõe /metrics (Prometheus) com métricas de
// request (ASP.NET Core) + as ações de negócio (IUsageMetrics → contador trino.business.actions).
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("trino-supply-api"))
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddMeter(TrinoSupply.Api.Observability.OpenTelemetryUsageMetrics.MeterName)
        .AddPrometheusExporter())
    // Tracing distribuído: spans de request (ASP.NET Core) + queries (Npgsql). Exporta via OTLP quando
    // OTEL_EXPORTER_OTLP_ENDPOINT está definido (ex.: collector/Tempo/Jaeger); console para dev/validação.
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation();
        t.AddSource("Npgsql");
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            t.AddOtlpExporter();
        if (builder.Configuration.GetValue<bool>("OTEL_CONSOLE_TRACING"))
            t.AddConsoleExporter();
    });
// Sobrepõe o LoggingUsageMetrics do Foundation por métricas exportáveis (mesma interface, sem tocar call-sites).
builder.Services.AddSingleton<IUsageMetrics, TrinoSupply.Api.Observability.OpenTelemetryUsageMetrics>();

// Fail-fast de segurança (SEC-004): em produção, recusa iniciar se conectar ao banco como papel
// privilegiado (SUPERUSER/BYPASSRLS) ou sem connection string. Melhor não subir do que subir inseguro.
builder.Services.AddHostedService<TrinoSupply.Api.Security.DatabasePrivilegeGuard>();

// Multi-tenant: o tenant vem do JWT (FD-001-01). Sobrepõe o NullTenantContext do host de infra.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// Roteamento pós-aprovação do Pedido unificado (v2): interno → baixa; sem estoque → compra.
builder.Services.AddScoped<TrinoSupply.Api.Procurement.StockFulfillment>();
// Entrada em estoque do recebimento de mercadoria (MMS-005).
builder.Services.AddScoped<TrinoSupply.Api.Procurement.ReceiptStockEntry>();

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
// Correlação primeiro, para que TODO log (inclusive de auth) já saia com o CorrelationId; em seguida
// o guarda-chuva de exceções — nenhum erro interno vaza cru para o cliente.
app.UseMiddleware<TrinoSupply.Api.Observability.CorrelationMiddleware>();
app.UseMiddleware<TrinoSupply.Api.Observability.ApiExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

// Health/readiness (aberto) para orquestração de containers e Cloudflare (ARC-003 §2, OPS-001 §5).
//   /health/live  → liveness: processo de pé (não checa dependências — evita reinício por falha transitória).
//   /health/ready → readiness: dependências OK (Postgres) — controla entrada em rota / rolling deploy.
//   /health       → agregado (todas as checagens) para inspeção.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = TrinoSupply.Api.Health.HealthJson.WriteAsync,
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("ready"),
    ResponseWriter = TrinoSupply.Api.Health.HealthJson.WriteAsync,
});
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = TrinoSupply.Api.Health.HealthJson.WriteAsync,
});

// Métricas Prometheus em /metrics (scraping). Aberto — restrinja por rede/borda em produção (ADR-016).
app.MapPrometheusScrapingEndpoint();

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

// PaaS: MIGRATE_ON_STARTUP=true aplica as migrations + roles (substitui o bootstrap do compose).
await TrinoSupply.Api.Startup.CloudEnvironment.MigrateIfRequestedAsync(app);

app.Run();

// Exposto para testes de integração (WebApplicationFactory).
public partial class Program;
