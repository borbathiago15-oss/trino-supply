using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// Torre de Controlo (Fase 04) pela stack HTTP real: item com necessidade vencida e nada recebido
/// fica vermelho; item entregue por inteiro encerra sem farol; os cards e o chip "só atrasados"
/// refletem isso.
/// </summary>
[Collection("pilot")]
public sealed class ControlTowerIntegrationTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken);
    private record UserResp(Guid UserId);
    private record RoleResp(Guid RoleId);
    private record ReqResp(Guid RequisitionId);
    private record OrderResp(Guid OrderId);
    private record PendingRow(Guid OrderLineId, string ItemCode);
    private record SummaryRow(Guid OrderId, PendingRow[] Lines);
    private record Row(
        Guid RequisitionId, Guid LineId, DateTimeOffset CreatedAt, string Requester, string CostCenterCode,
        string CostCenterName, string ItemCode, decimal Quantity, string Unit, string Priority, DateOnly? NeededBy,
        string Stage, bool IsOpen, string Light, Guid? OrderId, long? OrderNumber, string? SupplierCode,
        string? SupplierName, string? Buyer, DateOnly? PromisedDate, decimal QuantityReceived,
        decimal PendingQuantity, decimal? OrderedValue, string? InvoiceNumbers, string? InvoiceMatch);
    private record Summary(int OpenItems, int LateItems, int UrgentItems, int WithoutOrder, double? AvgLeadTimeDays, decimal BacklogValue);
    private record Tower(Summary Summary, Row[] Rows);

    private const string Password = "senha12345";

    [Fact]
    public async Task Item_vencido_fica_vermelho_e_o_entregue_encerra()
    {
        var c = fixture.Client();
        var email = $"{Guid.NewGuid():N}@tw.com";
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = "Torre Co", taxId = $"6{Guid.NewGuid():N}"[..14], adminSubject = $"adm-{Guid.NewGuid():N}",
            adminEmail = email, adminName = "Adm", adminPassword = Password,
        });
        var (_, login) = await PostAsync<LoginResp>(c, "/api/v1/auth/login",
            new { companyId = comp!.CompanyId, email, password = Password });
        var admin = login!.AccessToken;

        async Task<(string subject, string token)> Aprovador(string p)
        {
            var subject = $"{p}-{Guid.NewGuid():N}"[..12];
            var mail = $"{Guid.NewGuid():N}@{p}.com";
            var (_, u) = await PostAsync<UserResp>(c, "/api/v1/users",
                new { subject, email = mail, displayName = subject, password = Password }, admin);
            var (_, role) = await PostAsync<RoleResp>(c, "/api/v1/roles", new { name = $"R-{subject}" }, admin);
            foreach (var perm in new[] { "purchases.read", "purchases.approve" })
                await PostStatusAsync(c, $"/api/v1/roles/{role!.RoleId}/permissions", new { permission = perm }, admin);
            await PostStatusAsync(c, $"/api/v1/users/{u!.UserId}/roles", new { roleId = role!.RoleId }, admin);
            var (_, l) = await PostAsync<LoginResp>(c, "/api/v1/auth/login",
                new { companyId = comp.CompanyId, email = mail, password = Password });
            return (subject, l!.AccessToken);
        }
        var ap1 = await Aprovador("t1");
        var ap2 = await Aprovador("t2");

        await PostStatusAsync(c, "/api/v1/purchases/paying-companies",
            new { code = "EP-TW", legalName = "Pagadora Torre", taxId = "77.777.888/0001-77" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/cost-centers", new { code = "CC-TW", name = "Obra Norte" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/suppliers",
            new { code = "FORN-TW", name = "Fornecedor Torre", taxId = "88.888.999/0001-88" }, admin);
        await PostStatusAsync(c, "/api/v1/materials/units",
            new { code = "un", name = "un", dimension = "contagem", factorToBase = 1 }, admin);
        foreach (var item in new[] { "ATRASADO-TW", "ENTREGUE-TW" })
            await PostStatusAsync(c, "/api/v1/materials/items",
                new { code = item, name = $"Item {item}", baseUnitCode = "un" }, admin);

        async Task<Guid> Aprovada(string item, string prioridade, DateOnly necessidade)
        {
            var (_, req) = await PostAsync<ReqResp>(c, "/api/v1/purchases/requisitions", new
            {
                payingCompanyCode = "EP-TW", costCenterCode = "CC-TW", priority = prioridade,
                justification = "torre", neededBy = necessidade,
                approverLevel1Subject = ap1.subject, approverLevel2Subject = ap2.subject,
                lines = new[] { new { itemCode = item, quantity = 5m, unit = "un" } },
            }, admin);
            var id = req!.RequisitionId;
            await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{id}/submit", null, admin);
            await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{id}/approve", null, ap1.token);
            await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{id}/approve", null, ap2.token);
            return id;
        }

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        // ---- Emergencial, necessidade ONTEM, aprovada e parada: vermelho, sem OC -----------------
        var reqAtrasada = await Aprovada("ATRASADO-TW", "Emergencial", hoje.AddDays(-1));

        // ---- Normal, com OC emitida e tudo recebido: encerrado, sem farol -----------------------
        var reqEntregue = await Aprovada("ENTREGUE-TW", "Normal", hoje.AddDays(30));
        var (_, oc) = await PostAsync<OrderResp>(c, $"/api/v1/purchases/requisitions/{reqEntregue}/order", new
        {
            payingCompanyCode = "EP-TW", supplierCode = "FORN-TW",
            lines = new[] { new { itemCode = "ENTREGUE-TW", unitPrice = 20m } },
        }, admin);
        var (_, resumo) = await GetAsync<SummaryRow>(c, $"/api/v1/purchases/orders/{oc!.OrderId}/receipts", admin);
        Assert.Equal(201, await PostStatusAsync(c, $"/api/v1/purchases/orders/{oc.OrderId}/receipts", new
        {
            invoiceNumber = "NF-TW",
            lines = new[] { new { orderLineId = resumo!.Lines.Single().OrderLineId, quantityReceived = 5m, quantityDamaged = 0m } },
        }, admin));

        // ---- A Torre ----------------------------------------------------------------------------
        var (status, torre) = await GetAsync<Tower>(c, "/api/v1/purchases/control-tower", admin);
        Assert.Equal(200, status);

        var atrasado = torre!.Rows.Single(r => r.RequisitionId == reqAtrasada);
        Assert.Equal("Approved", atrasado.Stage);
        Assert.True(atrasado.IsOpen);
        Assert.Equal("Red", atrasado.Light);
        Assert.Null(atrasado.OrderId);
        Assert.Equal(5m, atrasado.PendingQuantity);
        Assert.Equal("Obra Norte", atrasado.CostCenterName);   // nome legível, não só o código

        var entregue = torre.Rows.Single(r => r.RequisitionId == reqEntregue);
        Assert.Equal("Received", entregue.Stage);
        Assert.False(entregue.IsOpen);
        Assert.Equal("None", entregue.Light);
        Assert.Equal(oc.OrderId, entregue.OrderId);
        Assert.Equal(0m, entregue.PendingQuantity);
        Assert.Equal(100m, entregue.OrderedValue);              // 5 × R$ 20

        // Cards: 1 em aberto, 1 atrasado, 1 urgente, 1 sem OC; lead time medido no entregue.
        Assert.Equal(1, torre.Summary.OpenItems);
        Assert.Equal(1, torre.Summary.LateItems);
        Assert.Equal(1, torre.Summary.UrgentItems);
        Assert.Equal(1, torre.Summary.WithoutOrder);
        Assert.NotNull(torre.Summary.AvgLeadTimeDays);
        Assert.Equal(0m, torre.Summary.BacklogValue);           // a única OC já chegou inteira

        // O vermelho vem antes do encerrado na fila de trabalho.
        Assert.True(Array.IndexOf(torre.Rows, atrasado) < Array.IndexOf(torre.Rows, entregue));

        // Chips: só atrasados / só sem OC / busca textual.
        var (_, soAtrasados) = await GetAsync<Tower>(c, "/api/v1/purchases/control-tower?late=true", admin);
        Assert.Single(soAtrasados!.Rows);
        Assert.Equal("ATRASADO-TW", soAtrasados.Rows[0].ItemCode);

        var (_, busca) = await GetAsync<Tower>(c, "/api/v1/purchases/control-tower?q=entregue", admin);
        Assert.Single(busca!.Rows);
        Assert.Equal("ENTREGUE-TW", busca.Rows[0].ItemCode);
    }
}
