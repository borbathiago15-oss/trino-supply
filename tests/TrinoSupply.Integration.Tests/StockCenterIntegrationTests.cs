using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// Spec v2/v3: saída de estoque identifica o centro de custo debitado (avulsa e em lote) e o
/// lote de entrada/saída é atômico — qualquer linha inválida desfaz o lote inteiro.
/// </summary>
[Collection("pilot")]
public sealed class StockCenterIntegrationTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken);
    private record BalanceResp(Guid ItemId, string ItemCode, decimal Quantity, int Version);
    private record MovementRow(Guid Id, string Direction, decimal Quantity, DateTimeOffset OccurredAt, string? Reason, string? CostCenterCode);

    private const string Password = "senha12345";

    private async Task<(HttpClient c, string admin)> SetupAsync(string tag)
    {
        var c = fixture.Client();
        var email = $"{Guid.NewGuid():N}@{tag}.com";
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = "Stock Co", taxId = $"6{Guid.NewGuid():N}"[..14], adminSubject = $"adm-{Guid.NewGuid():N}",
            adminEmail = email, adminName = "Adm", adminPassword = Password,
        });
        var (_, login) = await PostAsync<LoginResp>(c, "/api/v1/auth/login",
            new { companyId = comp!.CompanyId, email, password = Password });
        var admin = login!.AccessToken;

        await PostStatusAsync(c, "/api/v1/materials/units", new { code = "un", name = "un", dimension = "contagem", factorToBase = 1 }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/cost-centers", new { code = "CC-OBRA", name = "Obra Norte" }, admin);
        return (c, admin);
    }

    [Fact]
    public async Task Saida_avulsa_exige_centro_de_custo_e_ele_fica_no_ledger()
    {
        var (c, admin) = await SetupAsync("ctr");
        await PostStatusAsync(c, "/api/v1/materials/items", new { code = "CTR-1", name = "Item Centro", baseUnitCode = "un" }, admin);
        Assert.Equal(200, await PostStatusAsync(c, "/api/v1/materials/items/CTR-1/movements",
            new { direction = 1, quantity = 10 }, admin));

        // Saída SEM centro → 400 (materials.stock.center_required).
        Assert.Equal(400, await PostStatusAsync(c, "/api/v1/materials/items/CTR-1/movements",
            new { direction = 2, quantity = 3 }, admin));

        // Saída COM centro → ok; ledger guarda o centro (normalizado).
        Assert.Equal(200, await PostStatusAsync(c, "/api/v1/materials/items/CTR-1/movements",
            new { direction = 2, quantity = 3, costCenterCode = "cc-obra", reason = "uso na obra" }, admin));

        var (_, bal) = await GetAsync<BalanceResp>(c, "/api/v1/materials/items/CTR-1/balance", admin);
        Assert.Equal(7m, bal!.Quantity);

        var (_, movs) = await GetAsync<MovementRow[]>(c, "/api/v1/materials/items/CTR-1/movements", admin);
        var saida = Assert.Single(movs!.Where(mv => mv.Direction == "Out"));
        Assert.Equal("CC-OBRA", saida.CostCenterCode);
    }

    [Fact]
    public async Task Lote_atomico_de_entrada_e_saida()
    {
        var (c, admin) = await SetupAsync("lot");
        await PostStatusAsync(c, "/api/v1/materials/items", new { code = "LOT-A", name = "Lote A", baseUnitCode = "un" }, admin);
        await PostStatusAsync(c, "/api/v1/materials/items", new { code = "LOT-B", name = "Lote B", baseUnitCode = "un" }, admin);

        // Entrada em lote (sem centro — opcional na entrada).
        Assert.Equal(200, await PostStatusAsync(c, "/api/v1/materials/movements/batch", new
        {
            direction = 1, reason = "carga inicial",
            lines = new[] { new { itemCode = "LOT-A", quantity = 10 }, new { itemCode = "LOT-B", quantity = 5 } },
        }, admin));

        // Saída em lote SEM centro → 400.
        Assert.Equal(400, await PostStatusAsync(c, "/api/v1/materials/movements/batch", new
        {
            direction = 2, reason = "sem centro",
            lines = new[] { new { itemCode = "LOT-A", quantity = 1 } },
        }, admin));

        // Saída em lote com uma linha SEM saldo → aborta TUDO (LOT-A intacto).
        Assert.Equal(400, await PostStatusAsync(c, "/api/v1/materials/movements/batch", new
        {
            direction = 2, costCenterCode = "CC-OBRA", reason = "lote inválido",
            lines = new[] { new { itemCode = "LOT-A", quantity = 2 }, new { itemCode = "LOT-B", quantity = 99 } },
        }, admin));
        var (_, balA) = await GetAsync<BalanceResp>(c, "/api/v1/materials/items/LOT-A/balance", admin);
        Assert.Equal(10m, balA!.Quantity);

        // Saída em lote válida → debita os dois.
        Assert.Equal(200, await PostStatusAsync(c, "/api/v1/materials/movements/batch", new
        {
            direction = 2, costCenterCode = "CC-OBRA", reason = "consumo da obra",
            lines = new[] { new { itemCode = "LOT-A", quantity = 2 }, new { itemCode = "LOT-B", quantity = 5 } },
        }, admin));
        (_, balA) = await GetAsync<BalanceResp>(c, "/api/v1/materials/items/LOT-A/balance", admin);
        var (_, balB) = await GetAsync<BalanceResp>(c, "/api/v1/materials/items/LOT-B/balance", admin);
        Assert.Equal(8m, balA!.Quantity);
        Assert.Equal(0m, balB!.Quantity);

        var (_, movsB) = await GetAsync<MovementRow[]>(c, "/api/v1/materials/items/LOT-B/movements", admin);
        var saidaB = Assert.Single(movsB!.Where(mv => mv.Direction == "Out"));
        Assert.Equal("CC-OBRA", saidaB.CostCenterCode);
    }
}
