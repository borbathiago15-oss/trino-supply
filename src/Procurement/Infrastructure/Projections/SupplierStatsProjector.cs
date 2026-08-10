using Microsoft.Extensions.Logging;
using Npgsql;

namespace TrinoSupply.Procurement.Infrastructure.Projections;

/// <summary>
/// Aplica os eventos de OC à projeção <c>procurement.supplier_stats</c> de forma <b>idempotente</b>
/// (dedupe por EventId em <c>processed_event</c>) e <b>escopada ao tenant</b> (define
/// <c>app.current_company</c> na transação → RLS vale mesmo para a role da aplicação). ARC-005/SEC-004.
/// </summary>
public sealed class SupplierStatsProjector(string connectionString, ILogger<SupplierStatsProjector> logger)
{
    public Task ApplyOrderIssuedAsync(Guid eventId, Guid companyId, Guid supplierId, decimal netValue, DateTimeOffset occurredAt, CancellationToken ct = default)
        => ApplyAsync(eventId, companyId, supplierId, countDelta: 1, valueDelta: netValue, lastOrderAt: occurredAt, ct);

    public Task ApplyOrderCancelledAsync(Guid eventId, Guid companyId, Guid supplierId, decimal netValue, CancellationToken ct = default)
        => ApplyAsync(eventId, companyId, supplierId, countDelta: -1, valueDelta: -netValue, lastOrderAt: null, ct);

    private async Task ApplyAsync(
        Guid eventId, Guid companyId, Guid supplierId, int countDelta, decimal valueDelta,
        DateTimeOffset? lastOrderAt, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Idempotência: se o evento já foi aplicado, não faz nada (entrega at-least-once).
        await using (var dedup = new NpgsqlCommand(
            "INSERT INTO procurement.processed_event(event_id, processed_at) VALUES(@e, now()) ON CONFLICT DO NOTHING", conn, tx))
        {
            dedup.Parameters.AddWithValue("e", eventId);
            if (await dedup.ExecuteNonQueryAsync(ct) == 0)
            {
                await tx.CommitAsync(ct);
                logger.LogDebug("Evento {EventId} já processado; ignorado.", eventId);
                return;
            }
        }

        // Escopo de tenant para a RLS da projeção (is_local = true → vale só nesta transação).
        await using (var setc = new NpgsqlCommand("SELECT set_config('app.current_company', @c, true)", conn, tx))
        {
            setc.Parameters.AddWithValue("c", companyId.ToString());
            await setc.ExecuteNonQueryAsync(ct);
        }

        const string upsert = @"
INSERT INTO procurement.supplier_stats AS s (company_id, supplier_id, orders_count, total_value, last_order_at)
VALUES(@company, @supplier, GREATEST(@countDelta, 0), @valueDelta, @last)
ON CONFLICT (company_id, supplier_id) DO UPDATE SET
    orders_count  = GREATEST(s.orders_count + @countDelta, 0),
    total_value   = s.total_value + @valueDelta,
    last_order_at = COALESCE(GREATEST(s.last_order_at, @last), s.last_order_at);";
        await using (var cmd = new NpgsqlCommand(upsert, conn, tx))
        {
            cmd.Parameters.AddWithValue("company", companyId);
            cmd.Parameters.AddWithValue("supplier", supplierId);
            cmd.Parameters.AddWithValue("countDelta", countDelta);
            cmd.Parameters.AddWithValue("valueDelta", valueDelta);
            cmd.Parameters.AddWithValue("last", (object?)lastOrderAt ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }
}
