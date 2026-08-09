using TrinoSupply.Foundation.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// --- Serviços ---------------------------------------------------------------
builder.Services.AddFoundationInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();
// TODO(GO-001 · sprint 1): AuthN JWT (FD-001-01), AuthZ (escopo→RBAC→ABAC), multi-tenant + RLS,
// Outbox/RabbitMQ, OpenAPI, versionamento /api/v1 (ARC-004 §4, SEC-001).

var app = builder.Build();

// --- Pipeline ---------------------------------------------------------------
// Health/readiness para orquestração de containers e Cloudflare (ARC-003 §2, OPS-001 §5).
app.MapHealthChecks("/health");

var v1 = app.MapGroup("/api/v1");
v1.MapGet("/health", () => Results.Ok(new
{
    service = "trino-supply-api",
    status = "ok",
    schemaVersion = "0.0.0-skeleton"
}));

app.Run();

// Exposto para testes de integração (WebApplicationFactory).
public partial class Program;
