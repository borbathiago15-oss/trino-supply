using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// Numeração sequencial oficial (PR-001-11): PR-{ano}-{sequencial de 6 dígitos},
/// gerada pela sequence PostgreSQL procurement.pr_number_seq (sem lacunas por concorrência).
/// </summary>
public class PostgresPrNumberGenerator(AppDbContext db, TimeProvider clock) : IPrNumberGenerator
{
    public async Task<string> NextAsync(CancellationToken ct = default)
    {
        var next = await db.Database
            .SqlQueryRaw<long>("SELECT nextval('procurement.pr_number_seq') AS \"Value\"")
            .SingleAsync(ct);
        return $"PR-{clock.GetUtcNow().Year}-{next:000000}";
    }
}
