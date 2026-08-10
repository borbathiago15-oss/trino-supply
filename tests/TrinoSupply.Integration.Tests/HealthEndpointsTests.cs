using System.Net;
using Xunit;

namespace TrinoSupply.Integration.Tests;

/// <summary>Sondas de saúde (OPS-001 §5): liveness sempre de pé; readiness reflete o Postgres real.</summary>
[Collection("pilot")]
public sealed class HealthEndpointsTests(PilotFixture fixture)
{
    [Fact]
    public async Task Liveness_responde_200()
    {
        var res = await fixture.Client().GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", body);
    }

    [Fact]
    public async Task Readiness_com_banco_disponivel_responde_200_e_lista_postgres()
    {
        var res = await fixture.Client().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("postgres", body);
        Assert.Contains("Healthy", body);
    }
}
