using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TrinoSupply.Api.Startup;

/// <summary>
/// Compatibilidade com PaaS (Railway/Render/Heroku-like): traduz as convenções da plataforma para a
/// configuração da API, sem tocar no caminho do docker-compose (que continua igual).
/// - <c>DATABASE_URL</c> (postgres://user:pass@host/db) vira a connection string; com
///   <c>APP_DB_PASSWORD</c> definido, a app conecta como <c>trino_app</c> (RLS efetivo — SEC-004).
/// - <c>PORT</c> injetado pela plataforma vira o bind do Kestrel.
/// - <c>MIGRATE_ON_STARTUP=true</c> aplica as migrations dos 3 contextos e provisiona as roles
///   (grants.sql) usando a conexão ADMIN do DATABASE_URL — substitui o serviço `bootstrap`.
/// - Nomes amigáveis (JWT_SIGNING_SECRET, PROVISIONING_KEY, WEB_BASE_URL, SMTP_*) preenchem as
///   chaves de configuração equivalentes quando ainda não definidas.
/// </summary>
public static class CloudEnvironment
{
    public static void ApplyTo(WebApplicationBuilder builder)
    {
        var cfg = builder.Configuration;

        // --- DATABASE_URL → connection strings -----------------------------------------------
        var dbUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (!string.IsNullOrWhiteSpace(dbUrl))
        {
            var admin = FromUrl(dbUrl);
            cfg["Db:AdminConnection"] = admin;

            // DATABASE_URL vence o default do appsettings.json; perde apenas para a env explícita
            // ConnectionStrings__Postgres (quem a define sabe o que está fazendo).
            var explicitConn = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
            if (string.IsNullOrWhiteSpace(explicitConn))
            {
                var appPassword = Environment.GetEnvironmentVariable("APP_DB_PASSWORD");
                if (!string.IsNullOrWhiteSpace(appPassword))
                {
                    // Runtime como trino_app (a role é criada pelo MIGRATE_ON_STARTUP/grants.sql).
                    var b = new NpgsqlConnectionStringBuilder(admin) { Username = "trino_app", Password = appPassword };
                    cfg["ConnectionStrings:Postgres"] = b.ConnectionString;
                }
                else
                {
                    // Sem APP_DB_PASSWORD: conecta com o usuário da plataforma. Em Production o
                    // DatabasePrivilegeGuard recusa superuser — defina APP_DB_PASSWORD (ver deploy/railway.md).
                    cfg["ConnectionStrings:Postgres"] = admin;
                }
            }
        }

        // --- PORT da plataforma → bind do Kestrel --------------------------------------------
        // Onde há IPv6, escutamos em [::] (socket dual-stack: aceita IPv4 mapeado também). Isso é
        // exigido pela rede privada do Railway (*.railway.internal é IPv6-only) e continua servindo
        // o tráfego público. Em hosts sem IPv6 (alguns runners/containers), cai para 0.0.0.0.
        var port = Environment.GetEnvironmentVariable("PORT");
        if (!string.IsNullOrWhiteSpace(port) && int.TryParse(port, out var p))
        {
            var bind = System.Net.Sockets.Socket.OSSupportsIPv6 ? $"http://[::]:{p}" : $"http://0.0.0.0:{p}";
            builder.WebHost.UseUrls(bind);
        }

        // --- Nomes amigáveis → chaves de configuração (só quando ainda não definidas) --------
        Map(cfg, "Jwt:Keys:0:Secret", "JWT_SIGNING_SECRET");
        if (!string.IsNullOrWhiteSpace(cfg["Jwt:Keys:0:Secret"]))
        {
            cfg["Jwt:Keys:0:Kid"] ??= "cloud-1";
            cfg["Jwt:Issuer"] ??= "trino-supply";
            cfg["Jwt:Audience"] ??= "trino-supply";
        }
        Map(cfg, "Provisioning:Key", "PROVISIONING_KEY");
        Map(cfg, "Email:WebBaseUrl", "WEB_BASE_URL");
        Map(cfg, "Email:Smtp:Host", "SMTP_HOST");
        Map(cfg, "Email:Smtp:Port", "SMTP_PORT");
        Map(cfg, "Email:Smtp:User", "SMTP_USER");
        Map(cfg, "Email:Smtp:Password", "SMTP_PASSWORD");
        Map(cfg, "Email:From", "SMTP_FROM");
    }

    /// <summary>Aplica migrations + roles quando MIGRATE_ON_STARTUP=true. Roda antes do host servir.</summary>
    public static async Task MigrateIfRequestedAsync(WebApplication app)
    {
        var flag = Environment.GetEnvironmentVariable("MIGRATE_ON_STARTUP")
                   ?? app.Configuration["Db:MigrateOnStartup"];
        if (!string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase)) return;

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CloudEnvironment");
        var admin = app.Configuration["Db:AdminConnection"]
                    ?? app.Configuration.GetConnectionString("Postgres")
                    ?? throw new InvalidOperationException("MIGRATE_ON_STARTUP exige DATABASE_URL ou ConnectionStrings:Postgres.");

        await WaitForDatabaseAsync(admin, logger);

        // Foundation primeiro: cria foundation.current_company(), referenciada pelas policies dos demais.
        // Contextos construídos à mão (sem DI): sem TenantConnectionInterceptor — migrations são DDL
        // com a conexão ADMIN, como no bootstrap do compose.
        await MigrateContextAsync(Options<TrinoSupply.Foundation.Infrastructure.Persistence.FoundationDbContext>(admin, "foundation",
            o => new TrinoSupply.Foundation.Infrastructure.Persistence.FoundationDbContext(
                o, new TrinoSupply.Foundation.Infrastructure.Multitenancy.NullTenantContext())), "foundation", logger);
        await MigrateContextAsync(Options<TrinoSupply.Materials.Infrastructure.Persistence.MaterialsDbContext>(admin, "materials",
            o => new TrinoSupply.Materials.Infrastructure.Persistence.MaterialsDbContext(o)), "materials", logger);
        await MigrateContextAsync(Options<TrinoSupply.Procurement.Infrastructure.Persistence.ProcurementDbContext>(admin, "procurement",
            o => new TrinoSupply.Procurement.Infrastructure.Persistence.ProcurementDbContext(o)), "procurement", logger);

        await ProvisionRolesAsync(admin, logger);
        logger.LogInformation("MIGRATE_ON_STARTUP concluído: migrations aplicadas e roles provisionadas.");
    }

    private static DbContext Options<TContext>(string conn, string schema, Func<DbContextOptions<TContext>, DbContext> create)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(conn, npg => npg.MigrationsHistoryTable("__ef_migrations", schema))
            .Options;
        return create(options);
    }

    private static async Task MigrateContextAsync(DbContext db, string schema, ILogger logger)
    {
        await using (db)
        {
            await db.Database.MigrateAsync();
        }
        logger.LogInformation("Migrations aplicadas: {Schema}", schema);
    }

    private static async Task ProvisionRolesAsync(string adminConn, ILogger logger)
    {
        var grantsPath = Path.Combine(AppContext.BaseDirectory, "grants.sql");
        if (!File.Exists(grantsPath))
        {
            logger.LogWarning("grants.sql não encontrado em {Path} — roles trino_app/trino_worker NÃO provisionadas.", grantsPath);
            return;
        }

        var appPw = Environment.GetEnvironmentVariable("APP_DB_PASSWORD") ?? "";
        var workerPw = Environment.GetEnvironmentVariable("WORKER_DB_PASSWORD") ?? "";

        await using var conn = new NpgsqlConnection(adminConn);
        await conn.OpenAsync();
        // Senhas via settings de sessão (mesmo contrato do bootstrap do compose) — nunca interpoladas no SQL.
        await using (var set = new NpgsqlCommand("SELECT set_config('trino.app_password', @a, false), set_config('trino.worker_password', @w, false)", conn))
        {
            set.Parameters.AddWithValue("a", appPw);
            set.Parameters.AddWithValue("w", workerPw);
            await set.ExecuteNonQueryAsync();
        }
        await using (var grants = new NpgsqlCommand(await File.ReadAllTextAsync(grantsPath), conn))
        {
            await grants.ExecuteNonQueryAsync();
        }
        logger.LogInformation("Roles provisionadas via grants.sql (trino_app/trino_worker).");
    }

    private static async Task WaitForDatabaseAsync(string conn, ILogger logger)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var c = new NpgsqlConnection(conn);
                await c.OpenAsync();
                return;
            }
            catch (Exception ex) when (attempt < 30)
            {
                logger.LogInformation("Aguardando o banco ({Attempt}/30): {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    /// <summary>Converte postgres://user:pass@host:port/db?opts para o formato keyword do Npgsql.</summary>
    private static string FromUrl(string url)
    {
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        var b = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
        };
        // Preserva sslmode da query string (Railway costuma exigir; default seguro = Prefer).
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var ssl = query.Get("sslmode") ?? query.Get("ssl");
        if (!string.IsNullOrWhiteSpace(ssl) && Enum.TryParse<SslMode>(ssl, ignoreCase: true, out var mode))
            b.SslMode = mode;
        return b.ConnectionString;
    }

    private static void Map(IConfiguration cfg, string key, string envName)
    {
        if (string.IsNullOrWhiteSpace(cfg[key]))
        {
            var value = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(value)) cfg[key] = value;
        }
    }
}
