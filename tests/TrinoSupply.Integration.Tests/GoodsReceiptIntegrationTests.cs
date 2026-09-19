using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// MMS-005 — recebimento de mercadoria pela stack HTTP real. Cenário de aceite: OC de 50 unidades,
/// chegam as 50 com 2 avariadas → o estoque do Almox sobe exatamente +48, a avaria fica registrada
/// no histórico da entrega e a OC passa a "Recebida". Entregas parciais acumulam até completar.
/// </summary>
[Collection("pilot")]
public sealed class GoodsReceiptIntegrationTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken);
    private record UserResp(Guid UserId);
    private record RoleResp(Guid RoleId);
    private record ReqResp(Guid RequisitionId);
    private record OrderResp(Guid OrderId);
    private record BalanceResp(decimal Quantity);
    private record ReceiptResp(Guid ReceiptId, bool OrderComplete, bool HasOccurrence, bool StockPosted,
        string[] CreditedItems, string[] ItemsNotInCatalog, string? StockError);
    private record ReceiptLineRow(Guid OrderLineId, string ItemCode, decimal QuantityOrdered,
        decimal QuantityReceived, decimal QuantityDamaged, decimal NetQuantity, string Occurrence, string? OccurrenceNote);
    private record ReceiptRow(Guid Id, string InvoiceNumber, bool StockPosted, ReceiptLineRow[] Lines);
    private record PendingLineRow(Guid OrderLineId, string ItemCode, decimal QuantityOrdered,
        decimal QuantityAlreadyReceived, decimal QuantityPending);
    private record SummaryRow(Guid OrderId, long Number, string Status, PendingLineRow[] Lines, ReceiptRow[] Receipts);

    private const string Password = "senha12345";

    /// <summary>Monta empresa, aprovadores, cadastros, item no Almox e uma OC emitida do item pedido.</summary>
    private async Task<(HttpClient c, string admin, Guid orderId, Guid orderLineId)> OcEmitidaAsync(
        string itemCode, decimal quantidade)
    {
        var c = fixture.Client();
        var email = $"{Guid.NewGuid():N}@rec.com";
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = "Recebe Co", taxId = $"3{Guid.NewGuid():N}"[..14], adminSubject = $"adm-{Guid.NewGuid():N}",
            adminEmail = email, adminName = "Adm", adminPassword = Password,
        });
        var (_, login) = await PostAsync<LoginResp>(c, "/api/v1/auth/login",
            new { companyId = comp!.CompanyId, email, password = Password });
        var admin = login!.AccessToken;

        async Task<(string subject, string token)> Aprovador(string prefixo)
        {
            var subject = $"{prefixo}-{Guid.NewGuid():N}"[..12];
            var mail = $"{Guid.NewGuid():N}@{prefixo}.com";
            var (_, u) = await PostAsync<UserResp>(c, "/api/v1/users",
                new { subject, email = mail, displayName = subject, password = Password }, admin);
            var (_, role) = await PostAsync<RoleResp>(c, "/api/v1/roles", new { name = $"R-{subject}" }, admin);
            foreach (var p in new[] { "purchases.read", "purchases.approve" })
                await PostStatusAsync(c, $"/api/v1/roles/{role!.RoleId}/permissions", new { permission = p }, admin);
            await PostStatusAsync(c, $"/api/v1/users/{u!.UserId}/roles", new { roleId = role!.RoleId }, admin);
            var (_, l) = await PostAsync<LoginResp>(c, "/api/v1/auth/login",
                new { companyId = comp.CompanyId, email = mail, password = Password });
            return (subject, l!.AccessToken);
        }
        var ap1 = await Aprovador("r1");
        var ap2 = await Aprovador("r2");

        await PostStatusAsync(c, "/api/v1/purchases/paying-companies",
            new { code = "EP-RC", legalName = "Pagadora Rec", taxId = "44.444.444/0001-44" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/cost-centers", new { code = "CC-RC", name = "Centro Rec" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/suppliers",
            new { code = "FORN-RC", name = "Fornecedor Rec", taxId = "55.555.555/0001-55" }, admin);

        // O item precisa existir no catálogo do Almox para receber o crédito de estoque.
        await PostStatusAsync(c, "/api/v1/materials/units",
            new { code = "un", name = "un", dimension = "contagem", factorToBase = 1 }, admin);
        await PostStatusAsync(c, "/api/v1/materials/items",
            new { code = itemCode, name = $"Item {itemCode}", baseUnitCode = "un" }, admin);

        var (_, req) = await PostAsync<ReqResp>(c, "/api/v1/purchases/requisitions", new
        {
            payingCompanyCode = "EP-RC", costCenterCode = "CC-RC", priority = "Normal",
            justification = "compra para recebimento",
            approverLevel1Subject = ap1.subject, approverLevel2Subject = ap2.subject,
            lines = new[] { new { itemCode, quantity = quantidade, unit = "un" } },
        }, admin);
        await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{req!.RequisitionId}/submit", null, admin);
        await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{req.RequisitionId}/approve", null, ap1.token);
        await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{req.RequisitionId}/approve", null, ap2.token);

        var (ocStatus, oc) = await PostAsync<OrderResp>(c, $"/api/v1/purchases/requisitions/{req.RequisitionId}/order",
            new
            {
                payingCompanyCode = "EP-RC", supplierCode = "FORN-RC",
                lines = new[] { new { itemCode, unitPrice = 10.00m } },
            }, admin);
        Assert.Equal(201, ocStatus);

        var (_, resumo) = await GetAsync<SummaryRow>(c, $"/api/v1/purchases/orders/{oc!.OrderId}/receipts", admin);
        return (c, admin, oc.OrderId, resumo!.Lines.Single().OrderLineId);
    }

    [Fact]
    public async Task Recebimento_com_avaria_credita_so_o_liquido_no_estoque()
    {
        var (c, admin, orderId, lineId) = await OcEmitidaAsync("TELHA-6MM", 50m);

        var (status, rec) = await PostAsync<ReceiptResp>(c, $"/api/v1/purchases/orders/{orderId}/receipts", new
        {
            invoiceNumber = "NF-90210", invoiceDate = "2026-09-19", notes = "entrega no portão 2",
            lines = new[]
            {
                new
                {
                    orderLineId = lineId, quantityReceived = 50m, quantityDamaged = 2m,
                    occurrence = "Avaria", occurrenceNote = "2 telhas trincadas no transporte",
                },
            },
        }, admin);

        Assert.Equal(201, status);
        Assert.True(rec!.StockPosted);
        Assert.True(rec.HasOccurrence);
        Assert.True(rec.OrderComplete);
        Assert.Contains("TELHA-6MM", rec.CreditedItems);
        Assert.Empty(rec.ItemsNotInCatalog);
        Assert.Null(rec.StockError);

        // O estoque subiu EXATAMENTE o líquido: 50 recebidas − 2 avariadas = 48.
        var (_, saldo) = await GetAsync<BalanceResp>(c, "/api/v1/materials/items/TELHA-6MM/balance", admin);
        Assert.Equal(48m, saldo!.Quantity);

        // A avaria fica no histórico da entrega, com a descrição da não-conformidade.
        var (_, resumo) = await GetAsync<SummaryRow>(c, $"/api/v1/purchases/orders/{orderId}/receipts", admin);
        Assert.Equal("Received", resumo!.Status);
        var entrega = Assert.Single(resumo.Receipts);
        Assert.Equal("NF-90210", entrega.InvoiceNumber);
        var linha = Assert.Single(entrega.Lines);
        Assert.Equal(50m, linha.QuantityReceived);
        Assert.Equal(2m, linha.QuantityDamaged);
        Assert.Equal(48m, linha.NetQuantity);
        Assert.Equal("Avaria", linha.Occurrence);
        Assert.Equal("2 telhas trincadas no transporte", linha.OccurrenceNote);

        // OC que já recebeu mercadoria não é cancelável (resolve-se por devolução).
        Assert.Equal(400, await PostStatusAsync(c, $"/api/v1/purchases/orders/{orderId}/cancel",
            new { reason = "desistência" }, admin));
    }

    [Fact]
    public async Task Entregas_parciais_acumulam_ate_fechar_a_OC()
    {
        var (c, admin, orderId, lineId) = await OcEmitidaAsync("CIMENTO-50", 10m);

        // 1ª entrega: 4 de 10.
        var (s1, r1) = await PostAsync<ReceiptResp>(c, $"/api/v1/purchases/orders/{orderId}/receipts", new
        {
            invoiceNumber = "NF-1", lines = new[] { new { orderLineId = lineId, quantityReceived = 4m, quantityDamaged = 0m } },
        }, admin);
        Assert.Equal(201, s1);
        Assert.False(r1!.OrderComplete);

        var (_, parcial) = await GetAsync<SummaryRow>(c, $"/api/v1/purchases/orders/{orderId}/receipts", admin);
        Assert.Equal("PartiallyReceived", parcial!.Status);
        Assert.Equal(6m, parcial.Lines.Single().QuantityPending);

        var (_, saldo1) = await GetAsync<BalanceResp>(c, "/api/v1/materials/items/CIMENTO-50/balance", admin);
        Assert.Equal(4m, saldo1!.Quantity);

        // Tentar receber mais do que o pendente sem declarar excesso é recusado.
        Assert.Equal(400, await PostStatusAsync(c, $"/api/v1/purchases/orders/{orderId}/receipts", new
        {
            invoiceNumber = "NF-2", lines = new[] { new { orderLineId = lineId, quantityReceived = 7m, quantityDamaged = 0m } },
        }, admin));

        // 2ª entrega: as 6 restantes → OC fechada e saldo total 10.
        var (s2, r2) = await PostAsync<ReceiptResp>(c, $"/api/v1/purchases/orders/{orderId}/receipts", new
        {
            invoiceNumber = "NF-2", lines = new[] { new { orderLineId = lineId, quantityReceived = 6m, quantityDamaged = 0m } },
        }, admin);
        Assert.Equal(201, s2);
        Assert.True(r2!.OrderComplete);

        var (_, fim) = await GetAsync<SummaryRow>(c, $"/api/v1/purchases/orders/{orderId}/receipts", admin);
        Assert.Equal("Received", fim!.Status);
        Assert.Equal(0m, fim.Lines.Single().QuantityPending);
        Assert.Equal(2, fim.Receipts.Length);

        var (_, saldo2) = await GetAsync<BalanceResp>(c, "/api/v1/materials/items/CIMENTO-50/balance", admin);
        Assert.Equal(10m, saldo2!.Quantity);
    }
}
