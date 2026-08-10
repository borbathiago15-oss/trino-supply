using System.Text;
using TrinoSupply.Api.Procurement;
using Xunit;

namespace TrinoSupply.Integration.Tests;

/// <summary>Testes do gerador do PDF da OC e do valor por extenso (sem banco/containers).</summary>
public sealed class OcPdfTests
{
    static OcPdfTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    [Theory]
    [InlineData(0, "Zero reais")]
    [InlineData(1, "Um real")]
    [InlineData(1600, "Mil e seiscentos reais")]
    [InlineData(2, "Dois reais")]
    [InlineData(100, "Cem reais")]
    public void Valor_por_extenso_inteiros(double valor, string esperado) =>
        Assert.Equal(esperado, ValorPorExtenso.Converter((decimal)valor));

    [Fact]
    public void Valor_por_extenso_com_centavos()
    {
        Assert.Equal("Um real e cinquenta centavos", ValorPorExtenso.Converter(1.50m));
        Assert.Equal("Zero reais e um centavo", ValorPorExtenso.Converter(0.01m));
    }

    [Fact]
    public void Build_gera_um_PDF_valido()
    {
        var buyer = new OcParty("EP1", "Brilho Terceirizacoes Ltda", "05.345.258/0004-96", "16.411.708-3",
            "R Manoel Cesar de Melo S/N", "Distrito Industrial", "Alhandra", "PB", "58320-000",
            "81 3243-8500", "compras@grupotrino.com.br");
        var supplier = new OcParty("3963", "Rede & Vidros Decoracoes", "17.381.510/0001-59", "",
            "R Joao Cancio 925", "Manaira", "Joao Pessoa", "PB", "58038-341", "", "");
        var lines = new List<OcLine>
        {
            new(10m, "un", "VIDRO-TEMP", "Vidro temperado 8mm", DateTimeOffset.UtcNow, 120m, 1200m, 0m, 0m, 0m, 0m),
            new(5m, "m2", "ESPELHO", "Espelho 4mm", null, 80m, 400m, 0m, 0m, 0m, 0m),
        };
        var data = new OcData(664, DateTimeOffset.UtcNow, "16 - Caio Holanda", buyer, supplier,
            "A Vista", "Deposito Bancario", "C - POR CONTA DO DESTINATARIO", lines,
            1600m, 0m, 0m, 0m, 0m, 1600m);

        var pdf = OcPdf.Build(data);

        // Permite salvar uma amostra do PDF em disco para inspeção visual (opcional).
        var outPath = Environment.GetEnvironmentVariable("OC_PDF_OUT");
        if (!string.IsNullOrWhiteSpace(outPath)) File.WriteAllBytes(outPath, pdf);

        Assert.True(pdf.Length > 1000);
        // Assinatura de arquivo PDF ("%PDF-").
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    }
}
