using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// v3 — COMPRA DIVIDIDA (multi-fornecedor) pela stack HTTP real. Cenário de aceite: uma requisição
/// com 2 itens de EPI e 2 de limpeza rende DUAS OCs — EPI com o fornecedor A, limpeza com o B.
/// Depois da primeira emissão a requisição fica "atendida parcialmente"; depois da segunda, fechada.
/// Item já pedido não entra em outra OC, e cancelar a OC devolve os itens à fila de compra.
/// </summary>
[Collection("pilot")]
public sealed class SplitOrderIntegrationTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken);
    private record UserResp(Guid UserId);
    private record RoleResp(Guid RoleId);
    private record ReqResp(Guid RequisitionId);
    private record OrderResp(Guid OrderId);
    private record LineView(string ItemCode, decimal Quantity, string Unit, Guid? PurchaseOrderId);
    private record ReqView(Guid Id, string Status, IReadOnlyList<LineView> Lines);
    private record OrderLine(string ItemCode, decimal Quantity, decimal UnitPrice);
    private record OrderView(long Number, string SupplierCode, decimal ProductsValue, OrderLine[] Lines);

    private const string Password = "senha12345";

    [Fact]
    public async Task Requisicao_multi_familia_rende_uma_OC_por_fornecedor()
    {
        var c = fixture.Client();
        var email = $"{Guid.NewGuid():N}@split.com";
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = "Split Co", taxId = $"9{Guid.NewGuid():N}"[..14], adminSubject = $"adm-{Guid.NewGuid():N}",
            adminEmail = email, adminName = "Adm", adminPassword = Password,
        });
        var (_, login) = await PostAsync<LoginResp>(c, "/api/v1/auth/login",
            new { companyId = comp!.CompanyId, email, password = Password });
        var admin = login!.AccessToken;

        // Aprovadores dedicados (SoD: o requisitante não aprova).
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
        var ap1 = await Aprovador("s1");
        var ap2 = await Aprovador("s2");

        // Cadastros: empresa pagadora, centro e DOIS fornecedores (um por família).
        await PostStatusAsync(c, "/api/v1/purchases/paying-companies",
            new { code = "EP-SP", legalName = "Pagadora Split", taxId = "55.555.555/0001-55" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/cost-centers", new { code = "CC-SP", name = "Centro Split" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/suppliers",
            new { code = "FORN-A", name = "Alfa EPIs", taxId = "66.666.666/0001-66" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/suppliers",
            new { code = "FORN-B", name = "Beta Limpeza", taxId = "77.777.777/0001-77" }, admin);

        // Requisição multi-família: 2 EPIs + 2 itens de limpeza.
        var (createStatus, req) = await PostAsync<ReqResp>(c, "/api/v1/purchases/requisitions", new
        {
            payingCompanyCode = "EP-SP", costCenterCode = "CC-SP", priority = "Normal",
            justification = "compra dividida entre fornecedores",
            approverLevel1Subject = ap1.subject, approverLevel2Subject = ap2.subject,
            lines = new[]
            {
                new { itemCode = "BOTINA", quantity = 2, unit = "par" },
                new { itemCode = "LUVA", quantity = 5, unit = "par" },
                new { itemCode = "DETERGENTE", quantity = 10, unit = "un" },
                new { itemCode = "ALVEJANTE", quantity = 4, unit = "un" },
            },
        }, admin);
        Assert.Equal(201, createStatus);
        var reqId = req!.RequisitionId;

        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{reqId}/submit", null, admin));
        Assert.Equal(200, await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{reqId}/approve", null, ap1.token));
        Assert.Equal(200, await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{reqId}/approve", null, ap2.token));

        // ---- OC 1: só os EPIs, com o fornecedor A ----------------------------------------------
        var (oc1Status, oc1) = await PostAsync<OrderResp>(c, $"/api/v1/purchases/requisitions/{reqId}/order", new
        {
            payingCompanyCode = "EP-SP", supplierCode = "FORN-A",
            lines = new[]
            {
                new { itemCode = "BOTINA", unitPrice = 150.00m },
                new { itemCode = "LUVA", unitPrice = 12.50m },
            },
        }, admin);
        Assert.Equal(201, oc1Status);

        var (_, apos1) = await GetAsync<ReqView>(c, $"/api/v1/purchases/requisitions/{reqId}", admin);
        Assert.Equal("PartiallyOrdered", apos1!.Status);
        Assert.Equal(oc1!.OrderId, apos1.Lines.Single(l => l.ItemCode == "BOTINA").PurchaseOrderId);
        Assert.Equal(oc1.OrderId, apos1.Lines.Single(l => l.ItemCode == "LUVA").PurchaseOrderId);
        Assert.Null(apos1.Lines.Single(l => l.ItemCode == "DETERGENTE").PurchaseOrderId);

        // A OC 1 contém SÓ os dois EPIs — 2×150 + 5×12,50 = 362,50.
        var (_, ocA) = await GetAsync<OrderView>(c, $"/api/v1/purchases/orders/{oc1.OrderId}", admin);
        Assert.Equal("FORN-A", ocA!.SupplierCode);
        Assert.Equal(2, ocA.Lines.Length);
        Assert.Equal(362.50m, ocA.ProductsValue);

        // Com a requisição PARCIAL, repedir um item já coberto é recusado pela trava de LINHA (409).
        Assert.Equal(409, await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{reqId}/order", new
        {
            payingCompanyCode = "EP-SP", supplierCode = "FORN-B",
            lines = new[] { new { itemCode = "BOTINA", unitPrice = 99.00m } },
        }, admin));

        // ---- OC 2: a limpeza, com o fornecedor B -----------------------------------------------
        var (oc2Status, oc2) = await PostAsync<OrderResp>(c, $"/api/v1/purchases/requisitions/{reqId}/order", new
        {
            payingCompanyCode = "EP-SP", supplierCode = "FORN-B",
            lines = new[]
            {
                new { itemCode = "DETERGENTE", unitPrice = 8.00m },
                new { itemCode = "ALVEJANTE", unitPrice = 6.00m },
            },
        }, admin);
        Assert.Equal(201, oc2Status);

        var (_, apos2) = await GetAsync<ReqView>(c, $"/api/v1/purchases/requisitions/{reqId}", admin);
        Assert.Equal("Ordered", apos2!.Status);
        Assert.All(apos2.Lines, l => Assert.NotNull(l.PurchaseOrderId));
        Assert.Equal(oc2!.OrderId, apos2.Lines.Single(l => l.ItemCode == "ALVEJANTE").PurchaseOrderId);

        var (_, ocB) = await GetAsync<OrderView>(c, $"/api/v1/purchases/orders/{oc2.OrderId}", admin);
        Assert.Equal("FORN-B", ocB!.SupplierCode);
        Assert.Equal(2, ocB.Lines.Length);
        Assert.Equal(104.00m, ocB.ProductsValue);   // 10×8 + 4×6
        Assert.NotEqual(ocA.Number, ocB.Number);    // numeração sequencial distinta

        // ---- Requisição inteiramente pedida recusa nova OC (409) -------------------------------
        Assert.Equal(409, await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{reqId}/order", new
        {
            payingCompanyCode = "EP-SP", supplierCode = "FORN-B",
            lines = new[] { new { itemCode = "BOTINA", unitPrice = 99.00m } },
        }, admin));

        // ---- Cancelar a OC 2 devolve a limpeza para a fila de compra ---------------------------
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/purchases/orders/{oc2.OrderId}/cancel",
            new { reason = "fornecedor atrasou" }, admin));

        var (_, aposCancel) = await GetAsync<ReqView>(c, $"/api/v1/purchases/requisitions/{reqId}", admin);
        Assert.Equal("PartiallyOrdered", aposCancel!.Status);
        Assert.Null(aposCancel.Lines.Single(l => l.ItemCode == "DETERGENTE").PurchaseOrderId);
        Assert.Equal(oc1.OrderId, aposCancel.Lines.Single(l => l.ItemCode == "BOTINA").PurchaseOrderId);

        // E o item liberado pode ser recomprado de outro fornecedor.
        Assert.Equal(201, await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{reqId}/order", new
        {
            payingCompanyCode = "EP-SP", supplierCode = "FORN-A",
            lines = new[]
            {
                new { itemCode = "DETERGENTE", unitPrice = 7.50m },
                new { itemCode = "ALVEJANTE", unitPrice = 5.50m },
            },
        }, admin));
        var (_, fim) = await GetAsync<ReqView>(c, $"/api/v1/purchases/requisitions/{reqId}", admin);
        Assert.Equal("Ordered", fim!.Status);
    }
}
