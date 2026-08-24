using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Users;

/// <summary>Cadastro de centros de custo (dimensões regional/gerente/cliente dos dashboards).</summary>
public class CostCenterService(AppDbContext db, TimeProvider clock)
{
    public static bool CanMaintain(string role) =>
        role is Roles.SupplyManager or Roles.SystemAdministrator;

    public Task<List<CostCenter>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        var q = db.CostCenters.AsQueryable();
        if (!includeInactive) q = q.Where(c => c.Active);
        return q.OrderBy(c => c.Code).Take(1000).ToListAsync(ct);
    }

    public async Task<(CostCenter? cc, UserError? error)> CreateAsync(
        Guid actorId, string code, string name, string? region, string? manager, string? client,
        CancellationToken ct = default)
    {
        code = code.Trim().ToUpperInvariant();
        if (code.Length < 2) return (null, new("CC-ERR-011", "Informe o código do centro de custo."));
        if (name.Trim().Length < 3) return (null, new("CC-ERR-012", "Informe o nome do centro de custo."));
        if (await db.CostCenters.AnyAsync(c => c.Code == code, ct))
            return (null, new("CC-ERR-010", "Já existe um centro de custo com este código."));

        var now = clock.GetUtcNow();
        var cc = new CostCenter
        {
            Code = code,
            Name = name.Trim(),
            Region = Clean(region)?.ToUpperInvariant(),
            ManagerName = Clean(manager),
            ClientName = Clean(client),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = actorId,
        };
        db.CostCenters.Add(cc);
        await db.SaveChangesAsync(ct);
        return (cc, null);
    }

    public async Task<(CostCenter? cc, UserError? error)> UpdateAsync(
        Guid id, string? name, string? region, string? manager, string? client, bool? active,
        CancellationToken ct = default)
    {
        var cc = await db.CostCenters.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (cc is null) return (null, new("CC-ERR-404", "Centro de custo não encontrado."));
        if (name is not null && name.Trim().Length >= 3) cc.Name = name.Trim();
        if (region is not null) cc.Region = Clean(region)?.ToUpperInvariant();
        if (manager is not null) cc.ManagerName = Clean(manager);
        if (client is not null) cc.ClientName = Clean(client);
        if (active is not null) cc.Active = active.Value;
        cc.UpdatedAt = clock.GetUtcNow();
        cc.Version += 1;
        await db.SaveChangesAsync(ct);
        return (cc, null);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
