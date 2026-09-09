using Microsoft.AspNetCore.HttpOverrides;

namespace TrinoSupply.Foundation.Api.Infrastructure;

/// <summary>
/// O que o navegador precisa ouvir para não ser usado contra o próprio usuário:
/// cabeçalhos de segurança e a garantia de que a sessão anda em HTTPS.
/// </summary>
public static class Seguranca
{
    /// <summary>
    /// Política de conteúdo do SPA. Tudo vem da própria origem: o build do Vite não puxa
    /// fonte, script nem folha de estilo de fora — conferido antes de fechar a política,
    /// porque CSP escrita larga "por precaução" não protege de nada.
    ///
    /// <list type="bullet">
    /// <item><c>script-src 'self'</c> sem <c>unsafe-inline</c>: o <c>index.html</c> não tem
    /// script embutido, então nenhum script injetado no DOM executa.</item>
    /// <item><c>style-src</c> aceita inline porque o React escreve <c>style</c> em elemento;
    /// estilo injetado não executa código, é o afrouxamento barato da lista.</item>
    /// <item><c>img-src</c> aceita <c>blob:</c> e <c>data:</c>: a imagem do comunicado e o
    /// anexo chegam como Blob (a rota exige o token da sessão, e <c>&lt;img src&gt;</c> não
    /// manda cabeçalho).</item>
    /// <item><c>frame-ancestors 'none'</c> impede clickjacking — ninguém embute a tela do
    /// aprovador num iframe para roubar o clique de "Aprovar".</item>
    /// <item><c>object-src 'none'</c> e <c>base-uri 'self'</c> fecham dois desvios clássicos:
    /// plugin embutido e sequestro de caminho relativo.</item>
    /// </list>
    /// </summary>
    private const string Csp =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "frame-src 'self' blob:; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    /// <summary>
    /// Cabeçalhos em toda resposta — HTML, JSON, PDF e anexo igualmente.
    ///
    /// <para>
    /// O <c>nosniff</c> é o que fecha o furo do upload junto com a conferência de
    /// assinatura: sem ele o navegador adivinha o tipo pelo conteúdo, e um HTML gravado
    /// como <c>image/png</c> voltaria executando na origem do sistema.
    /// </para>
    /// </summary>
    public static IApplicationBuilder UsarCabecalhosDeSeguranca(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            var h = ctx.Response.Headers;
            h["Content-Security-Policy"] = Csp;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";                       // par antigo do frame-ancestors
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            // nada aqui usa câmera, microfone ou localização: negar é dizer a verdade
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            // resposta de API nunca deve ficar em cache compartilhado
            if (ctx.Request.Path.StartsWithSegments("/api"))
                h.CacheControl = "no-store";
            await next();
        });

    /// <summary>
    /// HTTPS atrás do proxy do Railway.
    ///
    /// <para>
    /// O TLS termina na borda: o contêiner recebe HTTP puro com <c>X-Forwarded-Proto</c>
    /// dizendo qual era o esquema de verdade. Por isso <c>UseHttpsRedirection()</c> sozinho
    /// não serve — ele olharia o esquema local, veria "http" sempre e redirecionaria em
    /// laço infinito.
    /// </para>
    ///
    /// <para>
    /// Aqui o redirecionamento só acontece quando o proxy <b>afirma</b> que a requisição
    /// chegou por HTTP. Sem o cabeçalho — desenvolvimento local, healthcheck interno — não
    /// se redireciona nada, que é o que evita o laço.
    /// </para>
    ///
    /// <para>
    /// O HSTS entra na mesma condição, e só em produção: o navegador ignora o cabeçalho
    /// recebido por HTTP, e mandá-lo em desenvolvimento prenderia <c>localhost</c> em HTTPS
    /// no navegador do desenvolvedor por um ano.
    /// </para>
    /// </summary>
    public static IApplicationBuilder UsarHttpsAtrasDoProxy(this IApplicationBuilder app, bool producao)
    {
        // A decisão vem ANTES do UseForwardedHeaders, e a ordem é o ponto: aquele
        // middleware **consome** o `X-Forwarded-Proto` — aplica ao esquema da requisição
        // e apaga o cabeçalho. Lendo depois dele, o valor já não existe e o
        // redirecionamento nunca dispararia (foi o que o teste pegou).
        app.Use(async (ctx, next) =>
        {
            var declarado = ctx.Request.Headers["X-Forwarded-Proto"].ToString();

            if (string.Equals(declarado, "http", StringComparison.OrdinalIgnoreCase))
            {
                var destino = $"https://{ctx.Request.Host}{ctx.Request.PathBase}{ctx.Request.Path}{ctx.Request.QueryString}";
                ctx.Response.Redirect(destino, permanent: true);
                return;
            }

            var porHttps = string.Equals(declarado, "https", StringComparison.OrdinalIgnoreCase)
                           || ctx.Request.IsHttps;   // TLS direto no contêiner, sem proxy
            if (producao && porHttps)
                ctx.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

            await next();
        });

        // e só então o esquema verdadeiro passa a valer para o resto do app
        return app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor,
            // o proxy é o do Railway, e não um endereço que possamos fixar aqui; a rede é
            // dele e o contêiner só é alcançável por ele
            KnownNetworks = { }, KnownProxies = { },
        });
    }
}
