using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// Fase B1 (v2): escopo por centro de custo (Master Junior só vê/aprova os seus), vínculo
/// CNPJ interno ↔ centro, Central de Aprovação e papéis-modelo criados no provisionamento.
/// </summary>
[Collection("pilot")]
public sealed class CenterScopeIntegrationTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken);
    private record UserResp(Guid UserId);
    private record RoleResp(Guid RoleId);
    private record ReqResp(Guid RequisitionId);
    private record RoleRow(Guid Id, string Name, string[] Permissions);
    private record CostCenterRow(Guid Id, string Code, string Name, string Status, string? PayingCompanyCode, string? PayingCompanyName);
    private record ReqRow(Guid Id, string Status, string CostCenterCode);
    private record ApprovalsResp(ReqRow[] Requisitions, object[] StockRequests);

    private const string Password = "senha12345";

    private async Task<(Guid company, string admin)> ProvisionAsync(HttpClient c, string email)
    {
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = "Scope Co", taxId = $"9{Guid.NewGuid():N}"[..14], adminSubject = $"adm-{Guid.NewGuid():N}",
            adminEmail = email, adminName = "Adm", adminPassword = Password,
        });
        var (_, login) = await PostAsync<LoginResp>(c, "/api/v1/auth/login", new { companyId = comp!.CompanyId, email, password = Password });
        return (comp.CompanyId, login!.AccessToken);
    }

    private async Task<(Guid userId, string token)> JuniorAsync(HttpClient c, string admin, Guid company, string subject, string email, params string[] centers)
    {
        var (_, user) = await PostAsync<UserResp>(c, "/api/v1/users", new { subject, email, displayName = subject, password = Password }, admin);
        // Usa o papel-modelo "Master Junior" criado no provisionamento.
        var (_, roles) = await GetAsync<RoleRow[]>(c, "/api/v1/roles", admin);
        var junior = roles!.Single(r => r.Name == "Master Junior");
        await PostStatusAsync(c, $"/api/v1/users/{user!.UserId}/roles", new { roleId = junior.Id }, admin);
        using (var put = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/users/{user.UserId}/cost-centers")
        { Content = System.Net.Http.Json.JsonContent.Create(new { codes = centers }) })
        {
            put.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", admin);
            var res = await c.SendAsync(put);
            Assert.Equal(204, (int)res.StatusCode);
        }
        var (_, l) = await PostAsync<LoginResp>(c, "/api/v1/auth/login", new { companyId = company, email, password = Password });
        return (user.UserId, l!.AccessToken);
    }

    [Fact]
    public async Task Papeis_modelo_sao_criados_no_provisionamento()
    {
        var c = fixture.Client();
        var (_, admin) = await ProvisionAsync(c, $"{Guid.NewGuid():N}@rt.com");
        var (_, roles) = await GetAsync<RoleRow[]>(c, "/api/v1/roles", admin);
        var names = roles!.Select(r => r.Name).ToHashSet();
        Assert.Contains("Administrador", names);
        Assert.Contains("Master Junior", names);
        Assert.Contains("Pleno", names);
    }

    [Fact]
    public async Task Centro_de_custo_pode_nascer_vinculado_a_um_CNPJ_interno()
    {
        var c = fixture.Client();
        var (_, admin) = await ProvisionAsync(c, $"{Guid.NewGuid():N}@ln.com");
        await PostStatusAsync(c, "/api/v1/purchases/paying-companies",
            new { code = "EPL", legalName = "Pagadora Link", taxId = "55.555.555/0001-55" }, admin);

        Assert.Equal(201, await PostStatusAsync(c, "/api/v1/purchases/cost-centers",
            new { code = "CCL", name = "Centro Linkado", payingCompanyCode = "EPL" }, admin));
        // CNPJ inexistente é recusado.
        Assert.Equal(400, await PostStatusAsync(c, "/api/v1/purchases/cost-centers",
            new { code = "CCX", name = "Centro Errado", payingCompanyCode = "NAO-EXISTE" }, admin));

        var (_, centers) = await GetAsync<CostCenterRow[]>(c, "/api/v1/purchases/cost-centers", admin);
        var ccl = centers!.Single(x => x.Code == "CCL");
        Assert.Equal("EPL", ccl.PayingCompanyCode);
        Assert.Equal("Pagadora Link", ccl.PayingCompanyName);
    }

    [Fact]
    public async Task Junior_so_ve_e_aprova_pedidos_dos_seus_centros()
    {
        var c = fixture.Client();
        var (company, admin) = await ProvisionAsync(c, $"{Guid.NewGuid():N}@sc.com");

        await PostStatusAsync(c, "/api/v1/purchases/paying-companies",
            new { code = "EPS", legalName = "Pagadora Scope", taxId = "66.666.666/0001-66" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/cost-centers", new { code = "CC-A", name = "Centro A" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/cost-centers", new { code = "CC-B", name = "Centro B" }, admin);

        // Junior responsável APENAS pelo CC-A; é o aprovador nível 1 dos dois pedidos.
        var (_, junior) = await JuniorAsync(c, admin, company, "junior1", "junior1@sc.com", "CC-A");

        async Task<Guid> NewReq(string center)
        {
            var (_, r) = await PostAsync<ReqResp>(c, "/api/v1/purchases/requisitions", new
            {
                payingCompanyCode = "EPS", costCenterCode = center, priority = "Normal", justification = "escopo",
                approverLevel1Subject = "junior1", approverLevel2Subject = "outro2",
                lines = new[] { new { itemCode = "X", quantity = 1, unit = "un" } },
            }, admin);
            await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{r!.RequisitionId}/submit", null, admin);
            return r.RequisitionId;
        }
        var reqA = await NewReq("CC-A");
        var reqB = await NewReq("CC-B");

        // Listagem escopada: o junior vê SÓ o pedido do CC-A.
        var (_, list) = await GetAsync<ReqRow[]>(c, "/api/v1/purchases/requisitions", junior);
        Assert.Contains(list!, r => r.Id == reqA);
        Assert.DoesNotContain(list!, r => r.Id == reqB);

        // Central de Aprovação: só o pedido do centro dele, na etapa dele.
        var (apStatus, approvals) = await GetAsync<ApprovalsResp>(c, "/api/v1/purchases/approvals", junior);
        Assert.Equal(200, apStatus);
        Assert.Contains(approvals!.Requisitions, r => r.Id == reqA);
        Assert.DoesNotContain(approvals.Requisitions, r => r.Id == reqB);

        // Decisão fora do escopo → 403; dentro do escopo → 204.
        Assert.Equal(403, await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{reqB}/approve", null, junior));
        Assert.Equal(200, await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{reqA}/approve", null, junior));
    }
}
