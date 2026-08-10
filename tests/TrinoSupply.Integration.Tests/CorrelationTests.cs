using Xunit;

namespace TrinoSupply.Integration.Tests;

/// <summary>Correlação de request (observabilidade): o <c>X-Correlation-ID</c> é ecoado ou gerado.</summary>
[Collection("pilot")]
public sealed class CorrelationTests(PilotFixture fixture)
{
    private const string Header = "X-Correlation-ID";

    [Fact]
    public async Task Ecoa_o_correlation_id_recebido()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        req.Headers.Add(Header, "teste-corr-123");
        using var res = await fixture.Client().SendAsync(req);

        Assert.True(res.Headers.TryGetValues(Header, out var values));
        Assert.Equal("teste-corr-123", values!.Single());
    }

    [Fact]
    public async Task Gera_um_correlation_id_quando_ausente()
    {
        using var res = await fixture.Client().GetAsync("/health/live");

        Assert.True(res.Headers.TryGetValues(Header, out var values));
        var id = values!.Single();
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.True(id.Length >= 16); // GUID "n" tem 32 chars
    }
}
