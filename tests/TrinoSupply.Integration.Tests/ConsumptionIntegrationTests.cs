using System.Net.Http.Headers;
using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// Fluxo B do almoxarifado (spec Sistema de Almoxarifado): baixa de consumo por colaborador.
/// O responsável pelo estoque registra a entrega (empresa + centro + colaborador + motivo) e o saldo
/// do produto é reduzido. Sem saldo suficiente, a baixa é recusada (garantia do ledger).
/// </summary>
[Collection("pilot")]
public sealed class ConsumptionIntegrationTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken);
    private record CollaboratorResp(Guid CollaboratorId);
    private record ConsumptionResp(Guid ConsumptionId);
    private record BalanceResp(decimal Quantity);

    private const string Password = "senha12345";

    private async Task<(Guid company, string token)> ProvisionAsync(HttpClient c, string legal, string taxId, string email)
    {
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = legal, taxId, adminSubject = $"adm-{Guid.NewGuid():N}",
            adminEmail = email, adminName = "Adm", adminPassword = Password,
        });
        var (_, login) = await PostAsync<LoginResp>(c, "/api/v1/auth/login", new { companyId = comp!.CompanyId, email, password = Password });
        return (comp.CompanyId, login!.AccessToken);
    }

    [Fact]
    public async Task Baixa_de_consumo_por_colaborador_reduz_o_saldo()
    {
        var c = fixture.Client();
        var (_, admin) = await ProvisionAsync(c, "Almox Co", $"6{Guid.NewGuid():N}"[..14], $"{Guid.NewGuid():N}@almox.com");

        // Cadastros base: unidade, produto (EPI) e entrada de estoque.
        Assert.Equal(201, await PostStatusAsync(c, "/api/v1/materials/units",
            new { code = "un", name = "unidade", dimension = "contagem", factorToBase = 1 }, admin));
        Assert.Equal(201, await PostStatusAsync(c, "/api/v1/materials/items",
            new { code = "BOTA-42", name = "Bota de segurança 42", baseUnitCode = "un", group = "EPI" }, admin));
        Assert.Equal(200, await PostStatusAsync(c, "/api/v1/materials/items/BOTA-42/movements",
            new { direction = 1, quantity = 10 }, admin)); // entrada: saldo 10

        // Colaborador que vai receber o EPI (com matrícula + data de contratação p/ a ficha).
        var (_, colab) = await PostAsync<CollaboratorResp>(c, "/api/v1/materials/collaborators",
            new { name = "João Silva", registration = "m123", costCenterCode = "CC1", companyCode = "EP1", admissionDate = "2026-01-15" }, admin);

        // Baixa de consumo: entrega 3 unidades ao colaborador (nova contratação).
        var (baixaStatus, cons) = await PostAsync<ConsumptionResp>(c, "/api/v1/materials/consumptions", new
        {
            companyCode = "EP1", costCenterCode = "CC1", collaboratorId = colab!.CollaboratorId,
            reason = "Nova contratação", lines = new[] { new { itemCode = "BOTA-42", quantity = 3 } },
        }, admin);
        Assert.Equal(201, baixaStatus);

        var (status, bal) = await GetAsync<BalanceResp>(c, "/api/v1/materials/items/BOTA-42/balance", admin);
        Assert.Equal(200, status);
        Assert.Equal(7m, bal!.Quantity); // 10 - 3 = 7

        // A baixa gera a Ficha de Entrega de EPI (PDF) pré-preenchida para assinatura.
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/materials/consumptions/{cons!.ConsumptionId}/ficha");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        var resp = await c.SendAsync(req);
        Assert.Equal(200, (int)resp.StatusCode);
        Assert.Equal("application/pdf", resp.Content.Headers.ContentType?.MediaType);
        var pdf = await resp.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
    }

    [Fact]
    public async Task Baixa_sem_saldo_suficiente_e_recusada()
    {
        var c = fixture.Client();
        var (_, admin) = await ProvisionAsync(c, "Almox Co2", $"6{Guid.NewGuid():N}"[..14], $"{Guid.NewGuid():N}@almox2.com");

        await PostStatusAsync(c, "/api/v1/materials/units",
            new { code = "un", name = "unidade", dimension = "contagem", factorToBase = 1 }, admin);
        await PostStatusAsync(c, "/api/v1/materials/items",
            new { code = "LUVA", name = "Luva nitrílica", baseUnitCode = "un", group = "EPI" }, admin); // saldo 0

        var (_, colab) = await PostAsync<CollaboratorResp>(c, "/api/v1/materials/collaborators",
            new { name = "Maria", registration = (string?)null, costCenterCode = (string?)null, companyCode = (string?)null }, admin);

        // Sem estoque → baixa recusada (400) e saldo permanece 0.
        Assert.Equal(400, await PostStatusAsync(c, "/api/v1/materials/consumptions", new
        {
            companyCode = "EP1", costCenterCode = "CC1", collaboratorId = colab!.CollaboratorId,
            reason = "Substituição", lines = new[] { new { itemCode = "LUVA", quantity = 5 } },
        }, admin));

        var (_, bal) = await GetAsync<BalanceResp>(c, "/api/v1/materials/items/LUVA/balance", admin);
        Assert.Equal(0m, bal!.Quantity);
    }
}
