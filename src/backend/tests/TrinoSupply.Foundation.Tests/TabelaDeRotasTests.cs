using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Rotas;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// SEC-A, agora sobre a tabela de rotas de verdade.
///
/// Depois do ARQ-A cada módulo expõe o seu <c>Map…</c>, então dá para montar a
/// tabela chamando todos eles num app vazio — sem migration, sem banco, sem
/// subir servidor. O que se confere aqui é o que o ASP.NET registrou, e não o
/// que o texto do arquivo sugere: o caminho de cada rota, o método HTTP e a
/// autorização exigida saem dos metadados do endpoint.
///
/// A conferência de texto que sobrou em <see cref="RotasProtegidasTests"/> é a
/// dos filtros de grupo (<c>RejectSupplierRole</c>, <c>RequireModules</c>), que
/// o ASP.NET embrulha no delegate e não publica como metadado.
/// </summary>
public class TabelaDeRotasTests
{
    /// <summary>
    /// As rotas públicas, por decisão e não por esquecimento — cada uma com o
    /// motivo. Rota nova nesta lista é um ato deliberado: sem entrar aqui, o
    /// teste falha.
    /// </summary>
    private static readonly string[] Publicas =
    [
        "/api/v1/auth/login",     // a porta de entrada do time interno
        "/api/v1/auth/refresh",   // renova a sessão com o refresh token, que é a credencial
        "/api/v1/auth/logout",    // revoga o refresh token de quem o apresenta: só encerra a
                                  // própria sessão, e exigir token válido impediria sair com
                                  // o acesso já expirado
        "/api/v1/portal/login",   // a porta do Portal do Fornecedor, por CNPJ e chave
    ];

    private static List<RouteEndpoint> Rotas()
    {
        // os mesmos serviços do app: o binder de minimal API distingue serviço de
        // corpo de requisição olhando o container, então uma lista diferente aqui
        // daria uma tabela diferente da real. Nada é instanciado — as rotas são
        // descritas, nunca executadas, e por isso não há banco nem servidor.
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddServicosDeDominio();
        builder.Services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("tabela-de-rotas"));
        builder.Services.AddAuthorization();
        var app = builder.Build();
        app.MapAutenticacao(seedOk: true);
        app.MapUsuarios();
        app.MapCatalogo();
        app.MapSolicitacoes();
        app.MapAvisos();
        app.MapComunicados();
        app.MapTorre();
        app.MapEstoque();
        app.MapFornecedores();
        app.MapPedidos();
        app.MapCadastros();
        app.MapAnalytics();
        app.MapCotacoes();
        app.MapDocumentos();
        // os dois últimos entraram atrasados, e enquanto estiveram fora as suas rotas
        // escapavam de toda conferência daqui — inclusive a de exigir autenticação
        app.MapPagamentos();
        app.MapPesosDoScore();
        app.MapPrazosDasEtapas();

        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(d => d.Endpoints).OfType<RouteEndpoint>().ToList();
    }

    private static string Assinatura(RouteEndpoint e)
    {
        var verbo = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? "?";
        return $"{verbo} {e.RoutePattern.RawText}";
    }

    [Fact]
    public void Toda_rota_da_api_exige_autenticacao_menos_as_portas_de_entrada()
    {
        // o que vale é o metadado que o ASP.NET registrou, não o texto do arquivo
        var abertas = Rotas()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .Where(e => e.Metadata.GetMetadata<IAuthorizeData>() is null)
            .Select(e => e.RoutePattern.RawText!)
            .Distinct()
            .Except(Publicas)
            .ToList();

        Assert.True(abertas.Count == 0, "Rota /api sem autorização: " + string.Join(", ", abertas));
    }

    [Fact]
    public void As_unicas_rotas_publicas_sao_as_portas_de_entrada()
    {
        // trava a lista: abrir uma rota nova passa a ser um ato deliberado
        var publicas = Rotas()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .Where(e => e.Metadata.GetMetadata<IAuthorizeData>() is null)
            .Select(e => e.RoutePattern.RawText!)
            .Distinct().OrderBy(r => r, StringComparer.Ordinal).ToList();

        Assert.Equal(Publicas.OrderBy(r => r, StringComparer.Ordinal), publicas);
    }

    [Fact]
    public void A_gestao_de_usuarios_so_abre_para_o_administrador()
    {
        // é o único grupo que se protege por papel, e não por módulo.
        // `/users/pickers` não é dele: é o seletor de responsável das telas de
        // cadastro — autenticado, mas aberto a quem mantém centro de custo e
        // triagem, que precisam escolher a pessoa sem administrar usuários.
        var usuarios = Rotas()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/users", StringComparison.Ordinal) == true)
            .Where(e => e.RoutePattern.RawText != "/api/v1/users/pickers")
            .ToList();
        Assert.NotEmpty(usuarios);
        Assert.All(usuarios, e =>
        {
            var politica = e.Metadata.GetMetadata<AuthorizationPolicy>();
            Assert.NotNull(politica);
            Assert.Contains(politica!.Requirements.OfType<RolesAuthorizationRequirement>(),
                r => r.AllowedRoles.Contains("SystemAdministrator"));
        });
    }

    [Fact]
    public void O_inventario_bate_com_a_tabela_registrada()
    {
        // a mesma lista versionada que guiou o recorte do Program.cs, agora
        // conferida contra o que o ASP.NET realmente registrou
        var esperadas = File.ReadAllLines(Path.Combine("fixtures", "rotas-da-api.txt"))
            .Where(l => !string.IsNullOrWhiteSpace(l)).ToHashSet(StringComparer.Ordinal);
        var registradas = Rotas()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .Select(Assinatura).ToHashSet(StringComparer.Ordinal);

        var sumiram = esperadas.Except(registradas).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var novas = registradas.Except(esperadas).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.True(sumiram.Count == 0, "Rota do inventário que não está registrada: " + string.Join(", ", sumiram));
        Assert.True(novas.Count == 0,
            "Rota registrada fora do inventário — atualize fixtures/rotas-da-api.txt: " + string.Join(", ", novas));
    }
}
