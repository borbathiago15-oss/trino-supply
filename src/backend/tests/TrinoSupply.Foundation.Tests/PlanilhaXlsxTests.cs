using System.IO.Compression;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O gerador de planilha mínimo. A prova é a volta inteira: o que ele escreve, o leitor da
/// importação lê de volta — texto, número, data e vazio nos lugares certos.
/// </summary>
public class PlanilhaXlsxTests
{
    [Fact]
    public void O_que_a_planilha_escreve_o_leitor_da_importacao_le_de_volta()
    {
        var bytes = PlanilhaXlsx.Gerar([
            new PlanilhaXlsx.Aba("Solicitações", ["Número", "Qtd", "Valor", "Criada em", "Obs"],
                [["MR-1", 10m, 12.5m, new DateOnly(2026, 10, 7), "luva & bota <P>"],
                 ["MR-2", 3, null, new DateTimeOffset(2026, 10, 7, 13, 45, 0, TimeSpan.Zero), ""]]),
            new PlanilhaXlsx.Aba("Itens", ["Código"], [["EPI-001"]]),
        ]);

        var linhas = SpreadsheetReader.Read(new MemoryStream(bytes), "x.xlsx");
        Assert.Equal(["Número", "Qtd", "Valor", "Criada em", "Obs"], linhas[0]);
        Assert.Equal(["MR-1", "10", "12.5", "07/10/2026", "luva & bota <P>"], linhas[1]);
        // o nulo é célula vazia, e a data com hora leva a hora
        Assert.Equal("MR-2", linhas[2][0]);
        Assert.Equal("3", linhas[2][1]);
        Assert.Equal("", linhas[2][2]);
        Assert.Equal("07/10/2026 13:45", linhas[2][3]);

        // a segunda aba existe no pacote, com o nome dado
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry("xl/worksheets/sheet2.xml"));
        using var wb = new StreamReader(zip.GetEntry("xl/workbook.xml")!.Open());
        var workbook = wb.ReadToEnd();
        Assert.Contains("name=\"Solicitações\"", workbook);
        Assert.Contains("name=\"Itens\"", workbook);
    }

    [Fact]
    public void A_referencia_da_coluna_passa_do_Z_para_AA_sem_pular()
    {
        Assert.Equal("A", PlanilhaXlsx.Coluna(0));
        Assert.Equal("Z", PlanilhaXlsx.Coluna(25));
        Assert.Equal("AA", PlanilhaXlsx.Coluna(26));
        Assert.Equal("AZ", PlanilhaXlsx.Coluna(51));
        Assert.Equal("BA", PlanilhaXlsx.Coluna(52));
    }

    [Fact]
    public void Nome_de_aba_longo_ou_com_caractere_proibido_e_corrigido_em_vez_de_recusado_pelo_Excel()
    {
        var bytes = PlanilhaXlsx.Gerar([
            new PlanilhaXlsx.Aba("Solicitações de material: tudo o que foi pedido [2026]", ["A"], [["x"]]),
        ]);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var wb = new StreamReader(zip.GetEntry("xl/workbook.xml")!.Open());
        var workbook = wb.ReadToEnd();
        Assert.Contains("name=\"Solicitações de material tudo o\"", workbook);
    }
}
