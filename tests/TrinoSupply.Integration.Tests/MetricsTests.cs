using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>Métricas Prometheus (OPS-001 §observabilidade): /metrics expõe request + ações de negócio.</summary>
[Collection("pilot")]
public sealed class MetricsTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);

    [Fact]
    public async Task Metrics_expoe_prometheus_com_negocio_e_request()
    {
        var c = fixture.Client();

        // Gera uma ação de negócio → incrementa trino.business.actions (company.provisioned/user.registered).
        var (status, _) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = "Metrics Co", taxId = $"9{Guid.NewGuid():N}"[..14], adminSubject = $"adm-{Guid.NewGuid():N}",
            adminEmail = $"{Guid.NewGuid():N}@met.com", adminName = "Adm", adminPassword = "senha12345",
        });
        Assert.Equal(201, status);

        var res = await c.GetAsync("/metrics");
        Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();

        Assert.Contains("trino_business_actions_total", body);          // métrica de negócio
        Assert.Contains("action=\"company.provisioned\"", body);        // dimensão da ação
        Assert.Contains("http_server_request_duration", body);          // métrica de request (ASP.NET)
    }
}
