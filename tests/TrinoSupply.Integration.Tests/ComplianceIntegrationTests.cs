using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// Compliance Score (Fase 05) pela stack HTTP real. Cenário de aceite: compra emergencial (−20)
/// com cotação em que só um fornecedor respondeu (−25) tem de dar exatamente 55 — e a varredura da
/// auditoria com <c>maxScore=70</c> tem de trazer só ela.
/// </summary>
[Collection("pilot")]
public sealed class ComplianceIntegrationTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken);
    private record UserResp(Guid UserId);
    private record RoleResp(Guid RoleId);
    private record ReqResp(Guid RequisitionId);
    private record QuotationResp(Guid QuotationId);
    private record OrdersResp(Guid[] OrderIds);
    private record LineRow(Guid Id, string ItemCode);
    private record QuotationRow(Guid Id, string Status, LineRow[] Lines);
    private record PenaltyRow(string Rule, int Points, string Title, string Evidence);
    private record ComplianceRow(
        Guid OrderId, long OrderNumber, Guid RequisitionId, string SupplierCode, string SupplierName,
        DateTimeOffset IssuedAt, decimal NetValue, int Score, string Band, string Summary,
        bool Emergencial, DateOnly? NeededBy, DateOnly CreatedOn, bool SupplierHomologated,
        int QuotationResponses, bool AwardedOutsideLowest, bool AwardJustified, PenaltyRow[] Penalties);

    private const string Password = "senha12345";

    [Fact]
    public async Task Urgencia_com_fornecedor_unico_da_55_e_a_varredura_a_encontra()
    {
        var c = fixture.Client();
        var email = $"{Guid.NewGuid():N}@cmp.com";
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = "Compliance Co", taxId = $"5{Guid.NewGuid():N}"[..14], adminSubject = $"adm-{Guid.NewGuid():N}",
            adminEmail = email, adminName = "Adm", adminPassword = Password,
        });
        var (_, login) = await PostAsync<LoginResp>(c, "/api/v1/auth/login",
            new { companyId = comp!.CompanyId, email, password = Password });
        var admin = login!.AccessToken;

        async Task<(string subject, string token)> Aprovador(string p)
        {
            var subject = $"{p}-{Guid.NewGuid():N}"[..12];
            var mail = $"{Guid.NewGuid():N}@{p}.com";
            var (_, u) = await PostAsync<UserResp>(c, "/api/v1/users",
                new { subject, email = mail, displayName = subject, password = Password }, admin);
            var (_, role) = await PostAsync<RoleResp>(c, "/api/v1/roles", new { name = $"R-{subject}" }, admin);
            foreach (var perm in new[] { "purchases.read", "purchases.approve" })
                await PostStatusAsync(c, $"/api/v1/roles/{role!.RoleId}/permissions", new { permission = perm }, admin);
            await PostStatusAsync(c, $"/api/v1/users/{u!.UserId}/roles", new { roleId = role!.RoleId }, admin);
            var (_, l) = await PostAsync<LoginResp>(c, "/api/v1/auth/login",
                new { companyId = comp.CompanyId, email = mail, password = Password });
            return (subject, l!.AccessToken);
        }
        var ap1 = await Aprovador("c1");
        var ap2 = await Aprovador("c2");

        await PostStatusAsync(c, "/api/v1/purchases/paying-companies",
            new { code = "EP-CM", legalName = "Pagadora Compliance", taxId = "11.111.222/0001-11" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/cost-centers", new { code = "CC-CM", name = "Centro Compliance" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/suppliers",
            new { code = "FORN-CM1", name = "Único que Respondeu", taxId = "22.222.333/0001-22" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/suppliers",
            new { code = "FORN-CM2", name = "Convidado Mudo", taxId = "33.333.444/0001-33" }, admin);
        await PostStatusAsync(c, "/api/v1/materials/units",
            new { code = "un", name = "un", dimension = "contagem", factorToBase = 1 }, admin);
        await PostStatusAsync(c, "/api/v1/materials/items",
            new { code = "GERADOR-CM", name = "Gerador", baseUnitCode = "un" }, admin);
        await PostStatusAsync(c, "/api/v1/materials/items",
            new { code = "CABO-CM", name = "Cabo", baseUnitCode = "un" }, admin);

        async Task<Guid> Aprovada(string item, string prioridade, DateOnly? necessidade)
        {
            var (_, req) = await PostAsync<ReqResp>(c, "/api/v1/purchases/requisitions", new
            {
                payingCompanyCode = "EP-CM", costCenterCode = "CC-CM", priority = prioridade,
                justification = "teste de compliance", neededBy = necessidade,
                approverLevel1Subject = ap1.subject, approverLevel2Subject = ap2.subject,
                lines = new[] { new { itemCode = item, quantity = 1m, unit = "un" } },
            }, admin);
            var id = req!.RequisitionId;
            await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{id}/submit", null, admin);
            await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{id}/approve", null, ap1.token);
            await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{id}/approve", null, ap2.token);
            return id;
        }

        var fecha = DateTimeOffset.UtcNow.AddDays(2).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var amanha = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        // ---- Processo RUIM: emergencial, dois convidados mas só um respondeu ---------------------
        var reqRuim = await Aprovada("GERADOR-CM", "Emergencial", amanha);
        var (_, cot) = await PostAsync<QuotationResp>(c, $"/api/v1/purchases/requisitions/{reqRuim}/quotation",
            new { supplierCodes = new[] { "FORN-CM1", "FORN-CM2" }, closesAt = fecha }, admin);
        var (_, mapa) = await GetAsync<QuotationRow>(c, $"/api/v1/purchases/quotations/{cot!.QuotationId}", admin);
        var linha = mapa!.Lines.Single().Id;
        await PostStatusAsync(c, $"/api/v1/purchases/quotations/{cot.QuotationId}/proposals",
            new { supplierCode = "FORN-CM1", bids = new[] { new { lineId = linha, unitPrice = 5000m, deliveryDays = 3 } } }, admin);
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/purchases/quotations/{cot.QuotationId}/award",
            new { awards = new[] { new { lineId = linha, supplierCode = "FORN-CM1", note = (string?)null } } }, admin));
        var (_, ocs) = await PostAsync<OrdersResp>(c, $"/api/v1/purchases/quotations/{cot.QuotationId}/orders", null, admin);
        var ocRuim = ocs!.OrderIds.Single();

        // ---- Processo LIMPO: normal, os dois responderam, ganhou o menor preço ------------------
        var reqBoa = await Aprovada("CABO-CM", "Normal", amanha);
        var (_, cot2) = await PostAsync<QuotationResp>(c, $"/api/v1/purchases/requisitions/{reqBoa}/quotation",
            new { supplierCodes = new[] { "FORN-CM1", "FORN-CM2" }, closesAt = fecha }, admin);
        var (_, mapa2) = await GetAsync<QuotationRow>(c, $"/api/v1/purchases/quotations/{cot2!.QuotationId}", admin);
        var linha2 = mapa2!.Lines.Single().Id;
        await PostStatusAsync(c, $"/api/v1/purchases/quotations/{cot2.QuotationId}/proposals",
            new { supplierCode = "FORN-CM1", bids = new[] { new { lineId = linha2, unitPrice = 10m, deliveryDays = 3 } } }, admin);
        await PostStatusAsync(c, $"/api/v1/purchases/quotations/{cot2.QuotationId}/proposals",
            new { supplierCode = "FORN-CM2", bids = new[] { new { lineId = linha2, unitPrice = 12m, deliveryDays = 3 } } }, admin);
        await PostStatusAsync(c, $"/api/v1/purchases/quotations/{cot2.QuotationId}/award",
            new { awards = new[] { new { lineId = linha2, supplierCode = "FORN-CM1", note = (string?)null } } }, admin);
        var (_, ocs2) = await PostAsync<OrdersResp>(c, $"/api/v1/purchases/quotations/{cot2.QuotationId}/orders", null, admin);
        var ocBoa = ocs2!.OrderIds.Single();

        // ---- O aceite: exatamente 55, com as duas penalidades e as evidências --------------------
        var (status, ruim) = await GetAsync<ComplianceRow>(c, $"/api/v1/purchases/orders/{ocRuim}/compliance", admin);
        Assert.Equal(200, status);
        Assert.Equal(55, ruim!.Score);
        Assert.Equal("Atencao", ruim.Band);
        Assert.True(ruim.Emergencial);
        Assert.Equal(1, ruim.QuotationResponses);
        Assert.Equal(2, ruim.Penalties.Length);
        Assert.Contains(ruim.Penalties, p => p.Rule == "CompraEmergencial" && p.Points == 20);
        Assert.Contains(ruim.Penalties, p => p.Rule == "SemConcorrencia" && p.Points == 25
            && p.Evidence.Contains("Apenas uma proposta"));

        var (_, boa) = await GetAsync<ComplianceRow>(c, $"/api/v1/purchases/orders/{ocBoa}/compliance", admin);
        Assert.Equal(100, boa!.Score);
        Assert.Empty(boa.Penalties);
        Assert.Equal(2, boa.QuotationResponses);

        // ---- A varredura da auditoria: filtro por score baixo traz só o processo ruim ------------
        var (_, suspeitos) = await GetAsync<ComplianceRow[]>(c, "/api/v1/purchases/compliance?maxScore=70", admin);
        Assert.Contains(suspeitos!, r => r.OrderId == ocRuim);
        Assert.DoesNotContain(suspeitos!, r => r.OrderId == ocBoa);

        // Sem filtro, os dois aparecem — e o pior vem primeiro.
        var (_, todos) = await GetAsync<ComplianceRow[]>(c, "/api/v1/purchases/compliance", admin);
        var meus = todos!.Where(r => r.OrderId == ocRuim || r.OrderId == ocBoa).ToList();
        Assert.Equal(2, meus.Count);
        Assert.Equal(ocRuim, meus[0].OrderId);
    }
}
