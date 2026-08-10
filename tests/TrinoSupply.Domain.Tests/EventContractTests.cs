using System.Text.Json;
using TrinoSupply.Procurement.Domain;
using Xunit;

namespace TrinoSupply.Domain.Tests;

/// <summary>
/// Contrato de fio dos eventos publicados (ARC-005): o consumidor de outro serviço/BC depende destes
/// nomes de campo. Se alguém renomear/remover um campo, este teste quebra ANTES de furar o consumidor.
/// </summary>
public class EventContractTests
{
    private static readonly JsonSerializerOptions Options = new();

    [Fact]
    public void OrderIssued_mantem_o_contrato_de_campos()
    {
        var ev = new OrderIssued(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid(), 664, Guid.NewGuid(), 1234.56m);
        using var doc = JsonSerializer.SerializeToDocument(ev, Options);
        var root = doc.RootElement;

        foreach (var field in new[] { "EventId", "OccurredAt", "CompanyId", "OrderId", "Number", "SupplierId", "NetValue" })
            Assert.True(root.TryGetProperty(field, out _), $"OrderIssued deve expor '{field}' no JSON.");

        Assert.Equal(664, root.GetProperty("Number").GetInt64());
        Assert.Equal(1234.56m, root.GetProperty("NetValue").GetDecimal());
    }

    [Fact]
    public void OrderCancelled_mantem_o_contrato_de_campos()
    {
        var ev = new OrderCancelled(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 99.90m);
        using var doc = JsonSerializer.SerializeToDocument(ev, Options);
        var root = doc.RootElement;

        foreach (var field in new[] { "EventId", "OccurredAt", "CompanyId", "OrderId", "SupplierId", "NetValue" })
            Assert.True(root.TryGetProperty(field, out _), $"OrderCancelled deve expor '{field}' no JSON.");
    }
}
