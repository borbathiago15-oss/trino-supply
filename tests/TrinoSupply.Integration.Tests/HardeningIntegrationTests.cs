using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// Endurecimento de go-live (Fase A): provisionamento com chave, freio de força bruta no login,
/// bloqueio de usuário e trilha de auditoria dos eventos de negócio.
/// </summary>
[Collection("pilot")]
public sealed class HardeningIntegrationTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken, string RefreshToken);
    private record UserResp(Guid UserId);
    private record ReqResp(Guid RequisitionId);
    private record AuditRow(Guid Id, DateTimeOffset OccurredAt, string Actor, string Action, string? TargetType, string? TargetId, string? Metadata);

    private const string Password = "senha12345";

    private static object NewCompany(string email) => new
    {
        legalName = "Hard Co", taxId = $"8{Guid.NewGuid():N}"[..14], adminSubject = $"adm-{Guid.NewGuid():N}",
        adminEmail = email, adminName = "Adm", adminPassword = Password,
    };

    [Fact]
    public async Task Provisionamento_exige_chave_quando_configurada()
    {
        using var keyed = fixture.Factory.WithWebHostBuilder(b => b.UseSetting("Provisioning:Key", "chave-super-secreta"));
        var c = keyed.CreateClient();

        // Sem header → 403 (mesmo em Development, pois a chave está configurada).
        var without = await PostStatusAsync(c, "/api/v1/companies", NewCompany($"{Guid.NewGuid():N}@k1.com"));
        Assert.Equal(403, without);

        // Header errado → 403.
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/companies") { Content = JsonContent.Create(NewCompany($"{Guid.NewGuid():N}@k2.com")) })
        {
            req.Headers.Add("X-Provisioning-Key", "errada");
            var res = await c.SendAsync(req);
            Assert.Equal(403, (int)res.StatusCode);
        }

        // Header correto → 201.
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/companies") { Content = JsonContent.Create(NewCompany($"{Guid.NewGuid():N}@k3.com")) })
        {
            req.Headers.Add("X-Provisioning-Key", "chave-super-secreta");
            var res = await c.SendAsync(req);
            Assert.Equal(201, (int)res.StatusCode);
        }
    }

    [Fact]
    public async Task Login_trava_a_conta_apos_5_falhas()
    {
        var c = fixture.Client();
        var email = $"{Guid.NewGuid():N}@lock.com";
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", NewCompany(email));

        for (var i = 0; i < 5; i++)
            Assert.Equal(401, await PostStatusAsync(c, "/api/v1/auth/login",
                new { companyId = comp!.CompanyId, email, password = "senha-errada" }));

        // 6ª tentativa — mesmo com a senha CORRETA — é recusada com 429 (conta travada na janela).
        Assert.Equal(429, await PostStatusAsync(c, "/api/v1/auth/login",
            new { companyId = comp!.CompanyId, email, password = Password }));
    }

    [Fact]
    public async Task Usuario_bloqueado_nao_loga_e_perde_o_refresh()
    {
        var c = fixture.Client();
        var adminEmail = $"{Guid.NewGuid():N}@blk.com";
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", NewCompany(adminEmail));
        var (_, adminLogin) = await PostAsync<LoginResp>(c, "/api/v1/auth/login", new { companyId = comp!.CompanyId, email = adminEmail, password = Password });
        var admin = adminLogin!.AccessToken;

        var (_, user) = await PostAsync<UserResp>(c, "/api/v1/users",
            new { subject = $"u-{Guid.NewGuid():N}", email = "alvo@blk.com", displayName = "Alvo", password = Password }, admin);

        var (loginOk, target) = await PostAsync<LoginResp>(c, "/api/v1/auth/login", new { companyId = comp.CompanyId, email = "alvo@blk.com", password = Password });
        Assert.Equal(200, loginOk);

        // Bloqueia (spec "Bloqueado: SIM") → login negado e refresh token revogado.
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/users/{user!.UserId}/status", new { active = false }, admin));
        Assert.Equal(401, await PostStatusAsync(c, "/api/v1/auth/login", new { companyId = comp.CompanyId, email = "alvo@blk.com", password = Password }));
        Assert.Equal(401, await PostStatusAsync(c, "/api/v1/auth/refresh", new { companyId = comp.CompanyId, refreshToken = target!.RefreshToken }));

        // Reativa → volta a logar.
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/users/{user.UserId}/status", new { active = true }, admin));
        Assert.Equal(200, await PostStatusAsync(c, "/api/v1/auth/login", new { companyId = comp.CompanyId, email = "alvo@blk.com", password = Password }));
    }

    [Fact]
    public async Task Auditoria_registra_login_e_eventos_de_negocio()
    {
        var c = fixture.Client();
        var email = $"{Guid.NewGuid():N}@aud.com";
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", NewCompany(email));
        var (_, login) = await PostAsync<LoginResp>(c, "/api/v1/auth/login", new { companyId = comp!.CompanyId, email, password = Password });
        var admin = login!.AccessToken;

        // Movimento de estoque + solicitação de compra criada/submetida — devem entrar na trilha.
        await PostStatusAsync(c, "/api/v1/materials/units", new { code = "un", name = "un", dimension = "contagem", factorToBase = 1 }, admin);
        await PostStatusAsync(c, "/api/v1/materials/items", new { code = "AUD-1", name = "Item Auditado", baseUnitCode = "un" }, admin);
        Assert.Equal(200, await PostStatusAsync(c, "/api/v1/materials/items/AUD-1/movements", new { direction = 1, quantity = 5 }, admin));

        await PostStatusAsync(c, "/api/v1/purchases/paying-companies", new { code = "EPA", legalName = "Pagadora Aud", taxId = "44.444.444/0001-44" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/cost-centers", new { code = "CCA", name = "Centro Aud" }, admin);
        var (_, req) = await PostAsync<ReqResp>(c, "/api/v1/purchases/requisitions", new
        {
            payingCompanyCode = "EPA", costCenterCode = "CCA", priority = "Normal", justification = "auditoria",
            approverLevel1Subject = "ap1", approverLevel2Subject = "ap2",
            lines = new[] { new { itemCode = "AUD-1", quantity = 1, unit = "un" } },
        }, admin);
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{req!.RequisitionId}/submit", null, admin));

        var (status, audit) = await GetAsync<AuditRow[]>(c, "/api/v1/audit?limit=100", admin);
        Assert.Equal(200, status);
        var actions = audit!.Select(a => a.Action).ToHashSet();
        Assert.Contains("auth.login", actions);
        Assert.Contains("materials.movement.posted", actions);
        Assert.Contains("purchases.requisition.created", actions);
        Assert.Contains("purchases.requisition.submitted", actions);
    }
}
