using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// Fase 03 — scorecard OTIF pela stack HTTP real, a partir dos fatos de operação: prazo prometido na
/// OC × data da conferência na doca, líquido × pedido, ocorrências. Cenário de aceite: uma entrega
/// com 1 dia de atraso e 10% de avaria penaliza pontualidade E integralidade; o fornecedor que
/// entregou no prazo e completo fica à frente no ranking.
/// </summary>
[Collection("pilot")]
public sealed class ScorecardIntegrationTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken);
    private record UserResp(Guid UserId);
    private record RoleResp(Guid RoleId);
    private record ReqResp(Guid RequisitionId);
    private record OrderResp(Guid OrderId);
    private record SupplierRow(Guid Id, string Code);
    private record ScoreRow(
        Guid SupplierId, string SupplierCode, string SupplierName, string Period,
        int LinesEvaluated, int LinesWithDeadline, int LinesWithoutDeadline,
        decimal OnTimeRate, decimal InFullRate, decimal OtifIndex, decimal DamageRate,
        int Occurrences, string Tier);
    private record ScorecardRow(Guid SupplierId, string SupplierCode, int Months, ScoreRow Overall, ScoreRow[] Periods);
    private record SummaryRow(Guid OrderId, PendingRow[] Lines);
    private record PendingRow(Guid OrderLineId, string ItemCode);

    private const string Password = "senha12345";

    [Fact]
    public async Task Atraso_e_avaria_derrubam_o_OTIF_e_o_ranking_reflete()
    {
        var c = fixture.Client();
        var email = $"{Guid.NewGuid():N}@otif.com";
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = "Otif Co", taxId = $"2{Guid.NewGuid():N}"[..14], adminSubject = $"adm-{Guid.NewGuid():N}",
            adminEmail = email, adminName = "Adm", adminPassword = Password,
        });
        var (_, login) = await PostAsync<LoginResp>(c, "/api/v1/auth/login",
            new { companyId = comp!.CompanyId, email, password = Password });
        var admin = login!.AccessToken;

        async Task<(string subject, string token)> Aprovador(string prefixo)
        {
            var subject = $"{prefixo}-{Guid.NewGuid():N}"[..12];
            var mail = $"{Guid.NewGuid():N}@{prefixo}.com";
            var (_, u) = await PostAsync<UserResp>(c, "/api/v1/users",
                new { subject, email = mail, displayName = subject, password = Password }, admin);
            var (_, role) = await PostAsync<RoleResp>(c, "/api/v1/roles", new { name = $"R-{subject}" }, admin);
            foreach (var p in new[] { "purchases.read", "purchases.approve" })
                await PostStatusAsync(c, $"/api/v1/roles/{role!.RoleId}/permissions", new { permission = p }, admin);
            await PostStatusAsync(c, $"/api/v1/users/{u!.UserId}/roles", new { roleId = role!.RoleId }, admin);
            var (_, l) = await PostAsync<LoginResp>(c, "/api/v1/auth/login",
                new { companyId = comp.CompanyId, email = mail, password = Password });
            return (subject, l!.AccessToken);
        }
        var ap1 = await Aprovador("o1");
        var ap2 = await Aprovador("o2");

        await PostStatusAsync(c, "/api/v1/purchases/paying-companies",
            new { code = "EP-OT", legalName = "Pagadora Otif", taxId = "11.222.333/0001-11" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/cost-centers", new { code = "CC-OT", name = "Centro Otif" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/suppliers",
            new { code = "FORN-ATRASA", name = "Atrasa Sempre", taxId = "22.333.444/0001-22" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/suppliers",
            new { code = "FORN-PONTUAL", name = "Pontual Ltda", taxId = "33.444.555/0001-33" }, admin);
        await PostStatusAsync(c, "/api/v1/materials/units",
            new { code = "un", name = "un", dimension = "contagem", factorToBase = 1 }, admin);
        foreach (var item in new[] { "OTIF-A", "OTIF-B" })
            await PostStatusAsync(c, "/api/v1/materials/items",
                new { code = item, name = $"Item {item}", baseUnitCode = "un" }, admin);

        // Um pedido com dois itens → uma OC por fornecedor (compra dividida).
        var (_, req) = await PostAsync<ReqResp>(c, "/api/v1/purchases/requisitions", new
        {
            payingCompanyCode = "EP-OT", costCenterCode = "CC-OT", priority = "Normal",
            justification = "medir desempenho de entrega",
            approverLevel1Subject = ap1.subject, approverLevel2Subject = ap2.subject,
            lines = new[]
            {
                new { itemCode = "OTIF-A", quantity = 100m, unit = "un" },
                new { itemCode = "OTIF-B", quantity = 100m, unit = "un" },
            },
        }, admin);
        var reqId = req!.RequisitionId;
        await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{reqId}/submit", null, admin);
        await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{reqId}/approve", null, ap1.token);
        await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{reqId}/approve", null, ap2.token);

        // Prazo prometido: ONTEM nas duas OCs — a conferência de hoje decide quem atrasou.
        var ontem = DateTimeOffset.UtcNow.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var hoje = DateTimeOffset.UtcNow.AddDays(2).ToString("yyyy-MM-ddTHH:mm:ssZ");

        var (_, ocAtrasa) = await PostAsync<OrderResp>(c, $"/api/v1/purchases/requisitions/{reqId}/order", new
        {
            payingCompanyCode = "EP-OT", supplierCode = "FORN-ATRASA",
            lines = new[] { new { itemCode = "OTIF-A", unitPrice = 5m, deliveryDate = ontem } },
        }, admin);
        var (_, ocPontual) = await PostAsync<OrderResp>(c, $"/api/v1/purchases/requisitions/{reqId}/order", new
        {
            payingCompanyCode = "EP-OT", supplierCode = "FORN-PONTUAL",
            lines = new[] { new { itemCode = "OTIF-B", unitPrice = 5m, deliveryDate = hoje } },
        }, admin);

        async Task<Guid> LinhaDaOc(Guid orderId)
        {
            var (_, resumo) = await GetAsync<SummaryRow>(c, $"/api/v1/purchases/orders/{orderId}/receipts", admin);
            return resumo!.Lines.Single().OrderLineId;
        }

        // FORN-ATRASA: entregou depois do prazo e com 10% avariado → penalizado nos dois índices.
        Assert.Equal(201, await PostStatusAsync(c, $"/api/v1/purchases/orders/{ocAtrasa!.OrderId}/receipts", new
        {
            invoiceNumber = "NF-ATR",
            lines = new[]
            {
                new
                {
                    orderLineId = await LinhaDaOc(ocAtrasa.OrderId), quantityReceived = 100m, quantityDamaged = 10m,
                    occurrence = "Avaria", occurrenceNote = "10 unidades molhadas",
                },
            },
        }, admin));

        // FORN-PONTUAL: dentro do prazo e completo.
        Assert.Equal(201, await PostStatusAsync(c, $"/api/v1/purchases/orders/{ocPontual!.OrderId}/receipts", new
        {
            invoiceNumber = "NF-PON",
            lines = new[]
            {
                new { orderLineId = await LinhaDaOc(ocPontual.OrderId), quantityReceived = 100m, quantityDamaged = 0m },
            },
        }, admin));

        // ---- Scorecard do fornecedor que atrasou ------------------------------------------------
        var (_, fornecedores) = await GetAsync<SupplierRow[]>(c, "/api/v1/purchases/suppliers", admin);
        var idAtrasa = fornecedores!.Single(f => f.Code == "FORN-ATRASA").Id;
        var idPontual = fornecedores.Single(f => f.Code == "FORN-PONTUAL").Id;

        var (status, card) = await GetAsync<ScorecardRow>(c, $"/api/v1/purchases/suppliers/{idAtrasa}/scorecard", admin);
        Assert.Equal(200, status);
        Assert.Equal(1, card!.Overall.LinesEvaluated);
        Assert.Equal(1, card.Overall.LinesWithDeadline);
        Assert.Equal(0m, card.Overall.OnTimeRate);    // chegou depois do prazo
        Assert.Equal(0m, card.Overall.InFullRate);    // líquido 90 < 100 pedidas
        Assert.Equal(0m, card.Overall.OtifIndex);
        Assert.Equal(10m, card.Overall.DamageRate);   // 10 de 100
        Assert.Equal(1, card.Overall.Occurrences);
        Assert.Equal("Critico", card.Overall.Tier);
        Assert.NotEmpty(card.Periods);                // série por mês

        // ---- E o pontual ------------------------------------------------------------------------
        var (_, bom) = await GetAsync<ScorecardRow>(c, $"/api/v1/purchases/suppliers/{idPontual}/scorecard", admin);
        Assert.Equal(100m, bom!.Overall.OnTimeRate);
        Assert.Equal(100m, bom.Overall.InFullRate);
        Assert.Equal("Ouro", bom.Overall.Tier);

        // ---- Ranking: o melhor OTIF vem primeiro ------------------------------------------------
        var (_, ranking) = await GetAsync<ScoreRow[]>(c, "/api/v1/purchases/suppliers/scorecard", admin);
        var posicoes = ranking!.Select(r => r.SupplierCode).ToList();
        Assert.True(posicoes.IndexOf("FORN-PONTUAL") < posicoes.IndexOf("FORN-ATRASA"),
            "o fornecedor pontual deve aparecer antes do atrasado no ranking");
    }
}
