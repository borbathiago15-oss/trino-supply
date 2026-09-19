using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// Conciliação fiscal de três pontas (Fase 05) pela stack HTTP real: o XML da NF-e entra por upload,
/// é cruzado com a OC e com a conferência da doca, e só segue para o financeiro se fechar. Os dois
/// cenários de aceite: match perfeito libera; preço 15% acima trava com <c>DIV-ERR-PRECO</c>.
/// </summary>
[Collection("pilot")]
public sealed class InvoiceMatchIntegrationTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken);
    private record UserResp(Guid UserId);
    private record RoleResp(Guid RoleId);
    private record ReqResp(Guid RequisitionId);
    private record OrderResp(Guid OrderId);
    private record PendingRow(Guid OrderLineId, string ItemCode);
    private record SummaryRow(Guid OrderId, PendingRow[] Lines);

    private record DivergenceRow(
        string ItemCode, string Kind, string Code, decimal Expected, decimal Found,
        decimal DeviationPercent, bool WithinTolerance, string Message);
    private record ImportedRow(Guid InvoiceId, string AccessKey, string Status, bool ReleasedToFinance,
        DivergenceRow[] Divergences);
    private record MatchLineRow(
        string ItemCode, decimal QuantityOrdered, decimal UnitPriceOrdered, decimal? QuantityInvoiced,
        decimal? UnitPriceInvoiced, decimal QuantityReceived, decimal QuantityDamaged, bool Matched,
        bool NotInvoiced, DivergenceRow[] Divergences);
    private record InvoiceRow(
        Guid Id, Guid PurchaseOrderId, string AccessKey, long Number, string Series, DateTimeOffset IssuedAt,
        string EmitterTaxId, string EmitterName, decimal TotalValue, string ImportedBy, DateTimeOffset ImportedAt,
        string Status, DateTimeOffset? MatchedAt, string? MatchSummary, bool ReleasedToFinance,
        string? ReleasedBy, DateTimeOffset? ReleasedAt, string? ReleaseNote,
        object[] Lines, MatchLineRow[] Match);
    private record OrderMatchRow(
        Guid OrderId, long Number, string SupplierCode, string SupplierName, string Status,
        decimal PriceTolerancePercent, decimal QuantityTolerancePercent, InvoiceRow[] Invoices);

    private const string Password = "senha12345";
    private const string CnpjFornecedor = "12345678000199";

    // ---- XML de NF-e mínimo, porém no layout real da SEFAZ ---------------------------------------

    /// <summary>Completa os 43 dígitos com o verificador módulo 11 — a chave tem de ser legítima.</summary>
    private static string ComDv(string prefixo43)
    {
        var soma = 0;
        var peso = 2;
        for (var i = 42; i >= 0; i--)
        {
            soma += (prefixo43[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }
        var resto = soma % 11;
        return prefixo43 + (resto is 0 or 1 ? 0 : 11 - resto);
    }

    private static string Chave(int numeroNota, string cnpj = CnpjFornecedor, string modelo = "55") =>
        ComDv($"3526{"09"}{cnpj}{modelo}001{numeroNota:D9}1{numeroNota:D8}");

    private static string Xml(
        int numeroNota, IEnumerable<(string Code, decimal Qtd, decimal Preco)> itens,
        string cnpj = CnpjFornecedor, string modelo = "55")
    {
        var chave = Chave(numeroNota, cnpj, modelo);
        var lista = itens.ToList();
        var sb = new StringBuilder();
        var n = 1;
        foreach (var (code, qtd, preco) in lista)
        {
            var total = Math.Round(qtd * preco, 2, MidpointRounding.AwayFromZero);
            sb.Append($"""
                <det nItem="{n++}">
                  <prod>
                    <cProd>{code}</cProd>
                    <xProd>Produto {code}</xProd>
                    <NCM>85444900</NCM>
                    <CFOP>5102</CFOP>
                    <uCom>un</uCom>
                    <qCom>{qtd.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)}</qCom>
                    <vUnCom>{preco.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)}</vUnCom>
                    <vProd>{total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}</vProd>
                  </prod>
                </det>
                """);
        }
        var soma = lista.Sum(i => Math.Round(i.Qtd * i.Preco, 2, MidpointRounding.AwayFromZero));

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
              <NFe>
                <infNFe Id="NFe{chave}" versao="4.00">
                  <ide>
                    <cUF>35</cUF><natOp>Venda</natOp><mod>{modelo}</mod><serie>1</serie>
                    <nNF>{numeroNota}</nNF><dhEmi>2026-09-18T10:30:00-03:00</dhEmi><tpNF>1</tpNF>
                  </ide>
                  <emit>
                    <CNPJ>{cnpj}</CNPJ><xNome>Fornecedor Fiscal Ltda</xNome>
                  </emit>
                  {sb}
                  <total>
                    <ICMSTot><vNF>{soma.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}</vNF></ICMSTot>
                  </total>
                </infNFe>
              </NFe>
            </nfeProc>
            """;
    }

    private static async Task<(int Status, ImportedRow? Body)> EnviarXmlAsync(
        System.Net.Http.HttpClient c, Guid orderId, string xml, string token)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(xml));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
        content.Add(file, "file", "nfe.xml");

        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/purchases/orders/{orderId}/invoices")
        {
            Content = content,
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await c.SendAsync(req);
        var body = res.IsSuccessStatusCode
            ? await res.Content.ReadFromJsonAsync<ImportedRow>(Json)
            : null;
        return ((int)res.StatusCode, body);
    }

    /// <summary>Cenário base: empresa, aprovadores, fornecedor com o CNPJ do XML, item e unidade.</summary>
    private async Task<(System.Net.Http.HttpClient C, string Admin, Guid OrderId, Guid LineId)>
        OcAsync(string item, decimal quantidade, decimal precoUnitario)
    {
        var c = fixture.Client();
        var email = $"{Guid.NewGuid():N}@nf.com";
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = "Fiscal Co", taxId = $"4{Guid.NewGuid():N}"[..14], adminSubject = $"adm-{Guid.NewGuid():N}",
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
        var ap1 = await Aprovador("n1");
        var ap2 = await Aprovador("n2");

        await PostStatusAsync(c, "/api/v1/purchases/paying-companies",
            new { code = "EP-NF", legalName = "Pagadora Fiscal", taxId = "99.888.777/0001-99" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/cost-centers", new { code = "CC-NF", name = "Centro Fiscal" }, admin);
        // O CNPJ do cadastro vem formatado; o do XML vem sem máscara — o domínio compara só dígitos.
        await PostStatusAsync(c, "/api/v1/purchases/suppliers",
            new { code = "FORN-NF", name = "Fornecedor Fiscal Ltda", taxId = "12.345.678/0001-99" }, admin);
        await PostStatusAsync(c, "/api/v1/materials/units",
            new { code = "un", name = "un", dimension = "contagem", factorToBase = 1 }, admin);
        await PostStatusAsync(c, "/api/v1/materials/items",
            new { code = item, name = $"Item {item}", baseUnitCode = "un" }, admin);

        var (_, req) = await PostAsync<ReqResp>(c, "/api/v1/purchases/requisitions", new
        {
            payingCompanyCode = "EP-NF", costCenterCode = "CC-NF", priority = "Normal",
            justification = "compra para conferencia fiscal",
            approverLevel1Subject = ap1.subject, approverLevel2Subject = ap2.subject,
            lines = new[] { new { itemCode = item, quantity = quantidade, unit = "un" } },
        }, admin);
        var reqId = req!.RequisitionId;
        await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{reqId}/submit", null, admin);
        await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{reqId}/approve", null, ap1.token);
        await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{reqId}/approve", null, ap2.token);

        var (_, oc) = await PostAsync<OrderResp>(c, $"/api/v1/purchases/requisitions/{reqId}/order", new
        {
            payingCompanyCode = "EP-NF", supplierCode = "FORN-NF",
            lines = new[] { new { itemCode = item, unitPrice = precoUnitario } },
        }, admin);

        var (_, resumo) = await GetAsync<SummaryRow>(c, $"/api/v1/purchases/orders/{oc!.OrderId}/receipts", admin);
        return (c, admin, oc.OrderId, resumo!.Lines.Single().OrderLineId);
    }

    [Fact]
    public async Task Match_perfeito_concilia_e_libera_para_o_financeiro()
    {
        var (c, admin, orderId, lineId) = await OcAsync("CABO-NF", 100m, 15m);

        // Doca: chegaram as 100, sem avaria.
        Assert.Equal(201, await PostStatusAsync(c, $"/api/v1/purchases/orders/{orderId}/receipts", new
        {
            invoiceNumber = "NF-777",
            lines = new[] { new { orderLineId = lineId, quantityReceived = 100m, quantityDamaged = 0m } },
        }, admin));

        // NF-e: 100 un a R$ 15,00 — as três pontas batem.
        var (status, importada) = await EnviarXmlAsync(c, orderId, Xml(777, [("CABO-NF", 100m, 15m)]), admin);

        Assert.Equal(201, status);
        Assert.Equal("Matched", importada!.Status);
        Assert.True(importada.ReleasedToFinance);
        Assert.Empty(importada.Divergences);
        Assert.Equal(44, importada.AccessKey.Length);

        var (_, tela) = await GetAsync<OrderMatchRow>(c, $"/api/v1/purchases/orders/{orderId}/invoices", admin);
        var nota = Assert.Single(tela!.Invoices);
        Assert.Equal(1500m, nota.TotalValue);
        Assert.Equal("Conciliado", nota.MatchSummary);

        var linha = Assert.Single(nota.Match);
        Assert.Equal(100m, linha.QuantityOrdered);
        Assert.Equal(100m, linha.QuantityInvoiced);
        Assert.Equal(100m, linha.QuantityReceived);
        Assert.True(linha.Matched);

        // O mesmo arquivo não entra duas vezes.
        var (repetida, _) = await EnviarXmlAsync(c, orderId, Xml(777, [("CABO-NF", 100m, 15m)]), admin);
        Assert.Equal(409, repetida);
    }

    [Fact]
    public async Task Preco_faturado_acima_da_tolerancia_trava_o_financeiro_ate_alguem_justificar()
    {
        var (c, admin, orderId, lineId) = await OcAsync("FIO-NF", 50m, 10m);

        Assert.Equal(201, await PostStatusAsync(c, $"/api/v1/purchases/orders/{orderId}/receipts", new
        {
            invoiceNumber = "NF-888",
            lines = new[] { new { orderLineId = lineId, quantityReceived = 50m, quantityDamaged = 0m } },
        }, admin));

        // NF-e a R$ 11,50 contra R$ 10,00 acordados: +15%, muito além da folga de 0,5%.
        var (status, importada) = await EnviarXmlAsync(c, orderId, Xml(888, [("FIO-NF", 50m, 11.50m)]), admin);

        Assert.Equal(201, status);
        Assert.Equal("Divergent", importada!.Status);
        Assert.False(importada.ReleasedToFinance);

        var d = Assert.Single(importada.Divergences);
        Assert.Equal("DIV-ERR-PRECO", d.Code);
        Assert.Equal(10m, d.Expected);
        Assert.Equal(11.50m, d.Found);
        Assert.Equal(15m, d.DeviationPercent);
        Assert.False(d.WithinTolerance);

        // Liberar sem justificativa não passa.
        Assert.Equal(400, await PostStatusAsync(c, $"/api/v1/purchases/invoices/{importada.InvoiceId}/release",
            new { note = "   " }, admin));

        // Com justificativa, passa — e a exceção fica registrada com nome e motivo.
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/purchases/invoices/{importada.InvoiceId}/release",
            new { note = "reajuste acordado por e-mail com o fornecedor em 15/09" }, admin));

        var (_, tela) = await GetAsync<OrderMatchRow>(c, $"/api/v1/purchases/orders/{orderId}/invoices", admin);
        var nota = Assert.Single(tela!.Invoices);
        Assert.True(nota.ReleasedToFinance);
        Assert.Contains("reajuste acordado", nota.ReleaseNote);
        Assert.Contains("DIV-ERR-PRECO", nota.MatchSummary);

        // Liberar de novo não faz sentido.
        Assert.Equal(409, await PostStatusAsync(c, $"/api/v1/purchases/invoices/{importada.InvoiceId}/release",
            new { note = "de novo" }, admin));
    }

    [Fact]
    public async Task Nota_de_outro_fornecedor_e_recusada_antes_de_entrar()
    {
        var (c, admin, orderId, _) = await OcAsync("TUBO-NF", 10m, 5m);

        // Chave e emitente de outro CNPJ: anexar ao pedido errado é o começo de um pagamento indevido.
        var (status, _) = await EnviarXmlAsync(c, orderId,
            Xml(999, [("TUBO-NF", 10m, 5m)], cnpj: "98765432000188"), admin);

        Assert.Equal(400, status);

        var (_, tela) = await GetAsync<OrderMatchRow>(c, $"/api/v1/purchases/orders/{orderId}/invoices", admin);
        Assert.Empty(tela!.Invoices);
    }

    [Fact]
    public async Task Xml_corrompido_nao_derruba_a_importacao()
    {
        var (c, admin, orderId, _) = await OcAsync("CHAPA-NF", 10m, 5m);

        var (status, _) = await EnviarXmlAsync(c, orderId, "<nfeProc><quebrado>", admin);

        Assert.Equal(400, status);   // o problema está no arquivo, não no servidor
    }

    private static readonly System.Text.Json.JsonSerializerOptions Json =
        new(System.Text.Json.JsonSerializerDefaults.Web);
}
