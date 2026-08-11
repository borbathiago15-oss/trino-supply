using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// Fluxo A do almoxarifado (spec Sistema de Almoxarifado): solicitação de EPI/fardamento com fluxo de
/// status — Pendente → Aprovado (gestor) → Em Separação (almoxarifado, com estoque) → Em Rota →
/// Entregue (baixa no estoque). Só o gestor designado aprova.
/// </summary>
[Collection("pilot")]
public sealed class StockRequestIntegrationTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken);
    private record UserResp(Guid UserId);
    private record RoleResp(Guid RoleId);
    private record RequestResp(Guid RequestId);
    private record BalanceResp(decimal Quantity);

    private const string Password = "senha12345";

    private async Task<(Guid company, string admin)> ProvisionAsync(HttpClient c, string legal, string taxId, string email)
    {
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = legal, taxId, adminSubject = $"adm-{Guid.NewGuid():N}",
            adminEmail = email, adminName = "Adm", adminPassword = Password,
        });
        var (_, login) = await PostAsync<LoginResp>(c, "/api/v1/auth/login", new { companyId = comp!.CompanyId, email, password = Password });
        return (comp.CompanyId, login!.AccessToken);
    }

    private async Task<string> UserWithPermsAsync(HttpClient c, string admin, Guid company, string subject, string email, params string[] perms)
    {
        var (_, user) = await PostAsync<UserResp>(c, "/api/v1/users", new { subject, email, displayName = subject, password = Password }, admin);
        var (_, role) = await PostAsync<RoleResp>(c, "/api/v1/roles", new { name = $"R-{subject}" }, admin);
        foreach (var p in perms)
            await PostStatusAsync(c, $"/api/v1/roles/{role!.RoleId}/permissions", new { permission = p }, admin);
        await PostStatusAsync(c, $"/api/v1/users/{user!.UserId}/roles", new { roleId = role!.RoleId }, admin);
        var (_, l) = await PostAsync<LoginResp>(c, "/api/v1/auth/login", new { companyId = company, email, password = Password });
        return l!.AccessToken;
    }

    [Fact]
    public async Task Fluxo_solicitacao_aprovacao_separacao_entrega_baixa_estoque()
    {
        var c = fixture.Client();
        var (company, admin) = await ProvisionAsync(c, "Almox Fluxo", $"6{Guid.NewGuid():N}"[..14], $"{Guid.NewGuid():N}@af.com");

        // Gestor aprovador (warehouse.approve).
        var gestor = await UserWithPermsAsync(c, admin, company, "gestor1", "gestor1@af.com", "warehouse.approve", "warehouse.request");

        // Estoque: unidade + item (EPI) + entrada de 10.
        await PostStatusAsync(c, "/api/v1/materials/units", new { code = "un", name = "un", dimension = "contagem", factorToBase = 1 }, admin);
        await PostStatusAsync(c, "/api/v1/materials/items", new { code = "BOTA-42", name = "Bota 42", baseUnitCode = "un", group = "EPI", ca = "38123" }, admin);
        Assert.Equal(200, await PostStatusAsync(c, "/api/v1/materials/items/BOTA-42/movements", new { direction = 1, quantity = 10 }, admin));

        // Solicitação (admin tem warehouse.request); gestor designado = gestor1.
        var (createStatus, req) = await PostAsync<RequestResp>(c, "/api/v1/materials/requests", new
        {
            companyCode = "EP1", costCenterCode = "CC1", managerSubject = "gestor1", reason = "Danificado",
            lines = new[] { new { itemCode = "BOTA-42", quantity = 3 } },
        }, admin);
        Assert.Equal(201, createStatus);

        // Gestor errado não aprova (só o designado) — usa o próprio admin (não é o gestor) → 403.
        Assert.Equal(403, await PostStatusAsync(c, $"/api/v1/materials/requests/{req!.RequestId}/approve", null, admin));

        // Gestor designado aprova; almoxarifado separa (tem estoque) → despacha → entrega.
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/materials/requests/{req.RequestId}/approve", null, gestor));
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/materials/requests/{req.RequestId}/separation", null, admin));
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/materials/requests/{req.RequestId}/dispatch", null, admin));
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/materials/requests/{req.RequestId}/deliver", null, admin));

        // Entrega deu baixa no estoque: 10 - 3 = 7.
        var (status, bal) = await GetAsync<BalanceResp>(c, "/api/v1/materials/items/BOTA-42/balance", admin);
        Assert.Equal(200, status);
        Assert.Equal(7m, bal!.Quantity);
    }

    [Fact]
    public async Task Separacao_sem_estoque_vai_para_solicitado_compra()
    {
        var c = fixture.Client();
        var (company, admin) = await ProvisionAsync(c, "Almox Sem Estoque", $"6{Guid.NewGuid():N}"[..14], $"{Guid.NewGuid():N}@ae.com");
        var gestor = await UserWithPermsAsync(c, admin, company, "gestor2", "gestor2@ae.com", "warehouse.approve");

        await PostStatusAsync(c, "/api/v1/materials/units", new { code = "un", name = "un", dimension = "contagem", factorToBase = 1 }, admin);
        await PostStatusAsync(c, "/api/v1/materials/items", new { code = "LUVA", name = "Luva", baseUnitCode = "un", group = "EPI" }, admin); // saldo 0

        var (_, req) = await PostAsync<RequestResp>(c, "/api/v1/materials/requests", new
        {
            companyCode = "EP1", costCenterCode = "CC1", managerSubject = "gestor2", reason = "Roteiro mensal",
            lines = new[] { new { itemCode = "LUVA", quantity = 5 } },
        }, admin);
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/materials/requests/{req!.RequestId}/approve", null, gestor));
        // Sem saldo → separação roteia para "Solicitado Compra" (não falha).
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/materials/requests/{req.RequestId}/separation", null, admin));

        // Entregar direto sem estoque não é permitido (estado Solicitado Compra) → 409.
        Assert.Equal(409, await PostStatusAsync(c, $"/api/v1/materials/requests/{req.RequestId}/deliver", null, admin));
    }
}
