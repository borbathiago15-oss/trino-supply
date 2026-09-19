using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// MMS-004 — saldo do Almox à vista para quem pede e para quem aprova. O endpoint vive em Compras
/// justamente porque o requisitante/aprovador normalmente NÃO tem permissão de estoque; item fora do
/// catálogo não é erro, é compra externa.
/// </summary>
[Collection("pilot")]
public sealed class StockCheckIntegrationTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken);
    private record UserResp(Guid UserId);
    private record RoleResp(Guid RoleId);
    private record StockCheckRow(string ItemCode, bool InCatalog, decimal Balance);

    private const string Password = "senha12345";

    [Fact]
    public async Task Requisitante_ve_saldo_sem_ter_permissao_de_estoque()
    {
        var c = fixture.Client();
        var email = $"{Guid.NewGuid():N}@chk.com";
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = "Check Co", taxId = $"8{Guid.NewGuid():N}"[..14], adminSubject = $"adm-{Guid.NewGuid():N}",
            adminEmail = email, adminName = "Adm", adminPassword = Password,
        });
        var (_, login) = await PostAsync<LoginResp>(c, "/api/v1/auth/login",
            new { companyId = comp!.CompanyId, email, password = Password });
        var admin = login!.AccessToken;

        // Catálogo: um item COM saldo e outro zerado.
        await PostStatusAsync(c, "/api/v1/materials/units",
            new { code = "un", name = "un", dimension = "contagem", factorToBase = 1 }, admin);
        await PostStatusAsync(c, "/api/v1/materials/items", new { code = "CHK-COM", name = "Com saldo", baseUnitCode = "un" }, admin);
        await PostStatusAsync(c, "/api/v1/materials/items", new { code = "CHK-ZERO", name = "Zerado", baseUnitCode = "un" }, admin);
        Assert.Equal(200, await PostStatusAsync(c, "/api/v1/materials/items/CHK-COM/movements",
            new { direction = 1, quantity = 12 }, admin));

        // Usuário que SÓ pode requisitar — sem materials.read.
        var subject = $"req-{Guid.NewGuid():N}"[..12];
        var mail = $"{Guid.NewGuid():N}@req.com";
        var (_, u) = await PostAsync<UserResp>(c, "/api/v1/users",
            new { subject, email = mail, displayName = subject, password = Password }, admin);
        var (_, role) = await PostAsync<RoleResp>(c, "/api/v1/roles", new { name = $"R-{subject}" }, admin);
        await PostStatusAsync(c, $"/api/v1/roles/{role!.RoleId}/permissions", new { permission = "purchases.request" }, admin);
        await PostStatusAsync(c, $"/api/v1/users/{u!.UserId}/roles", new { roleId = role.RoleId }, admin);
        var (_, l) = await PostAsync<LoginResp>(c, "/api/v1/auth/login",
            new { companyId = comp.CompanyId, email = mail, password = Password });
        var requisitante = l!.AccessToken;

        // Ele NÃO enxerga o módulo de estoque…
        Assert.Equal(403, (await GetAsync<object>(c, "/api/v1/materials/items", requisitante)).Item1);

        // …mas vê o saldo dos itens que vai pedir.
        var (status, saldos) = await GetAsync<StockCheckRow[]>(c,
            "/api/v1/purchases/stock-check?items=CHK-COM,CHK-ZERO,NAO-CADASTRADO", requisitante);
        Assert.Equal(200, status);

        var comSaldo = Assert.Single(saldos!, r => r.ItemCode == "CHK-COM");
        Assert.True(comSaldo.InCatalog);
        Assert.Equal(12m, comSaldo.Balance);

        var zerado = Assert.Single(saldos!, r => r.ItemCode == "CHK-ZERO");
        Assert.True(zerado.InCatalog);
        Assert.Equal(0m, zerado.Balance);

        // Item que nem existe no Almox: não é erro — segue como compra externa.
        var fora = Assert.Single(saldos!, r => r.ItemCode == "NAO-CADASTRADO");
        Assert.False(fora.InCatalog);
        Assert.Equal(0m, fora.Balance);

        // Minúsculas e espaços são normalizados.
        var (_, normalizado) = await GetAsync<StockCheckRow[]>(c,
            "/api/v1/purchases/stock-check?items=%20chk-com%20", requisitante);
        Assert.Equal(12m, Assert.Single(normalizado!).Balance);
    }
}
