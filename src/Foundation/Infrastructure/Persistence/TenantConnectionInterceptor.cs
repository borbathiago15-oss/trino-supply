using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Foundation.Infrastructure.Persistence;

/// <summary>
/// Enforcement real do RLS multi-tenant (SEC-004, ADR-015 §3, ARC-006 §12.6).
/// A cada abertura de conexão, define o GUC <c>app.current_company</c> com o tenant corrente
/// (<see cref="ITenantContext"/>). As policies RLS (deploy/db/rls.sql) restringem toda linha a
/// <c>company_id = foundation.current_company()</c>.
///
/// <para><b>Fail-closed:</b> sem tenant resolvido, o GUC é definido como string vazia →
/// <c>foundation.current_company()</c> retorna NULL → nenhuma linha de negócio é visível/gravável.
/// Nunca deixamos o GUC herdado de uma conexão anterior do pool (evita vazamento cross-tenant).</para>
///
/// <para>Usamos <c>set_config(..., is_local := false)</c> (nível de sessão): o Npgsql redefine
/// o estado da sessão ao devolver a conexão ao pool, e reaplicamos aqui a cada abertura lógica.</para>
/// </summary>
public sealed class TenantConnectionInterceptor(ITenantContext tenant) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyTenantAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        // Caminho síncrono (EF pode abrir conexões de forma síncrona em alguns cenários).
        ApplyTenantAsync(connection, CancellationToken.None).GetAwaiter().GetResult();
    }

    private async Task ApplyTenantAsync(DbConnection connection, CancellationToken ct)
    {
        // String vazia quando não há tenant → RLS bloqueia tudo (fail-closed). Nunca interpolar o
        // valor: passa-se por parâmetro para blindar contra injeção via claim.
        var companyId = tenant.HasTenant ? tenant.CompanyId.Value.ToString() : string.Empty;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.current_company', @company, false)";

        var p = cmd.CreateParameter();
        p.ParameterName = "company";
        p.Value = companyId;
        cmd.Parameters.Add(p);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
