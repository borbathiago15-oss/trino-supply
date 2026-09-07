using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TrinoSupply.Foundation.Api.Rotas;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// OPS-B — o que o usuário recebe quando algo quebra sem que ninguém tenha previsto.
///
/// Antes disto o 500 saía com corpo vazio: a tela mostrava "o servidor não respondeu"
/// e a pessoa não tinha número nenhum para relatar; do outro lado, quem fosse
/// investigar não conseguia ligar a queixa dela a uma linha do log. O contrato agora
/// é o mesmo do resto da API — `{ error: { code, message, correlationId } }` — e a
/// correlação aparece nos dois lados.
/// </summary>
public class EnvelopeDeFalhaTests
{
    /// <summary>Um servidor mínimo com o handler e uma rota que estoura de propósito.</summary>
    private static async Task<HttpClient> ServidorQueQuebraAsync()
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(s => s.AddRouting());
            web.Configure(app =>
            {
                app.UsarEnvelopeDeFalha();
                app.UseRouting();
                app.UseEndpoints(rotas =>
                {
                    rotas.MapGet("/estoura", void () => throw new InvalidOperationException("boom"));
                    rotas.MapGet("/ok", () => Results.Json(new { data = "ok" }));
                });
            });
        });
        var host = await builder.StartAsync();
        return host.GetTestClient();
    }

    [Fact]
    public async Task Falha_inesperada_responde_no_envelope_da_api_com_a_correlacao()
    {
        var http = await ServidorQueQuebraAsync();

        var res = await http.GetAsync("/estoura");

        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        var corpo = await res.Content.ReadFromJsonAsync<JsonElement>();
        var erro = corpo.GetProperty("error");
        Assert.Equal(TrinoSupply.Foundation.Api.Rotas.Api.CodigoDeFalhaInesperada, erro.GetProperty("code").GetString());

        // o número que a pessoa vai relatar tem de estar na mensagem, e não só num campo
        // que a tela pode não mostrar
        var correlacao = erro.GetProperty("correlationId").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(correlacao));
        Assert.Contains(correlacao, erro.GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_correlacao_enviada_pelo_cliente_e_a_que_volta_na_falha()
    {
        var http = await ServidorQueQuebraAsync();
        http.DefaultRequestHeaders.Add("X-Correlation-Id", "chamado-4711");

        var res = await http.GetAsync("/estoura");

        var corpo = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("chamado-4711", corpo.GetProperty("error").GetProperty("correlationId").GetString());
    }

    /// <summary>O handler só entra em cena na falha: a resposta normal não muda.</summary>
    [Fact]
    public async Task Rota_que_funciona_continua_respondendo_normalmente()
    {
        var http = await ServidorQueQuebraAsync();

        var res = await http.GetAsync("/ok");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var corpo = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", corpo.GetProperty("data").GetString());
    }
}
