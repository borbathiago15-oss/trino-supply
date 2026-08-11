using Npgsql;

namespace TrinoSupply.Api.Security;

/// <summary>
/// Guarda de inicialização (SEC-004, fail-fast): em produção, verifica que a aplicação conecta ao
/// Postgres com um papel <b>sem</b> <c>SUPERUSER</c> e <b>sem</b> <c>BYPASSRLS</c>. Um papel privilegiado
/// ignora as policies de Row Level Security e quebraria o isolamento multi-tenant — então, se detectado,
/// <b>abortamos o start</b> (melhor não subir do que subir inseguro). Roda só fora de Development.
/// </summary>
public sealed class DatabasePrivilegeGuard(
    IConfiguration config, IHostEnvironment env, ILogger<DatabasePrivilegeGuard> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        if (env.IsDevelopment())
            return; // em dev aceitamos rodar com o papel do desenvolvedor (sem gate)

        var cs = config.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                "Configuração ausente: ConnectionStrings:Postgres é obrigatória em produção (SEC-004).");

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT current_user, rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("Não foi possível verificar os privilégios do papel de banco (SEC-004).");

        var user = reader.GetString(0);
        var isSuper = reader.GetBoolean(1);
        var bypassRls = reader.GetBoolean(2);

        if (isSuper || bypassRls)
        {
            throw new InvalidOperationException(
                $"Papel de banco inseguro em produção: '{user}' tem " +
                $"{(isSuper ? "SUPERUSER " : "")}{(bypassRls ? "BYPASSRLS" : "")}".TrimEnd() +
                " — ignora o RLS e quebra o isolamento multi-tenant. Conecte com um papel NOSUPERUSER NOBYPASSRLS (ex.: trino_app). Abortando o start (SEC-004).");
        }

        logger.LogInformation("Guard SEC-004 OK: aplicação conecta como '{User}' (sem SUPERUSER/BYPASSRLS).", user);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
