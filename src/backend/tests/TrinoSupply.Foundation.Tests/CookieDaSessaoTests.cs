using Microsoft.AspNetCore.Http;
using TrinoSupply.Foundation.Api.Rotas;

namespace TrinoSupply.Foundation.Tests;

/// <summary>O cookie do refresh token (auditoria A5): atributos, duração e a tranca anti-CSRF.</summary>
public class CookieDaSessaoTests
{
    private static DefaultHttpContext Contexto(bool https = false, string? cookies = null, string? xrw = null)
    {
        var ctx = new DefaultHttpContext();
        if (https) ctx.Request.Scheme = "https";
        if (cookies is not null) ctx.Request.Headers.Cookie = cookies;
        if (xrw is not null) ctx.Request.Headers[CookieDaSessao.CabecalhoDoApp] = xrw;
        return ctx;
    }

    private static string[] SetCookie(HttpContext ctx) => ctx.Response.Headers.SetCookie.ToArray()!;

    [Fact]
    public void O_cookie_e_httponly_strict_e_so_viaja_para_a_autenticacao()
    {
        var ctx = Contexto(https: true);
        CookieDaSessao.Gravar(ctx, "token-r1", lembrar: false, dias: 7);

        var refresh = SetCookie(ctx).Single(c => c.StartsWith("ts_refresh="));
        Assert.Contains("httponly", refresh, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", refresh, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", refresh, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", refresh, StringComparison.OrdinalIgnoreCase);
        // sem "manter conectado" é cookie de sessão: sem validade, some quando o navegador fecha
        Assert.DoesNotContain("expires=", refresh, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Em_http_o_cookie_sai_sem_secure_senao_o_navegador_o_recusaria_no_desenvolvimento()
    {
        var ctx = Contexto(https: false);
        CookieDaSessao.Gravar(ctx, "token-r1", lembrar: true, dias: 7);
        var refresh = SetCookie(ctx).Single(c => c.StartsWith("ts_refresh="));
        Assert.DoesNotContain("secure", refresh, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", refresh, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manter_conectado_da_validade_e_a_renovacao_mantem_a_escolha()
    {
        var entrada = Contexto(https: true);
        CookieDaSessao.Gravar(entrada, "r1", lembrar: true, dias: 7);
        var cookies = SetCookie(entrada);
        Assert.Contains("expires=", cookies.Single(c => c.StartsWith("ts_refresh=")), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(cookies, c => c.StartsWith("ts_lembrar=1"));

        // a renovação não sabe da escolha: lê o cookie da escolha e mantém a validade
        var renovacao = Contexto(https: true, cookies: "ts_refresh=r1; ts_lembrar=1");
        CookieDaSessao.Gravar(renovacao, "r2", lembrar: null, dias: 7);
        Assert.Contains("expires=", SetCookie(renovacao).Single(c => c.StartsWith("ts_refresh=")), StringComparison.OrdinalIgnoreCase);

        // sem a escolha, a renovação grava cookie de sessão — e apaga a marca da escolha
        var naAba = Contexto(https: true, cookies: "ts_refresh=r1");
        CookieDaSessao.Gravar(naAba, "r2", lembrar: null, dias: 7);
        Assert.DoesNotContain("expires=", SetCookie(naAba).Single(c => c.StartsWith("ts_refresh=")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ler_e_a_tranca_do_cabecalho()
    {
        Assert.Equal("r1", CookieDaSessao.Ler(Contexto(cookies: "ts_refresh=r1")));
        Assert.Null(CookieDaSessao.Ler(Contexto()));
        Assert.True(CookieDaSessao.PedidoDoProprioApp(Contexto(xrw: "TrinoSupply")));
        Assert.False(CookieDaSessao.PedidoDoProprioApp(Contexto()));
        Assert.False(CookieDaSessao.PedidoDoProprioApp(Contexto(xrw: "XMLHttpRequest")));
    }

    [Fact]
    public void Apagar_expira_os_dois_cookies_no_mesmo_caminho()
    {
        var ctx = Contexto(https: true);
        CookieDaSessao.Apagar(ctx);
        var cookies = SetCookie(ctx);
        Assert.Equal(2, cookies.Length);
        Assert.All(cookies, c =>
        {
            Assert.Contains("path=/api/v1/auth", c, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("expires=", c, StringComparison.OrdinalIgnoreCase);
        });
    }
}
