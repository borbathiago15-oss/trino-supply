using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// Fase B3 (v2): roteamento pós-aprovação do Pedido unificado. Aprovado com saldo no Almox para
/// todas as linhas → baixa em lote atômica + status "Atendido pelo estoque" (sem OC). Sem saldo →
/// permanece Aprovado e segue a rota de compra (OC), como antes.
/// </summary>
[Collection("pilot")]
public sealed class FulfillmentIntegrationTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken);
    private record UserResp(Guid UserId);
    private record RoleResp(Guid RoleId);
    private record ReqResp(Guid RequisitionId);
    private record RouteResp(string Route);
    private record ReqRow(Guid Id, string Status);
    private record BalanceResp(decimal Quantity);

    private const string Password = "senha12345";

    private async Task<(Guid company, string admin, string ap1, string ap2)> SetupAsync(HttpClient c)
    {
        var email = $"{Guid.NewGuid():N}@ff.com";
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = "Fulfill Co", taxId = $"3{Guid.NewGuid():N}"[..14], adminSubject = $"adm-{Guid.NewGuid():N}",
            adminEmail = email, adminName = "Adm", adminPassword = Password,
        });
        var (_, login) = await PostAsync<LoginResp>(c, "/api/v1/auth/login", new { companyId = comp!.CompanyId, email, password = Password });
        var admin = login!.AccessToken;

        async Task<string> Approver(string subject, string mail)
        {
            var (_, u) = await PostAsync<UserResp>(c, "/api/v1/users", new { subject, email = mail, displayName = subject, password = Password }, admin);
            var (_, role) = await PostAsync<RoleResp>(c, "/api/v1/roles", new { name = $"R-{subject}" }, admin);
            foreach (var p in new[] { "purchases.read", "purchases.approve" })
                await PostStatusAsync(c, $"/api/v1/roles/{role!.RoleId}/permissions", new { permission = p }, admin);
            await PostStatusAsync(c, $"/api/v1/users/{u!.UserId}/roles", new { roleId = role!.RoleId }, admin);
            var (_, l) = await PostAsync<LoginResp>(c, "/api/v1/auth/login", new { companyId = comp.CompanyId, email = mail, password = Password });
            return l!.AccessToken;
        }
        var ap1 = await Approver($"f1-{Guid.NewGuid():N}"[..12], $"{Guid.NewGuid():N}@f1.com");
        var ap2 = await Approver($"f2-{Guid.NewGuid():N}"[..12], $"{Guid.NewGuid():N}@f2.com");

        await PostStatusAsync(c, "/api/v1/purchases/paying-companies", new { code = "EPF", legalName = "Pagadora F", taxId = "77.777.777/0001-77" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/cost-centers", new { code = "CCF", name = "Centro F" }, admin);
        await PostStatusAsync(c, "/api/v1/materials/units", new { code = "un", name = "un", dimension = "contagem", factorToBase = 1 }, admin);
        return (comp.CompanyId, admin, ap1, ap2);
    }

    private static async Task<(Guid id, string subject1, string subject2)> NewSubmittedAsync(
        HttpClient c, string admin, string itemCode, int qty)
    {
        // Descobre os subjects dos aprovadores via /purchases/approvers; ignora o admin (SoD: o
        // requisitante não pode ser aprovador) — os aprovadores do teste têm prefixo f1-/f2-.
        var (_, approvers) = await GetAsync<List<Dictionary<string, string>>>(c, "/api/v1/purchases/approvers", admin);
        var all = approvers!.Select(a => a["subject"]).ToList();
        var subjects = new List<string>
        {
            all.First(s => s.StartsWith("f1-", StringComparison.Ordinal)),
            all.First(s => s.StartsWith("f2-", StringComparison.Ordinal)),
        };
        var (status, r) = await PostAsync<ReqResp>(c, "/api/v1/purchases/requisitions", new
        {
            payingCompanyCode = "EPF", costCenterCode = "CCF", priority = "Normal", justification = "roteamento",
            approverLevel1Subject = subjects[0], approverLevel2Subject = subjects[1],
            lines = new[] { new { itemCode, quantity = qty, unit = "un" } },
        }, admin);
        Assert.Equal(201, status);
        await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{r!.RequisitionId}/submit", null, admin);
        return (r.RequisitionId, subjects[0], subjects[1]);
    }

    [Fact]
    public async Task Aprovado_com_estoque_da_baixa_e_encerra_sem_OC()
    {
        var c = fixture.Client();
        var (_, admin, ap1, ap2) = await SetupAsync(c);

        // Item COM saldo (10) no Almox; pedido de 4.
        await PostStatusAsync(c, "/api/v1/materials/items", new { code = "EST-1", name = "Item Estoque", baseUnitCode = "un", group = "EPI" }, admin);
        await PostStatusAsync(c, "/api/v1/materials/items/EST-1/movements", new { direction = 1, quantity = 10 }, admin);

        var (id, _, _) = await NewSubmittedAsync(c, admin, "EST-1", 4);
        var (s1, r1) = await PostAsync<RouteResp>(c, $"/api/v1/purchases/requisitions/{id}/approve", null!, ap1);
        Assert.Equal(200, s1);
        Assert.Equal("purchase", r1!.Route); // nível 1 ainda não conclui — sem baixa
        var (s2, r2) = await PostAsync<RouteResp>(c, $"/api/v1/purchases/requisitions/{id}/approve", null!, ap2);
        Assert.Equal(200, s2);
        Assert.Equal("stock", r2!.Route); // nível 2: com saldo → atendido pelo estoque

        var (_, req) = await GetAsync<ReqRow>(c, $"/api/v1/purchases/requisitions/{id}", admin);
        Assert.Equal("FulfilledFromStock", req!.Status);

        var (_, bal) = await GetAsync<BalanceResp>(c, "/api/v1/materials/items/EST-1/balance", admin);
        Assert.Equal(6m, bal!.Quantity); // 10 - 4

        // Encerrado sem OC: emitir contra ele falha (não está mais Aprovado).
        await PostStatusAsync(c, "/api/v1/purchases/suppliers", new { code = "FRN", name = "Forn", taxId = "88.888.888/0001-88" }, admin);
        var issue = await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{id}/order",
            new { payingCompanyCode = "EPF", supplierCode = "FRN", lines = new[] { new { itemCode = "EST-1", unitPrice = 1m } } }, admin);
        Assert.NotEqual(201, issue);
    }

    [Fact]
    public async Task Aprovado_sem_estoque_segue_para_compra()
    {
        var c = fixture.Client();
        var (_, admin, ap1, ap2) = await SetupAsync(c);

        // Item SEM saldo suficiente (2) para o pedido (5).
        await PostStatusAsync(c, "/api/v1/materials/items", new { code = "EST-2", name = "Item Sem Saldo", baseUnitCode = "un", group = "EPI" }, admin);
        await PostStatusAsync(c, "/api/v1/materials/items/EST-2/movements", new { direction = 1, quantity = 2 }, admin);

        var (id, _, _) = await NewSubmittedAsync(c, admin, "EST-2", 5);
        await PostAsync<RouteResp>(c, $"/api/v1/purchases/requisitions/{id}/approve", null!, ap1);
        var (_, r2) = await PostAsync<RouteResp>(c, $"/api/v1/purchases/requisitions/{id}/approve", null!, ap2);
        Assert.Equal("purchase", r2!.Route); // sem saldo → rota de compra

        var (_, req) = await GetAsync<ReqRow>(c, $"/api/v1/purchases/requisitions/{id}", admin);
        Assert.Equal("Approved", req!.Status); // pronto para OC

        var (_, bal) = await GetAsync<BalanceResp>(c, "/api/v1/materials/items/EST-2/balance", admin);
        Assert.Equal(2m, bal!.Quantity); // saldo intacto — nenhuma baixa parcial
    }
}
