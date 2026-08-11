using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using TrinoSupply.Api.Materials;
using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// Importação em lote de itens/produtos via planilha (spec Sistema de Compras): cria os itens com
/// grupo/família e unidades ausentes automaticamente; a listagem filtra por grupo.
/// </summary>
[Collection("pilot")]
public sealed class ItemImportTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken);
    private record ItemRow(Guid Id, string Code, string Name, string Group);

    private const string Password = "senha12345";

    private static MultipartFormDataContent ExcelForm(params (string Code, string Name, string Unit, string Group)[] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Produtos");
        ws.Cell(1, 1).Value = "Código"; ws.Cell(1, 2).Value = "Descrição"; ws.Cell(1, 3).Value = "Unidade"; ws.Cell(1, 4).Value = "Grupo";
        for (var i = 0; i < rows.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = rows[i].Code;
            ws.Cell(i + 2, 2).Value = rows[i].Name;
            ws.Cell(i + 2, 3).Value = rows[i].Unit;
            ws.Cell(i + 2, 4).Value = rows[i].Group;
        }
        var ms = new MemoryStream(); wb.SaveAs(ms);
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(ms.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue(ItemExcel.ContentType);
        content.Add(file, "file", "produtos.xlsx");
        return content;
    }

    [Fact]
    public async Task Importa_itens_em_lote_com_grupo_e_filtra()
    {
        var c = fixture.Client();
        var (_, company) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = "Import Co", taxId = $"5{Guid.NewGuid():N}"[..14], adminSubject = $"adm-{Guid.NewGuid():N}",
            adminEmail = $"{Guid.NewGuid():N}@imp.com", adminName = "Adm", adminPassword = Password,
        });
        var email = await AdminEmail(company!.CompanyId);
        var (_, login) = await PostAsync<LoginResp>(c, "/api/v1/auth/login", new { companyId = company.CompanyId, email, password = Password });
        var admin = login!.AccessToken;

        // Template baixável
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/materials/items/import-template"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
            using var res = await c.SendAsync(req);
            Assert.Equal(200, (int)res.StatusCode);
            Assert.True((await res.Content.ReadAsByteArrayAsync()).Length > 0);
        }

        // Importa 3 itens (unidades 'un'/'cx' criadas automaticamente se ausentes)
        int imported;
        using (var form = ExcelForm(
            ("DETERG-5L", "Detergente 5L", "un", "Limpeza"),
            ("LUVA-NIT", "Luva nitrílica", "cx", "EPI"),
            ("PAPEL-A4", "Papel sulfite A4", "un", "Material de escritório")))
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/materials/items/import") { Content = form };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
            using var res = await c.SendAsync(req);
            Assert.Equal(200, (int)res.StatusCode);
            var body = await res.Content.ReadFromJsonAsync<ImportResp>();
            imported = body!.Imported;
        }
        Assert.Equal(3, imported);

        // Lista geral tem os 3; filtro por grupo recorta
        var (_, all) = await GetAsync<ItemRow[]>(c, "/api/v1/materials/items", admin);
        Assert.True(all!.Length >= 3);
        Assert.Contains(all, i => i.Code == "DETERG-5L" && i.Group == "Limpeza");

        var (_, limpeza) = await GetAsync<ItemRow[]>(c, "/api/v1/materials/items?group=Limpeza", admin);
        Assert.All(limpeza!, i => Assert.Equal("Limpeza", i.Group));
        Assert.Contains(limpeza!, i => i.Code == "DETERG-5L");
        Assert.DoesNotContain(limpeza!, i => i.Code == "LUVA-NIT");
    }

    private record ImportResp(int Imported, string[] Warnings);

    private async Task<string> AdminEmail(Guid companyId)
    {
        await using var conn = new Npgsql.NpgsqlConnection(fixture.AdminConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand("SELECT email FROM foundation.app_user WHERE company_id=@c ORDER BY email LIMIT 1", conn);
        cmd.Parameters.AddWithValue("c", companyId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }
}
