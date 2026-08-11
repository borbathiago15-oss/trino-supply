using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

[Collection("pilot")]
public sealed class SecurityIntegrationTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken);
    private record IdResp(Guid UserId, Guid RoleId, Guid RequisitionId);
    private record UserRow(Guid Id, string Email);
    private record BalanceResp(decimal Quantity);

    private static string Password => "senha12345";

    private async Task<Guid> ProvisionAsync(HttpClient c, string legal, string taxId, string subject, string email)
    {
        var (status, body) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = legal, taxId, adminSubject = subject, adminEmail = email, adminName = subject, adminPassword = Password,
        });
        Assert.Equal(201, status);
        return body!.CompanyId;
    }

    private async Task<string> LoginAsync(HttpClient c, Guid companyId, string email)
    {
        var (status, body) = await PostAsync<LoginResp>(c, "/api/v1/auth/login", new { companyId, email, password = Password });
        Assert.Equal(200, status);
        return body!.AccessToken;
    }

    [Fact]
    public async Task RLS_isola_usuarios_entre_tenants()
    {
        var c = fixture.Client();
        var a = await ProvisionAsync(c, "Empresa A", "10.000.000/0001-01", "adminA", "admin@a-rls.com");
        var b = await ProvisionAsync(c, "Empresa B", "20.000.000/0001-02", "adminB", "admin@b-rls.com");
        var tokenA = await LoginAsync(c, a, "admin@a-rls.com");
        var tokenB = await LoginAsync(c, b, "admin@b-rls.com");

        var (sa, listA) = await GetAsync<UserRow[]>(c, "/api/v1/users", tokenA);
        var (sb, listB) = await GetAsync<UserRow[]>(c, "/api/v1/users", tokenB);

        Assert.Equal(200, sa);
        Assert.Equal(200, sb);
        // Cada admin só vê o próprio tenant (isolamento por RLS, sem filtro na aplicação).
        Assert.All(listA!, u => Assert.Equal("admin@a-rls.com", u.Email));
        Assert.All(listB!, u => Assert.Equal("admin@b-rls.com", u.Email));
        Assert.DoesNotContain(listA!, u => u.Email == "admin@b-rls.com");
    }

    [Fact]
    public async Task Deny_by_default_usuario_sem_papel_recebe_403()
    {
        var c = fixture.Client();
        var company = await ProvisionAsync(c, "Empresa DBD", "30.000.000/0001-03", "adminDbd", "admin@dbd.com");
        var admin = await LoginAsync(c, company, "admin@dbd.com");

        var create = await PostStatusAsync(c, "/api/v1/users",
            new { subject = "semrole", email = "sem@dbd.com", displayName = "Sem", password = Password }, admin);
        Assert.Equal(201, create);

        var semrole = await LoginAsync(c, company, "sem@dbd.com");
        var (status, _) = await GetAsync<UserRow[]>(c, "/api/v1/users", semrole);
        Assert.Equal(403, status); // sem papéis → negado
    }

    [Fact]
    public async Task SoD_requisitante_nao_aprova_a_propria_requisicao()
    {
        var c = fixture.Client();
        var company = await ProvisionAsync(c, "Empresa SoD", "40.000.000/0001-04", "adminSod", "admin@sod.com");
        var admin = await LoginAsync(c, company, "admin@sod.com");

        Assert.Equal(201, await PostStatusAsync(c, "/api/v1/purchases/paying-companies", new { code = "EPS", legalName = "Pagadora SoD", taxId = "40.000.000/0001-04" }, admin));
        Assert.Equal(201, await PostStatusAsync(c, "/api/v1/purchases/cost-centers", new { code = "CCS", name = "Centro SoD" }, admin));

        // SoD: o requisitante (adminSod) não pode ser designado aprovador → 400 sod_violation na criação.
        var (createStatus, _) = await PostAsync<IdResp>(c, "/api/v1/purchases/requisitions", new
        {
            payingCompanyCode = "EPS", costCenterCode = "CCS", priority = "Normal", justification = "teste SoD",
            approverLevel1Subject = "adminSod", approverLevel2Subject = "outro",
            lines = new[] { new { itemCode = "X", quantity = 1, unit = "un" } },
        }, admin);
        Assert.Equal(400, createStatus);
    }

    [Fact]
    public async Task Estoque_sob_concorrencia_mantem_saldo_exato()
    {
        var c = fixture.Client();
        var company = await ProvisionAsync(c, "Empresa Conc", "50.000.000/0001-05", "adminConc", "admin@conc.com");
        var admin = await LoginAsync(c, company, "admin@conc.com");

        await PostStatusAsync(c, "/api/v1/materials/units",
            new { code = "un", name = "un", dimension = "contagem", factorToBase = 1 }, admin);
        await PostStatusAsync(c, "/api/v1/materials/items",
            new { code = "CONC", name = "Item", baseUnitCode = "un" }, admin);

        const int n = 15;
        var tasks = Enumerable.Range(0, n).Select(_ =>
            PostStatusAsync(c, "/api/v1/materials/items/CONC/movements", new { direction = 1, quantity = 1 }, admin));
        var codes = await Task.WhenAll(tasks);
        Assert.All(codes, code => Assert.Equal(200, code));

        var (status, balance) = await GetAsync<BalanceResp>(c, "/api/v1/materials/items/CONC/balance", admin);
        Assert.Equal(200, status);
        Assert.Equal(n, balance!.Quantity); // sem atualização perdida
    }
}
