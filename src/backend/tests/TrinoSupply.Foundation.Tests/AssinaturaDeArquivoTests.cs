using System.Text;
using TrinoSupply.Foundation.Api.Domain;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// A conferência de assinatura do upload (DOC-ERR-004). O <c>Content-Type</c> é escrito
/// por quem envia — a lista de tipos aceitos, sozinha, só barra quem é honesto.
/// </summary>
public class AssinaturaDeArquivoTests
{
    private static byte[] Pdf() => Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n");
    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1];
    private static byte[] Xlsx() => [0x50, 0x4B, 0x03, 0x04, 0x14, 0, 0, 0, 8, 0, 0, 0];
    private static byte[] Xls() => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0];
    private static byte[] Webp() => [.. "RIFF"u8.ToArray(), 0x24, 0, 0, 0, .. "WEBP"u8.ToArray()];

    [Fact]
    public void Arquivo_de_verdade_passa_em_cada_formato_aceito()
    {
        Assert.True(AssinaturaDeArquivo.Confere("application/pdf", Pdf()));
        Assert.True(AssinaturaDeArquivo.Confere("image/png", Png()));
        Assert.True(AssinaturaDeArquivo.Confere("image/jpeg", Jpeg()));
        Assert.True(AssinaturaDeArquivo.Confere("image/webp", Webp()));
        Assert.True(AssinaturaDeArquivo.Confere(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Xlsx()));
        Assert.True(AssinaturaDeArquivo.Confere(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", Xlsx()));
        Assert.True(AssinaturaDeArquivo.Confere("application/vnd.ms-excel", Xls()));
        // .xlsx renomeado para .xls é o engano comum do escritório, e não é ataque
        Assert.True(AssinaturaDeArquivo.Confere("application/vnd.ms-excel", Xlsx()));
        Assert.True(AssinaturaDeArquivo.Confere("text/csv", "codigo;descricao\n1;Luva\n"u8.ToArray()));
    }

    /// <summary>
    /// O caso que a lista de tipos não pegava: conteúdo executável ou marcação, com o
    /// cabeçalho declarando um formato inofensivo.
    /// </summary>
    [Fact]
    public void Arquivo_disfarcado_nao_passa()
    {
        var html = "<script>fetch('/api/v1/users')</script>"u8.ToArray();
        var exe = new byte[] { 0x4D, 0x5A, 0x90, 0, 3, 0, 0, 0, 4, 0, 0, 0 };   // MZ

        Assert.False(AssinaturaDeArquivo.Confere("application/pdf", html));
        Assert.False(AssinaturaDeArquivo.Confere("image/png", html));
        Assert.False(AssinaturaDeArquivo.Confere("application/pdf", exe));
        // CSV não tem começo obrigatório, mas tem começo proibido: marcação e binário
        Assert.False(AssinaturaDeArquivo.Confere("text/csv", html));
        Assert.False(AssinaturaDeArquivo.Confere("text/csv", Png()));
        Assert.False(AssinaturaDeArquivo.Confere("text/csv", "<svg onload=alert(1)>"u8.ToArray()));
        // e uma imagem legítima declarada como outra imagem também não passa
        Assert.False(AssinaturaDeArquivo.Confere("image/png", Jpeg()));
    }

    [Fact]
    public void Tipo_fora_da_tabela_e_arquivo_vazio_sao_recusados()
    {
        // não se aceita o que não se sabe conferir
        Assert.False(AssinaturaDeArquivo.Confere("text/html", "<h1>oi</h1>"u8.ToArray()));
        Assert.False(AssinaturaDeArquivo.Confere("image/svg+xml", "<svg/>"u8.ToArray()));
        Assert.False(AssinaturaDeArquivo.Confere("application/octet-stream", Pdf()));
        Assert.False(AssinaturaDeArquivo.Confere(null, Pdf()));
        Assert.False(AssinaturaDeArquivo.Confere("application/pdf", []));
        // arquivo menor que a assinatura não "passa por engano"
        Assert.False(AssinaturaDeArquivo.Confere("image/png", [0x89, 0x50]));
    }

    /// <summary>Todo tipo aceito no upload precisa ser conferível — senão a lista mente.</summary>
    [Fact]
    public void Todo_formato_da_lista_de_aceitos_tem_conferencia()
    {
        var exemplos = new Dictionary<string, byte[]>
        {
            ["application/pdf"] = Pdf(),
            ["image/png"] = Png(),
            ["image/jpeg"] = Jpeg(),
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = Xlsx(),
            ["application/vnd.ms-excel"] = Xls(),
            ["text/csv"] = "a;b\n"u8.ToArray(),
            ["application/csv"] = "a;b\n"u8.ToArray(),
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = Xlsx(),
        };

        foreach (var tipo in StoredDocument.AllowedContentTypes)
        {
            Assert.True(exemplos.ContainsKey(tipo), $"{tipo} está na lista de aceitos e não tem exemplo conferível.");
            Assert.True(AssinaturaDeArquivo.Confere(tipo, exemplos[tipo]), $"{tipo} não passou na própria assinatura.");
        }
    }
}
