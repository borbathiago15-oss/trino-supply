using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using TrinoSupply.Api.Procurement;
using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// Fluxo completo da OC pela stack HTTP real (Postgres via Testcontainers, role trino_app → RLS efetivo):
/// cadastro de empresa pagadora + fornecedor fiscal, importação de itens em lote via planilha Excel,
/// aprovação com SoD (aprovador ≠ requisitante) e emissão da OC com preços do vencedor + totais.
/// </summary>
[Collection("pilot")]
public sealed class OcFlowIntegrationTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken);
    private record RequisitionResp(Guid RequisitionId);
    private record ImportResp(Guid RequisitionId, int Imported);
    private record OrderResp(Guid OrderId);
    private record UserResp(Guid UserId);
    private record OrderLineView(string ItemCode, decimal Quantity, decimal UnitPrice, decimal ServiceValue);
    private record OrderView(
        long Number, Guid PayingCompanyId, string PayingCompanyName, string SupplierCode, string SupplierName,
        string PaymentTerms, decimal ProductsValue, decimal NetValue, OrderLineView[] Lines);

    private const string Password = "senha12345";

    private async Task<Guid> ProvisionAsync(HttpClient c, string legal, string taxId, string subject, string email)
    {
        var (status, body) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = legal, taxId, adminSubject = subject, adminEmail = email, adminName = subject, adminPassword = Password,
        });
        Assert.Equal(201, status);
        return body!.CompanyId;
    }

    private async Task<string> LoginAsync(HttpClient c, Guid companyId, string email)
    {
        var (status, body) = await PostAsync<LoginResp>(c, "/api/v1/auth/login", new { companyId, email, password = Password });
        Assert.Equal(200, status);
        return body!.AccessToken;
    }

    private static MultipartFormDataContent ExcelForm(params (string Code, decimal Qty, string Unit)[] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Itens");
        ws.Cell(1, 1).Value = "Código do Item";
        ws.Cell(1, 2).Value = "Quantidade";
        ws.Cell(1, 3).Value = "Unidade";
        for (var i = 0; i < rows.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = rows[i].Code;
            ws.Cell(i + 2, 2).Value = rows[i].Qty;
            ws.Cell(i + 2, 3).Value = rows[i].Unit;
        }
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        var bytes = ms.ToArray();

        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(RequisitionExcel.ContentType);
        content.Add(file, "file", "itens.xlsx");
        return content;
    }

    [Fact]
    public async Task Template_de_planilha_e_baixavel_e_valido()
    {
        var c = fixture.Client();
        var company = await ProvisionAsync(c, "Empresa Tpl", "61.000.000/0001-01", "adminTpl", "admin@tpl.com");
        var admin = await LoginAsync(c, company, "admin@tpl.com");

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/purchases/requisitions/import-template");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        using var res = await c.SendAsync(req);

        Assert.Equal(200, (int)res.StatusCode);
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);

        // O arquivo baixado abre como planilha e traz o cabeçalho esperado.
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet("Itens");
        Assert.Equal("Código do Item", ws.Cell(1, 1).GetString());
        Assert.Equal("Quantidade", ws.Cell(1, 2).GetString());
        Assert.Equal("Unidade", ws.Cell(1, 3).GetString());
    }

    [Fact]
    public async Task Fluxo_completo_importa_lote_aprova_e_emite_OC()
    {
        var c = fixture.Client();
        var company = await ProvisionAsync(c, "Empresa OC", "62.000.000/0001-02", "adminOc", "admin@oc.com");
        var admin = await LoginAsync(c, company, "admin@oc.com");

        // Segundo usuário aprovador (o admin será o requisitante → SoD exige outro para aprovar).
        var (createUser, user) = await PostAsync<UserResp>(c, "/api/v1/users",
            new { subject = "aprovadorOc", email = "aprov@oc.com", displayName = "Aprovador", password = Password }, admin);
        Assert.Equal(201, createUser);

        // Cria papel de aprovador, concede as permissões e atribui ao segundo usuário (admin gerencia papéis).
        var (roleStatus, role) = await PostAsync<RoleResp>(c, "/api/v1/roles", new { name = "Aprovador" }, admin);
        Assert.Equal(201, roleStatus);
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/roles/{role!.RoleId}/permissions", new { permission = "purchases.read" }, admin));
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/roles/{role.RoleId}/permissions", new { permission = "purchases.approve" }, admin));
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/users/{user!.UserId}/roles", new { roleId = role.RoleId }, admin));
        var approver = await LoginAsync(c, company, "aprov@oc.com");

        // Empresa pagadora (CNPJ do grupo) + fornecedor vencedor com dados fiscais.
        Assert.Equal(201, await PostStatusAsync(c, "/api/v1/purchases/paying-companies", new
        {
            code = "EP1", legalName = "Brilho Terceirizacoes Ltda", taxId = "05.345.258/0004-96",
            stateRegistration = "16.411.708-3", address = "R Manoel Cesar de Melo S/N", district = "Distrito Industrial",
            city = "Alhandra", state = "PB", zipCode = "58320-000", phone = "81 3243-8500", email = "compras@grupotrino.com.br",
        }, admin));

        Assert.Equal(201, await PostStatusAsync(c, "/api/v1/purchases/suppliers", new
        {
            code = "3963", name = "Rede & Vidros Decoracoes", taxId = "17.381.510/0001-59",
            address = "R Joao Cancio 925", district = "Manaira", city = "Joao Pessoa", state = "PB",
            zipCode = "58038-341", paymentTerms = "A Vista", paymentMethod = "Deposito Bancario",
        }, admin));

        // Importa itens em lote via planilha Excel → cria a requisição (rascunho).
        ImportResp? imported;
        using (var form = ExcelForm(("VIDRO-TEMP", 10m, "un"), ("ESPELHO", 5m, "m2")))
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/purchases/requisitions/import") { Content = form };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
            using var res = await c.SendAsync(req);
            Assert.Equal(201, (int)res.StatusCode);
            imported = await res.Content.ReadFromJsonAsync<ImportResp>();
        }
        Assert.Equal(2, imported!.Imported);
        var reqId = imported.RequisitionId;

        // Envia (admin) e aprova (aprovador distinto — respeita SoD).
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{reqId}/submit", null, admin));
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{reqId}/approve", null, approver));

        // Emite a OC: seleciona pagadora + fornecedor vencedor + preços por linha + totais.
        var (orderStatus, order) = await PostAsync<OrderResp>(c, $"/api/v1/purchases/requisitions/{reqId}/order", new
        {
            payingCompanyCode = "EP1",
            supplierCode = "3963",
            lines = new[]
            {
                new { itemCode = "VIDRO-TEMP", unitPrice = 120.00m, irrfPercent = 0m, issPercent = 0m },
                new { itemCode = "ESPELHO", unitPrice = 80.00m, irrfPercent = 0m, issPercent = 0m },
            },
            ipiValue = 0m, icmsValue = 0m, discountValue = 0m, otherExpenses = 0m,
        }, admin);
        Assert.Equal(201, orderStatus);

        // Confere a OC materializada: número, pagadora, fornecedor, preços e totais calculados.
        var (getStatus, view) = await GetAsync<OrderView>(c, $"/api/v1/purchases/orders/{order!.OrderId}", admin);
        Assert.Equal(200, getStatus);
        Assert.True(view!.Number >= 1);
        Assert.Equal("Brilho Terceirizacoes Ltda", view.PayingCompanyName);
        Assert.Equal("3963", view.SupplierCode);
        Assert.Equal("A Vista", view.PaymentTerms); // snapshot do fornecedor
        Assert.Equal(2, view.Lines.Length);
        // Produtos = 10*120 + 5*80 = 1200 + 400 = 1600
        Assert.Equal(1600m, view.ProductsValue);
        Assert.Equal(1600m, view.NetValue);
        Assert.Contains(view.Lines, l => l.ItemCode == "VIDRO-TEMP" && l.UnitPrice == 120.00m && l.ServiceValue == 1200m);
    }

    private record RoleResp(Guid RoleId);
}
