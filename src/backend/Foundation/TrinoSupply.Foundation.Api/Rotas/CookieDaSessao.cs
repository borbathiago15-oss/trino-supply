namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// O refresh token mora num cookie <c>HttpOnly</c> (auditoria A5). No <c>sessionStorage</c>
/// ele ficava ao alcance de qualquer script da página: um XSS — hoje barrado pela CSP, que era
/// a única barreira — levava uma sessão de sete dias. No cookie HttpOnly o JavaScript não o lê,
/// e o que um XSS alcança passa a ser o access token de quinze minutos.
///
/// <para>
/// Três atributos seguram o cookie: <b>HttpOnly</b> (o script não lê), <b>SameSite=Strict</b>
/// (outro site não o manda) e <b>Path=/api/v1/auth</b> (ele só viaja para a renovação e a
/// saída — a API inteira continua sendo Bearer). <b>Secure</b> acompanha o esquema da
/// requisição: em produção o proxy diz https e o cookie só anda por TLS; no desenvolvimento e
/// no E2E, em http, o navegador o recusaria com a marca.
/// </para>
///
/// <para>
/// "Manter conectado neste aparelho" é a <b>duração</b> do cookie: persistente pelos dias do
/// refresh token, ou de sessão (some quando o navegador fecha). A escolha fica num segundo
/// cookie, para a renovação manter o lugar escolhido na entrada. E o pedido que traz o cookie
/// precisa do cabeçalho <c>X-Requested-With</c>, que só o nosso JavaScript manda: um formulário
/// de outro site não consegue acrescentá-lo — é a segunda tranca contra CSRF, além do SameSite.
/// </para>
/// </summary>
public static class CookieDaSessao
{
    public const string Nome = "ts_refresh";
    public const string Lembrar = "ts_lembrar";
    public const string Caminho = "/api/v1/auth";
    public const string CabecalhoDoApp = "X-Requested-With";
    public const string ValorDoApp = "TrinoSupply";

    public static string? Ler(HttpContext ctx) =>
        ctx.Request.Cookies.TryGetValue(Nome, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    /// <summary>O pedido veio do nosso JavaScript, e não de um formulário de outro site.</summary>
    public static bool PedidoDoProprioApp(HttpContext ctx) =>
        string.Equals(ctx.Request.Headers[CabecalhoDoApp].ToString(), ValorDoApp, StringComparison.Ordinal);

    /// <summary>
    /// Grava o refresh token. <paramref name="lembrar"/> nulo mantém a escolha da entrada (a
    /// renovação não a conhece); dado, decide — é a entrada.
    /// </summary>
    public static void Gravar(HttpContext ctx, string refreshToken, bool? lembrar, int dias)
    {
        var persistente = lembrar ?? ctx.Request.Cookies.ContainsKey(Lembrar);
        var validade = persistente ? DateTimeOffset.UtcNow.AddDays(dias) : (DateTimeOffset?)null;
        ctx.Response.Cookies.Append(Nome, refreshToken, Opcoes(ctx, validade));
        if (persistente) ctx.Response.Cookies.Append(Lembrar, "1", Opcoes(ctx, validade));
        else ctx.Response.Cookies.Delete(Lembrar, Opcoes(ctx, null));
    }

    public static void Apagar(HttpContext ctx)
    {
        ctx.Response.Cookies.Delete(Nome, Opcoes(ctx, null));
        ctx.Response.Cookies.Delete(Lembrar, Opcoes(ctx, null));
    }

    private static CookieOptions Opcoes(HttpContext ctx, DateTimeOffset? validade) => new()
    {
        HttpOnly = true,
        Secure = ctx.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = Caminho,
        IsEssential = true,
        Expires = validade,
    };
}
