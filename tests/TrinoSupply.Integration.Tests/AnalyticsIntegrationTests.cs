using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// Dashboards analíticos (v3): consumo por centro de custo e por colaborador (Materials) e
/// tempo de ciclo de aprovação (Procurement).
/// </summary>
[Collection("pilot")]
public sealed class AnalyticsIntegrationTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken);
    private record ReqResp(Guid RequisitionId);
    private record UserResp(Guid UserId);
    private record RoleResp(Guid RoleId);
    private record CollabResp(Guid CollaboratorId);
    private record CenterRow(string CostCenterCode, string ItemCode, string ItemName, decimal TotalQuantity, int Movements);
    private record CollabRow(string CollaboratorName, string? Registration, string CostCenterCode, int Deliveries, decimal TotalItems);
    private record CycleResp(int Total, int Approved, int Rejected, int FulfilledFromStock, int Pending,
        double? AvgHoursToLevel1, double? AvgHoursToLevel2);

    private const string Password = "senha12345";

    [Fact]
    public async Task Consumo_por_centro_e_por_colaborador()
    {
        var c = fixture.Client();
        var email = $"{Guid.NewGuid():N}@ana.com";
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = "Ana Co", taxId = $"5{Guid.NewGuid():N}"[..14], adminSubject = $"adm-{Guid.NewGuid():N}",
            adminEmail = email, adminName = "Adm", adminPassword = Password,
        });
        var (_, login) = await PostAsync<LoginResp>(c, "/api/v1/auth/login",
            new { companyId = comp!.CompanyId, email, password = Password });
        var admin = login!.AccessToken;

        await PostStatusAsync(c, "/api/v1/materials/units", new { code = "un", name = "un", dimension = "contagem", factorToBase = 1 }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/cost-centers", new { code = "CC-SUL", name = "Obra Sul" }, admin);
        await PostStatusAsync(c, "/api/v1/materials/items", new { code = "ANA-1", name = "Botina", baseUnitCode = "un", group = "EPI" }, admin);
        Assert.Equal(200, await PostStatusAsync(c, "/api/v1/materials/items/ANA-1/movements", new { direction = 1, quantity = 20 }, admin));

        // Saída avulsa com centro + entrega a colaborador (Fluxo B) — ambas alimentam o consumo por centro.
        Assert.Equal(200, await PostStatusAsync(c, "/api/v1/materials/items/ANA-1/movements",
            new { direction = 2, quantity = 4, costCenterCode = "CC-SUL", reason = "uso da obra" }, admin));

        var (_, collab) = await PostAsync<CollabResp>(c, "/api/v1/materials/collaborators",
            new { name = "José Silva", registration = "M-123", costCenterCode = "CC-SUL", companyCode = "TRINO" }, admin);
        Assert.Equal(201, await PostStatusAsync(c, "/api/v1/materials/consumptions", new
        {
            companyCode = "TRINO", costCenterCode = "CC-SUL", collaboratorId = collab!.CollaboratorId,
            reason = "Nova contratação",
            lines = new[] { new { itemCode = "ANA-1", quantity = 2 } },
        }, admin));

        var (s1, porCentro) = await GetAsync<CenterRow[]>(c, "/api/v1/materials/analytics/consumption-by-center?days=30", admin);
        Assert.Equal(200, s1);
        var row = Assert.Single(porCentro!.Where(r => r.CostCenterCode == "CC-SUL" && r.ItemCode == "ANA-1"));
        Assert.Equal(6m, row.TotalQuantity);   // 4 avulsa + 2 entrega
        Assert.Equal(2, row.Movements);

        var (s2, porColab) = await GetAsync<CollabRow[]>(c, "/api/v1/materials/analytics/consumption-by-collaborator?days=30", admin);
        Assert.Equal(200, s2);
        var jose = Assert.Single(porColab!.Where(r => r.CollaboratorName == "José Silva"));
        Assert.Equal(1, jose.Deliveries);
        Assert.Equal(2m, jose.TotalItems);
        Assert.Equal("CC-SUL", jose.CostCenterCode);
    }

    [Fact]
    public async Task Tempo_de_ciclo_de_aprovacao()
    {
        var c = fixture.Client();
        var email = $"{Guid.NewGuid():N}@cyc.com";
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = "Cyc Co", taxId = $"4{Guid.NewGuid():N}"[..14], adminSubject = "adm-cyc",
            adminEmail = email, adminName = "Adm", adminPassword = Password,
        });
        var (_, login) = await PostAsync<LoginResp>(c, "/api/v1/auth/login",
            new { companyId = comp!.CompanyId, email, password = Password });
        var admin = login!.AccessToken;

        // SoD: o requisitante não aprova — cria dois aprovadores dedicados (nível 1 e 2).
        async Task<(string subject, string token)> Approver(string subject, string mail)
        {
            var (_, u) = await PostAsync<UserResp>(c, "/api/v1/users", new { subject, email = mail, displayName = subject, password = Password }, admin);
            var (_, role) = await PostAsync<RoleResp>(c, "/api/v1/roles", new { name = $"R-{subject}" }, admin);
            foreach (var p in new[] { "purchases.read", "purchases.approve" })
                await PostStatusAsync(c, $"/api/v1/roles/{role!.RoleId}/permissions", new { permission = p }, admin);
            await PostStatusAsync(c, $"/api/v1/users/{u!.UserId}/roles", new { roleId = role!.RoleId }, admin);
            var (_, l) = await PostAsync<LoginResp>(c, "/api/v1/auth/login", new { companyId = comp.CompanyId, email = mail, password = Password });
            return (subject, l!.AccessToken);
        }
        var ap1 = await Approver($"c1-{Guid.NewGuid():N}"[..12], $"{Guid.NewGuid():N}@c1.com");
        var ap2 = await Approver($"c2-{Guid.NewGuid():N}"[..12], $"{Guid.NewGuid():N}@c2.com");

        await PostStatusAsync(c, "/api/v1/purchases/paying-companies", new { code = "CYC", legalName = "Cyc Pag", taxId = "33.333.333/0001-33" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/cost-centers", new { code = "CC-CYC", name = "Centro Cyc" }, admin);
        var (_, req) = await PostAsync<ReqResp>(c, "/api/v1/purchases/requisitions", new
        {
            payingCompanyCode = "CYC", costCenterCode = "CC-CYC", priority = "Normal", justification = "ciclo",
            approverLevel1Subject = ap1.subject, approverLevel2Subject = ap2.subject,
            lines = new[] { new { itemCode = "X-1", quantity = 1, unit = "un" } },
        }, admin);
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{req!.RequisitionId}/submit", null, admin));

        // Ainda pendente na janela.
        var (_, before) = await GetAsync<CycleResp>(c, "/api/v1/purchases/analytics/cycle?days=30", admin);
        Assert.Equal(1, before!.Total);
        Assert.Equal(1, before.Pending);

        // Duas aprovações (nível 1 e 2, cada um pelo seu aprovador) → médias calculadas.
        Assert.Equal(200, await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{req.RequisitionId}/approve", null, ap1.token));
        Assert.Equal(200, await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{req.RequisitionId}/approve", null, ap2.token));

        var (status, after) = await GetAsync<CycleResp>(c, "/api/v1/purchases/analytics/cycle?days=30", admin);
        Assert.Equal(200, status);
        Assert.Equal(1, after!.Total);
        Assert.Equal(0, after.Pending);
        Assert.Equal(1, after.Approved + after.FulfilledFromStock);
        Assert.NotNull(after.AvgHoursToLevel1);
        Assert.NotNull(after.AvgHoursToLevel2);
        Assert.True(after.AvgHoursToLevel2 >= after.AvgHoursToLevel1);
    }
}
