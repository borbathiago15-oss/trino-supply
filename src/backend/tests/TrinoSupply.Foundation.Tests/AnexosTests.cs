using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Rotas;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// A porta única do upload (auditoria A2). Cinco rotas liam o formulário por conta própria e
/// gravavam o arquivo só pelo <c>Content-Type</c> declarado — um HTML dizendo ser PNG entrava
/// na SC, na proposta, no documento do fornecedor e na foto do produto. Agora todas passam por
/// <see cref="Anexos.StoreUploadAsync"/>, que confere os primeiros bytes (DOC-ERR-004).
/// </summary>
public class AnexosTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n");
    private static readonly byte[] Html = Encoding.ASCII.GetBytes("<html><script>alert(1)</script></html>");

    private static AppDbContext Banco() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static HttpRequest Formulario(string contentType, byte[] conteudo, string nome = "arquivo")
    {
        var ctx = new DefaultHttpContext();
        var arquivo = new FormFile(new MemoryStream(conteudo), 0, conteudo.Length, "file", nome)
        {
            Headers = new HeaderDictionary(), ContentType = contentType,
        };
        ctx.Request.ContentType = "multipart/form-data; boundary=x";
        ctx.Request.Form = new FormCollection(new Dictionary<string, StringValues>(), new FormFileCollection { arquivo });
        return ctx.Request;
    }

    private static Task<(TrinoSupply.Foundation.Api.Domain.StoredDocument? doc, TrinoSupply.Foundation.Api.Users.UserError? error)>
        Enviar(AppDbContext db, HttpRequest req, IReadOnlyCollection<string>? tipos = null) =>
        Anexos.StoreUploadAsync(req, db, TimeProvider.System, new ClaimsPrincipal(new ClaimsIdentity()),
            "REQUISITION", Guid.NewGuid(), tiposAceitos: tipos);

    [Fact]
    public async Task Html_declarado_como_imagem_e_recusado_pelo_conteudo()
    {
        using var db = Banco();
        var (doc, erro) = await Enviar(db, Formulario("image/png", Html, "foto.png"));
        Assert.Null(doc);
        Assert.Equal("DOC-ERR-004", erro!.Code);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Arquivo_de_verdade_entra_com_o_dono_e_o_rotulo()
    {
        using var db = Banco();
        var fornecedor = Guid.NewGuid();
        var (doc, erro) = await Anexos.StoreUploadAsync(Formulario("application/pdf", Pdf, "proposta.pdf"), db,
            TimeProvider.System, new ClaimsPrincipal(new ClaimsIdentity()), "PROPOSAL", Guid.NewGuid(),
            supplierId: fornecedor, rotuloPadrao: "Fornecedor");
        Assert.Null(erro);
        Assert.Equal(fornecedor, doc!.SupplierId);
        Assert.Equal("Fornecedor", doc.UploadedByLabel);
    }

    [Fact]
    public async Task A_foto_do_produto_aceita_so_imagem()
    {
        using var db = Banco();
        var (_, pdf) = await Enviar(db, Formulario("application/pdf", Pdf), Anexos.SoImagem);
        Assert.Equal("DOC-ERR-003", pdf!.Code);
        var (png, erro) = await Enviar(db, Formulario("image/png", Png), Anexos.SoImagem);
        Assert.Null(erro);
        Assert.NotNull(png);
    }

    /// <summary>
    /// A regressão que importa: rota nova lendo o formulário por conta própria pula a
    /// conferência. Quem lê multipart fora de <c>Anexos.cs</c> precisa estar nesta lista, com
    /// o motivo.
    /// </summary>
    [Fact]
    public void Nenhuma_rota_le_o_arquivo_enviado_por_fora_da_porta_unica()
    {
        var permitidos = new Dictionary<string, string>
        {
            ["Anexos.cs"] = "é a porta única",
            ["ComunicadoRotas.cs"] = "confere a assinatura na própria rota (DOC-ERR-004)",
            ["CatalogoRotas.cs"] = "importação de planilha: lê linhas, não grava o arquivo",
            ["FornecedorRotas.cs"] = "relê os campos do formulário depois de StoreUploadAsync",
        };
        var rotas = Path.Combine(RaizDoRepositorio(), "src", "backend", "Foundation",
            "TrinoSupply.Foundation.Api", "Rotas");
        var quemLe = Directory.EnumerateFiles(rotas, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("ReadFormAsync"))
            .Select(Path.GetFileName)
            .ToList();
        Assert.NotEmpty(quemLe);
        Assert.All(quemLe, f => Assert.True(permitidos.ContainsKey(f!), $"{f} lê o formulário fora de Anexos.StoreUploadAsync."));
    }

    private static string RaizDoRepositorio()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "backend"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }
}
