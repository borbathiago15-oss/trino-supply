namespace TrinoSupply.Foundation.Api.Infrastructure;

public static class ConnectionStringFactory
{
    /// <summary>
    /// Resolve a connection string do PostgreSQL a partir de:
    /// 1. DATABASE_URL no formato URI (padrão do Railway: postgresql://user:pass@host:port/db);
    /// 2. ConnectionStrings:Default (appsettings/variável ConnectionStrings__Default).
    /// </summary>
    public static string Resolve(IConfiguration configuration)
    {
        var databaseUrl = configuration["DATABASE_URL"];
        if (!string.IsNullOrWhiteSpace(databaseUrl) &&
            Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme is "postgres" or "postgresql"))
        {
            var userInfo = uri.UserInfo.Split(':', 2);
            var user = Uri.UnescapeDataString(userInfo[0]);
            var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
            var database = uri.AbsolutePath.TrimStart('/');
            var port = uri.Port > 0 ? uri.Port : 5432;
            return $"Host={uri.Host};Port={port};Database={database};Username={user};Password={password};Ssl Mode=Prefer";
        }

        var fromConfig = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(fromConfig)) return fromConfig;

        throw new InvalidOperationException(
            "Nenhuma conexão de banco configurada. Defina DATABASE_URL (Railway) ou ConnectionStrings__Default.");
    }
}
