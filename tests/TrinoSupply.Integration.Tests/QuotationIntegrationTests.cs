using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// Cotação (Fase 03) pela stack HTTP real: requisição aprovada vai a mercado, dois fornecedores
/// propõem, o mapa de equalização compara, a adjudicação é POR ITEM (cada um com o seu vencedor) e
/// o ciclo fecha em OCs — uma por fornecedor, no preço adjudicado e no prazo que ele prometeu.
/// </summary>
[Collection("pilot")]
public sealed class QuotationIntegrationTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken);
    private record UserResp(Guid UserId);
    private record RoleResp(Guid RoleId);
    private record ReqResp(Guid RequisitionId);
    private record QuotationResp(Guid QuotationId);
    private record OrdersResp(Guid[] OrderIds);

    private record BidRow(
        Guid SupplierId, string SupplierCode, string SupplierName, decimal UnitPrice, decimal TotalPrice,
        int? DeliveryDays, string? Notes, bool IsLowest, bool IsLate, decimal PercentAboveLowest,
        decimal? PercentAboveLastPaid, bool OverpriceAlert);
    private record LineRow(
        Guid Id, Guid RequisitionLineId, string ItemCode, decimal Quantity, string Unit,
        Guid? AwardedSupplierId, string? AwardedSupplierCode, decimal? AwardedUnitPrice, string? AwardNote,
        decimal? LastPaidPrice, DateTimeOffset? LastPaidAt, BidRow[] Bids);
    private record ParticipantRow(
        Guid SupplierId, string SupplierCode, string SupplierName, bool HasResponded, bool IsLate,
        DateTimeOffset? RespondedAt, string? PaymentTerms, string? FreightTerms, DateOnly? ValidUntil,
        string? Notes, int ItemsQuoted, decimal Total, string? OtifTier, decimal? OtifIndex);
    private record QuotationRow(
        Guid Id, long Number, Guid RequisitionId, string Status, string CreatedBy, DateTimeOffset CreatedAt,
        DateTimeOffset ClosesAt, bool IsClosed, string? Notes, string? CancelledBy, DateTimeOffset? CancelledAt,
        string? CancelReason, LineRow[] Lines, ParticipantRow[] Participants);

    private record OrderLineRow(string ItemCode, decimal Quantity, decimal UnitPrice, DateTimeOffset? DeliveryDate);
    private record OrderRow(Guid Id, long Number, string SupplierCode, string Status, decimal NetValue, OrderLineRow[] Lines);
    private record RequisitionRow(Guid Id, string Status);

    private const string Password = "senha12345";

    /// <summary>Cria a empresa, dois aprovadores e o cadastro básico; devolve o token do admin.</summary>
    private async Task<(System.Net.Http.HttpClient C, string Admin, string Ap1, string Ap1Token, string Ap2, string Ap2Token)>
        CenarioAsync(string prefixo, params string[] itens)
    {
        var c = fixture.Client();
        var email = $"{Guid.NewGuid():N}@{prefixo}.com";
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = $"{prefixo} Co", taxId = $"3{Guid.NewGuid():N}"[..14],
            adminSubject = $"adm-{Guid.NewGuid():N}", adminEmail = email, adminName = "Adm", adminPassword = Password,
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
        var ap1 = await Aprovador($"{prefixo}1");
        var ap2 = await Aprovador($"{prefixo}2");

        await PostStatusAsync(c, "/api/v1/purchases/paying-companies",
            new { code = "EP-CT", legalName = "Pagadora Cotação", taxId = "44.555.666/0001-44" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/cost-centers", new { code = "CC-CT", name = "Centro Cotação" }, admin);
        await PostStatusAsync(c, "/api/v1/materials/units",
            new { code = "un", name = "un", dimension = "contagem", factorToBase = 1 }, admin);
        foreach (var item in itens)
            await PostStatusAsync(c, "/api/v1/materials/items",
                new { code = item, name = $"Item {item}", baseUnitCode = "un" }, admin);

        return (c, admin, ap1.subject, ap1.token, ap2.subject, ap2.token);
    }

    private static async Task<Guid> RequisicaoAprovadaAsync(
        System.Net.Http.HttpClient c, string admin, string ap1, string ap1Token, string ap2, string ap2Token,
        params (string Item, decimal Qtd)[] linhas)
    {
        var (_, req) = await PostAsync<ReqResp>(c, "/api/v1/purchases/requisitions", new
        {
            payingCompanyCode = "EP-CT", costCenterCode = "CC-CT", priority = "Normal",
            justification = "compra via concorrência",
            approverLevel1Subject = ap1, approverLevel2Subject = ap2,
            lines = linhas.Select(l => new { itemCode = l.Item, quantity = l.Qtd, unit = "un" }).ToArray(),
        }, admin);
        var id = req!.RequisitionId;
        await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{id}/submit", null, admin);
        await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{id}/approve", null, ap1Token);
        await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{id}/approve", null, ap2Token);
        return id;
    }

    [Fact]
    public async Task Concorrencia_equaliza_adjudica_por_item_e_gera_uma_OC_por_fornecedor()
    {
        var (c, admin, ap1, t1, ap2, t2) = await CenarioAsync("ctq", "LUVA-CT", "BOTA-CT");
        await PostStatusAsync(c, "/api/v1/purchases/suppliers",
            new { code = "FORN-BARATO", name = "Barato Ltda", taxId = "55.666.777/0001-55" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/suppliers",
            new { code = "FORN-RAPIDO", name = "Rápido SA", taxId = "66.777.888/0001-66" }, admin);

        var reqId = await RequisicaoAprovadaAsync(c, admin, ap1, t1, ap2, t2, ("LUVA-CT", 100m), ("BOTA-CT", 50m));

        // ---- Abre a concorrência (sem lista de linhas = todas as pendentes) ----------------------
        var fecha = DateTimeOffset.UtcNow.AddDays(3).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var (statusAbrir, cot) = await PostAsync<QuotationResp>(c, $"/api/v1/purchases/requisitions/{reqId}/quotation",
            new { supplierCodes = new[] { "FORN-BARATO", "FORN-RAPIDO" }, closesAt = fecha, notes = "EPI obra norte" }, admin);
        Assert.Equal(201, statusAbrir);
        var cotId = cot!.QuotationId;

        var (_, mapa0) = await GetAsync<QuotationRow>(c, $"/api/v1/purchases/quotations/{cotId}", admin);
        Assert.Equal("Open", mapa0!.Status);
        var luva = mapa0.Lines.Single(l => l.ItemCode == "LUVA-CT").Id;
        var bota = mapa0.Lines.Single(l => l.ItemCode == "BOTA-CT").Id;

        // Um item não pode estar em duas concorrências ao mesmo tempo.
        Assert.Equal(409, await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{reqId}/quotation",
            new { supplierCodes = new[] { "FORN-BARATO", "FORN-RAPIDO" }, closesAt = fecha }, admin));

        // ---- Propostas: o barato é menor na luva; o rápido é menor na bota e entrega antes --------
        // Primeira rodada do barato: preço alto. A recotação abaixo tem de APAGAR esta proposta.
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/purchases/quotations/{cotId}/proposals", new
        {
            supplierCode = "FORN-BARATO",
            bids = new[] { new { lineId = luva, unitPrice = 99m, deliveryDays = 40 } },
        }, admin));

        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/purchases/quotations/{cotId}/proposals", new
        {
            supplierCode = "FORN-BARATO", paymentTerms = "30 dias", freightTerms = "CIF",
            bids = new[]
            {
                new { lineId = luva, unitPrice = 10m, deliveryDays = 20 },
                new { lineId = bota, unitPrice = 90m, deliveryDays = 20 },
            },
        }, admin));
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/purchases/quotations/{cotId}/proposals", new
        {
            supplierCode = "FORN-RAPIDO", paymentTerms = "à vista", freightTerms = "FOB",
            bids = new[]
            {
                new { lineId = luva, unitPrice = 12m, deliveryDays = 5 },
                new { lineId = bota, unitPrice = 80m, deliveryDays = 5 },
            },
        }, admin));

        // ---- Mapa de equalização -----------------------------------------------------------------
        var (_, mapa) = await GetAsync<QuotationRow>(c, $"/api/v1/purchases/quotations/{cotId}", admin);
        var luvaRow = mapa!.Lines.Single(l => l.ItemCode == "LUVA-CT");
        Assert.Equal(2, luvaRow.Bids.Length);
        Assert.Equal("FORN-BARATO", luvaRow.Bids[0].SupplierCode);          // ordenado por preço
        Assert.True(luvaRow.Bids[0].IsLowest);
        Assert.Equal(1000m, luvaRow.Bids[0].TotalPrice);                    // 100 un × R$ 10
        Assert.Equal(20m, luvaRow.Bids[1].PercentAboveLowest);              // 12 é 20% acima de 10
        Assert.All(mapa.Participants, p => Assert.True(p.HasResponded));
        Assert.Equal(2, mapa.Participants.Single(p => p.SupplierCode == "FORN-RAPIDO").ItemsQuoted);

        // ---- Adjudicação: fora do menor preço exige justificativa --------------------------------
        Assert.Equal(400, await PostStatusAsync(c, $"/api/v1/purchases/quotations/{cotId}/award",
            new { awards = new[] { new { lineId = luva, supplierCode = "FORN-RAPIDO", note = (string?)null } } }, admin));

        // Compra mista: luva com o mais barato, bota com o mais rápido (que ali TAMBÉM é o menor).
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/purchases/quotations/{cotId}/award", new
        {
            awards = new[]
            {
                new { lineId = luva, supplierCode = "FORN-BARATO", note = (string?)null },
                new { lineId = bota, supplierCode = "FORN-RAPIDO", note = (string?)null },
            },
        }, admin));

        var (_, adjudicada) = await GetAsync<QuotationRow>(c, $"/api/v1/purchases/quotations/{cotId}", admin);
        Assert.Equal("Awarded", adjudicada!.Status);
        Assert.Equal("FORN-BARATO", adjudicada.Lines.Single(l => l.ItemCode == "LUVA-CT").AwardedSupplierCode);
        Assert.Equal(80m, adjudicada.Lines.Single(l => l.ItemCode == "BOTA-CT").AwardedUnitPrice);

        // Cotação adjudicada não aceita nova proposta.
        Assert.Equal(409, await PostStatusAsync(c, $"/api/v1/purchases/quotations/{cotId}/proposals",
            new { supplierCode = "FORN-BARATO", bids = new[] { new { lineId = bota, unitPrice = 1m, deliveryDays = 1 } } }, admin));

        // ---- Fecha o ciclo: uma OC por fornecedor vencedor ---------------------------------------
        var (statusOc, ocs) = await PostAsync<OrdersResp>(c, $"/api/v1/purchases/quotations/{cotId}/orders", null, admin);
        Assert.Equal(201, statusOc);
        Assert.Equal(2, ocs!.OrderIds.Length);

        var (_, todas) = await GetAsync<OrderRow[]>(c, "/api/v1/purchases/orders", admin);
        var emitidas = todas!.Where(o => ocs.OrderIds.Contains(o.Id)).ToList();
        var ocBarato = emitidas.Single(o => o.SupplierCode == "FORN-BARATO");
        var ocRapido = emitidas.Single(o => o.SupplierCode == "FORN-RAPIDO");

        Assert.Equal("LUVA-CT", ocBarato.Lines.Single().ItemCode);
        Assert.Equal(10m, ocBarato.Lines.Single().UnitPrice);        // preço congelado na adjudicação
        Assert.Equal(1000m, ocBarato.NetValue);
        Assert.Equal(80m, ocRapido.Lines.Single().UnitPrice);
        Assert.Equal(4000m, ocRapido.NetValue);                      // 50 pares × R$ 80

        // O prazo prometido na proposta virou a data de entrega da OC — é o que o OTIF vai cobrar.
        Assert.NotNull(ocRapido.Lines.Single().DeliveryDate);
        Assert.True(ocRapido.Lines.Single().DeliveryDate! < ocBarato.Lines.Single().DeliveryDate!,
            "quem prometeu 5 dias deve ter prazo anterior a quem prometeu 20");

        // A requisição ficou inteiramente coberta pelas duas OCs.
        var (_, reqs) = await GetAsync<RequisitionRow[]>(c, "/api/v1/purchases/requisitions", admin);
        Assert.Equal("Ordered", reqs!.Single(r => r.Id == reqId).Status);

        // Não há o que pedir de novo.
        Assert.Equal(400, await PostStatusAsync(c, $"/api/v1/purchases/quotations/{cotId}/orders", null, admin));
    }

    [Fact]
    public async Task Proposta_acima_do_ultimo_preco_pago_acende_alerta_de_sobrepreco()
    {
        var (c, admin, ap1, t1, ap2, t2) = await CenarioAsync("sob", "CABO-SB");
        await PostStatusAsync(c, "/api/v1/purchases/suppliers",
            new { code = "FORN-SB1", name = "Sobrepreço Um", taxId = "77.888.999/0001-77" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/suppliers",
            new { code = "FORN-SB2", name = "Sobrepreço Dois", taxId = "88.999.111/0001-88" }, admin);

        // ---- Compra anterior do mesmo item a R$ 10,00 — a referência interna de preço -------------
        var req1 = await RequisicaoAprovadaAsync(c, admin, ap1, t1, ap2, t2, ("CABO-SB", 10m));
        Assert.Equal(201, await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{req1}/order", new
        {
            payingCompanyCode = "EP-CT", supplierCode = "FORN-SB1",
            lines = new[] { new { itemCode = "CABO-SB", unitPrice = 10m } },
        }, admin));

        // ---- Nova demanda do mesmo item, agora em concorrência ------------------------------------
        var req2 = await RequisicaoAprovadaAsync(c, admin, ap1, t1, ap2, t2, ("CABO-SB", 20m));
        var fecha = DateTimeOffset.UtcNow.AddDays(2).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var (_, cot) = await PostAsync<QuotationResp>(c, $"/api/v1/purchases/requisitions/{req2}/quotation",
            new { supplierCodes = new[] { "FORN-SB1", "FORN-SB2" }, closesAt = fecha }, admin);

        var (_, mapa0) = await GetAsync<QuotationRow>(c, $"/api/v1/purchases/quotations/{cot!.QuotationId}", admin);
        var cabo = mapa0!.Lines.Single().Id;

        // R$ 12,50 = +25% sobre o último pago (dispara); R$ 10,50 = +5% (não dispara).
        await PostStatusAsync(c, $"/api/v1/purchases/quotations/{cot.QuotationId}/proposals",
            new { supplierCode = "FORN-SB1", bids = new[] { new { lineId = cabo, unitPrice = 12.50m, deliveryDays = 7 } } }, admin);
        await PostStatusAsync(c, $"/api/v1/purchases/quotations/{cot.QuotationId}/proposals",
            new { supplierCode = "FORN-SB2", bids = new[] { new { lineId = cabo, unitPrice = 10.50m, deliveryDays = 7 } } }, admin);

        var (_, mapa) = await GetAsync<QuotationRow>(c, $"/api/v1/purchases/quotations/{cot.QuotationId}", admin);
        var linha = mapa!.Lines.Single();
        Assert.Equal(10m, linha.LastPaidPrice);
        Assert.NotNull(linha.LastPaidAt);

        var caro = linha.Bids.Single(b => b.SupplierCode == "FORN-SB1");
        Assert.Equal(25m, caro.PercentAboveLastPaid);
        Assert.True(caro.OverpriceAlert, "proposta 25% acima do último preço pago deve acender o alerta");

        var aceitavel = linha.Bids.Single(b => b.SupplierCode == "FORN-SB2");
        Assert.Equal(5m, aceitavel.PercentAboveLastPaid);
        Assert.False(aceitavel.OverpriceAlert);   // dentro da tolerância de 15%

        // O alerta informa, não bloqueia: o comprador ainda pode escolher o caro — justificando.
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/purchases/quotations/{cot.QuotationId}/award",
            new { awards = new[] { new { lineId = cabo, supplierCode = "FORN-SB1", note = "único com certificado INMETRO" } } }, admin));
    }
}
