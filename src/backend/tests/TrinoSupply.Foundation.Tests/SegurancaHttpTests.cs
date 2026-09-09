using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// Os cabeçalhos de segurança e o HTTPS atrás do proxy, sobre um servidor de verdade.
///
/// O pipeline montado aqui é o mesmo do <c>Program.cs</c> na parte que importa — as duas
/// chamadas, na mesma ordem — porque cabeçalho que só existe no código e não chega na
/// resposta não protege ninguém.
/// </summary>
public class SegurancaHttpTests
{
    private static async Task<HttpClient> ServidorAsync(bool producao = true)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .Configure(app =>
                {
                    app.UsarHttpsAtrasDoProxy(producao);
                    app.UsarCabecalhosDeSeguranca();
                    app.Run(ctx => ctx.Response.WriteAsync("ok"));
                }))
            .StartAsync();
        var cliente = host.GetTestClient();
        cliente.DefaultRequestVersion = new Version(1, 1);
        return cliente;
    }

    private static string? Cabecalho(HttpResponseMessage r, string nome) =>
        r.Headers.TryGetValues(nome, out var v) ? string.Join(", ", v) : null;

    [Fact]
    public async Task Toda_resposta_sai_com_os_cabecalhos_de_seguranca()
    {
        var cliente = await ServidorAsync();
        var r = await cliente.GetAsync("/pedidos");

        var csp = Cabecalho(r, "Content-Security-Policy");
        Assert.NotNull(csp);
        // sem 'unsafe-inline' em script: é o que faz o XSS injetado não executar
        Assert.Contains("script-src 'self'", csp);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp);
        // clickjacking fechado dos dois jeitos, o novo e o antigo
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Equal("DENY", Cabecalho(r, "X-Frame-Options"));
        // o par do upload: sem sniffing, HTML gravado como imagem não vira página
        Assert.Equal("nosniff", Cabecalho(r, "X-Content-Type-Options"));
        Assert.Equal("strict-origin-when-cross-origin", Cabecalho(r, "Referrer-Policy"));
        Assert.Contains("camera=()", Cabecalho(r, "Permissions-Policy"));
    }

    [Fact]
    public async Task Resposta_de_api_nao_fica_em_cache()
    {
        var cliente = await ServidorAsync();

        var api = await cliente.GetAsync("/api/v1/purchase-requisitions");
        Assert.Equal("no-store", api.Headers.CacheControl?.ToString());

        // o SPA continua cacheável: a regra é para dado, não para arquivo estático
        var tela = await cliente.GetAsync("/pedidos");
        Assert.NotEqual("no-store", tela.Headers.CacheControl?.ToString());
    }

    /// <summary>
    /// O Railway termina o TLS na borda: o contêiner recebe HTTP e o esquema verdadeiro
    /// vem no <c>X-Forwarded-Proto</c>. Redirecionar pelo esquema local daria laço infinito.
    /// </summary>
    [Fact]
    public async Task Proxy_dizendo_http_redireciona_para_https()
    {
        var cliente = await ServidorAsync();
        var pedido = new HttpRequestMessage(HttpMethod.Get, "http://trino.exemplo/pedidos?a=1");
        pedido.Headers.Add("X-Forwarded-Proto", "http");

        var r = await cliente.SendAsync(pedido);

        Assert.Equal(System.Net.HttpStatusCode.MovedPermanently, r.StatusCode);
        Assert.Equal("https://trino.exemplo/pedidos?a=1", r.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Sem_o_cabecalho_do_proxy_nao_ha_redirecionamento()
    {
        // é o caso do desenvolvimento local e do healthcheck interno: redirecionar aqui
        // prenderia o app num laço, porque o contêiner nunca vê "https" no esquema local
        var cliente = await ServidorAsync();
        var r = await cliente.GetAsync("/health");

        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        Assert.Null(Cabecalho(r, "Strict-Transport-Security"));
    }

    [Fact]
    public async Task Hsts_so_em_producao_e_so_quando_a_conexao_veio_por_https()
    {
        var producao = await ServidorAsync(producao: true);
        var comHttps = new HttpRequestMessage(HttpMethod.Get, "http://trino.exemplo/pedidos");
        comHttps.Headers.Add("X-Forwarded-Proto", "https");
        var r = await producao.SendAsync(comHttps);
        Assert.Contains("max-age=31536000", Cabecalho(r, "Strict-Transport-Security"));

        // fora de produção o cabeçalho não sai: ele prenderia o localhost do
        // desenvolvedor em HTTPS por um ano, no navegador dele
        var dev = await ServidorAsync(producao: false);
        var outro = new HttpRequestMessage(HttpMethod.Get, "http://trino.exemplo/pedidos");
        outro.Headers.Add("X-Forwarded-Proto", "https");
        Assert.Null(Cabecalho(await dev.SendAsync(outro), "Strict-Transport-Security"));
    }
}
