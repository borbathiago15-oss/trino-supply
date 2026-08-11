using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// Sobe um PostgreSQL real (Testcontainers), aplica o bootstrap (migrations idempotentes do EF +
/// grants — os MESMOS scripts do piloto) e cria o host da API apontando para a role <c>trino_app</c>
/// (não-superuser → RLS efetivo). É o ambiente dos testes de integração de segurança (SEC-004 §8).
/// </summary>
public sealed class PilotFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithUsername("trino")
        .WithPassword("trino")
        .WithDatabase("trino")
        .Build();

    private WebApplicationFactory<Program> _factory = default!;

    /// <summary>Connection string de superuser (para testes que inspecionam infra, ex.: o relay do Outbox).</summary>
    public string AdminConnectionString { get; private set; } = default!;

    /// <summary>Connection string da role da aplicação (trino_app, sem BYPASSRLS) — exercita RLS + set_config.</summary>
    public string AppConnectionString { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        AdminConnectionString = _db.GetConnectionString();

        // Aplica os scripts de bootstrap como superuser (igual ao serviço `bootstrap` do compose).
        var sqlDir = Path.Combine(AppContext.BaseDirectory, "sql");
        await using (var admin = new NpgsqlConnection(_db.GetConnectionString()))
        {
            await admin.OpenAsync();
            foreach (var name in new[] { "foundation", "materials", "procurement", "grants" })
            {
                var sql = await File.ReadAllTextAsync(Path.Combine(sqlDir, $"{name}.sql"));
                await using var cmd = new NpgsqlCommand(sql, admin);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // Connection string da APLICAÇÃO: role trino_app no container.
        var appConn = new NpgsqlConnectionStringBuilder(_db.GetConnectionString())
        {
            Username = "trino_app",
            Password = "apppw",
        }.ConnectionString;
        AppConnectionString = appConn;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Postgres", appConn);
            builder.UseSetting("Jwt:Issuer", "trino-supply");
            builder.UseSetting("Jwt:Audience", "trino-supply");
            builder.UseSetting("Jwt:Keys:0:Kid", "test");
            builder.UseSetting("Jwt:Keys:0:Secret", "chave-de-teste-com-mais-de-32-bytes-abcdefgh!!");
        });
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await _db.DisposeAsync();
    }

    public HttpClient Client() => _factory.CreateClient();

    /// <summary>Fábrica base — testes podem derivá-la (WithWebHostBuilder) p/ configurações específicas.</summary>
    public WebApplicationFactory<Program> Factory => _factory;

    // ---- Helpers HTTP ----

    public static async Task<(int Status, T? Body)> PostAsync<T>(HttpClient c, string path, object body, string? token = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await c.SendAsync(req);
        var payload = res.Content.Headers.ContentLength is > 0 ? await res.Content.ReadFromJsonAsync<T>() : default;
        return ((int)res.StatusCode, payload);
    }

    public static async Task<int> PostStatusAsync(HttpClient c, string path, object? body, string? token = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await c.SendAsync(req);
        return (int)res.StatusCode;
    }

    public static async Task<(int Status, T? Body)> GetAsync<T>(HttpClient c, string path, string? token = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await c.SendAsync(req);
        var payload = res.IsSuccessStatusCode ? await res.Content.ReadFromJsonAsync<T>() : default;
        return ((int)res.StatusCode, payload);
    }
}

[CollectionDefinition("pilot")]
public sealed class PilotCollection : ICollectionFixture<PilotFixture> { }
