using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// Ponte v3: solicitação do almoxarifado em "Solicitado Compra" gera o pedido no módulo Pedido —
/// linhas e centro vêm da solicitação; o pedido já entra Submetido no fluxo de 2 níveis; o vínculo
/// é único (idempotência) e fica visível na solicitação.
/// </summary>
[Collection("pilot")]
public sealed class BridgeIntegrationTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken);
    private record UserResp(Guid UserId);
    private record RoleResp(Guid RoleId);
    private record RequestResp(Guid RequestId);
    private record GenResp(Guid RequisitionId);
    private record ReqRow(Guid Id, string Status, string CostCenterCode,
        IReadOnlyList<LineRow> Lines);
    private record LineRow(string ItemCode, decimal Quantity, string Unit);
    private record StockReqRow(Guid Id, string Status, Guid? LinkedRequisitionId);

    private const string Password = "senha12345";

    [Fact]
    public async Task Solicitado_compra_gera_pedido_submetido_e_vincula_uma_unica_vez()
    {
        var c = fixture.Client();
        var email = $"{Guid.NewGuid():N}@br.com";
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = "Bridge Co", taxId = $"2{Guid.NewGuid():N}"[..14], adminSubject = $"adm-{Guid.NewGuid():N}",
            adminEmail = email, adminName = "Adm", adminPassword = Password,
        });
        var (_, login) = await PostAsync<LoginResp>(c, "/api/v1/auth/login",
            new { companyId = comp!.CompanyId, email, password = Password });
        var admin = login!.AccessToken;

        async Task<string> UserWithPerms(string subject, string mail, params string[] perms)
        {
            var (_, u) = await PostAsync<UserResp>(c, "/api/v1/users", new { subject, email = mail, displayName = subject, password = Password }, admin);
            var (_, role) = await PostAsync<RoleResp>(c, "/api/v1/roles", new { name = $"R-{subject}" }, admin);
            foreach (var p in perms)
                await PostStatusAsync(c, $"/api/v1/roles/{role!.RoleId}/permissions", new { permission = p }, admin);
            await PostStatusAsync(c, $"/api/v1/users/{u!.UserId}/roles", new { roleId = role!.RoleId }, admin);
            var (_, l) = await PostAsync<LoginResp>(c, "/api/v1/auth/login", new { companyId = comp.CompanyId, email = mail, password = Password });
            return l!.AccessToken;
        }
        var gestor = await UserWithPerms($"g-{Guid.NewGuid():N}"[..10], $"{Guid.NewGuid():N}@g.com", "warehouse.approve");
        _ = await UserWithPerms($"b1-{Guid.NewGuid():N}"[..12], $"{Guid.NewGuid():N}@b1.com", "purchases.read", "purchases.approve");
        _ = await UserWithPerms($"b2-{Guid.NewGuid():N}"[..12], $"{Guid.NewGuid():N}@b2.com", "purchases.read", "purchases.approve");

        // Cadastros do Procurement que o pedido gerado precisa: pagadora + centro (com vínculo).
        await PostStatusAsync(c, "/api/v1/purchases/paying-companies", new { code = "EP-BR", legalName = "Pag Bridge", taxId = "22.222.222/0001-22" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/cost-centers", new { code = "CC-BR", name = "Centro Bridge", payingCompanyCode = "EP-BR" }, admin);

        // Item SEM saldo → separação roteia para Solicitado Compra.
        await PostStatusAsync(c, "/api/v1/materials/units", new { code = "un", name = "un", dimension = "contagem", factorToBase = 1 }, admin);
        await PostStatusAsync(c, "/api/v1/materials/items", new { code = "BR-1", name = "Capacete", baseUnitCode = "un", group = "EPI" }, admin);

        var gestorSubject = (await GetAsync<List<Dictionary<string, string>>>(c, "/api/v1/materials/managers", admin))
            .Item2!.Select(a => a["subject"]).First(s => s.StartsWith("g-", StringComparison.Ordinal));
        var (_, req) = await PostAsync<RequestResp>(c, "/api/v1/materials/requests", new
        {
            companyCode = "EP-BR", costCenterCode = "CC-BR", managerSubject = gestorSubject, reason = "Roteiro mensal",
            lines = new[] { new { itemCode = "BR-1", quantity = 4 } },
        }, admin);
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/materials/requests/{req!.RequestId}/approve", null, gestor));
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/materials/requests/{req.RequestId}/separation", null, admin));

        // Descobre os subjects dos aprovadores de compra (b1-/b2-).
        var (_, approvers) = await GetAsync<List<Dictionary<string, string>>>(c, "/api/v1/purchases/approvers", admin);
        var subjects = approvers!.Select(a => a["subject"]).ToList();
        var ap1 = subjects.First(s => s.StartsWith("b1-", StringComparison.Ordinal));
        var ap2 = subjects.First(s => s.StartsWith("b2-", StringComparison.Ordinal));

        // Gera o pedido a partir da solicitação.
        var (genStatus, gen) = await PostAsync<GenResp>(c, $"/api/v1/materials/requests/{req.RequestId}/generate-purchase",
            new { payingCompanyCode = "EP-BR", approverLevel1Subject = ap1, approverLevel2Subject = ap2 }, admin);
        Assert.Equal(200, genStatus);

        // O pedido existe, já Submetido, com o centro e as linhas da solicitação.
        var (reqStatus, pedido) = await GetAsync<ReqRow>(c, $"/api/v1/purchases/requisitions/{gen!.RequisitionId}", admin);
        Assert.Equal(200, reqStatus);
        Assert.Equal("Submitted", pedido!.Status);
        Assert.Equal("CC-BR", pedido.CostCenterCode);
        var line = Assert.Single(pedido.Lines);
        Assert.Equal("BR-1", line.ItemCode);
        Assert.Equal(4m, line.Quantity);

        // O vínculo aparece na solicitação e a segunda geração é recusada (idempotência).
        var (_, stockReqs) = await GetAsync<StockReqRow[]>(c, "/api/v1/materials/requests", admin);
        var mine = Assert.Single(stockReqs!.Where(r => r.Id == req.RequestId));
        Assert.Equal(gen.RequisitionId, mine.LinkedRequisitionId);

        Assert.Equal(400, await PostStatusAsync(c, $"/api/v1/materials/requests/{req.RequestId}/generate-purchase",
            new { payingCompanyCode = "EP-BR", approverLevel1Subject = ap1, approverLevel2Subject = ap2 }, admin));
    }
}
