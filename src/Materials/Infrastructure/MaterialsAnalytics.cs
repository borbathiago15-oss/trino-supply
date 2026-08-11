using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.Materials.Application;
using TrinoSupply.Materials.Domain;
using TrinoSupply.Materials.Infrastructure.Persistence;

namespace TrinoSupply.Materials.Infrastructure;

/// <summary>Leituras agregadas do Almox (v3). RLS já limita ao tenant; agregação no banco.</summary>
public sealed class MaterialsAnalytics(MaterialsDbContext db, IClock clock) : IMaterialsAnalytics
{
    public async Task<IReadOnlyList<CenterConsumptionView>> ConsumptionByCenterAsync(
        int days = 90, CancellationToken ct = default)
    {
        var cutoff = clock.UtcNow.AddDays(-Math.Clamp(days, 1, 365));

        var grouped = await db.StockMovements.AsNoTracking()
            .Where(mv => mv.Direction == StockDirection.Out && mv.OccurredAt >= cutoff && mv.CostCenterCode != null)
            .GroupBy(mv => new { mv.CostCenterCode, mv.ItemId })
            .Select(g => new
            {
                g.Key.CostCenterCode,
                g.Key.ItemId,
                Total = g.Sum(mv => mv.Quantity),
                Movements = g.Count(),
            })
            .ToListAsync(ct);

        var itemIds = grouped.Select(g => g.ItemId).Distinct().ToList();
        var items = (await db.Items.AsNoTracking()
                .Where(i => itemIds.Contains(i.Id)).ToListAsync(ct))
            .ToDictionary(i => i.Id);

        return grouped
            .OrderBy(g => g.CostCenterCode).ThenByDescending(g => g.Total)
            .Select(g => new CenterConsumptionView(
                g.CostCenterCode!,
                items.TryGetValue(g.ItemId, out var it) ? it.Code : "?",
                items.TryGetValue(g.ItemId, out var it2) ? it2.Name : "?",
                g.Total, g.Movements))
            .ToList();
    }

    public async Task<IReadOnlyList<CollaboratorConsumptionView>> ConsumptionByCollaboratorAsync(
        int days = 90, CancellationToken ct = default)
    {
        var cutoff = clock.UtcNow.AddDays(-Math.Clamp(days, 1, 365));

        var consumptions = await db.Consumptions.AsNoTracking().Include(cs => cs.Lines)
            .Where(cs => cs.IssuedAt >= cutoff)
            .ToListAsync(ct);
        var collaborators = (await db.Collaborators.AsNoTracking().ToListAsync(ct))
            .ToDictionary(cl => cl.Id);

        return consumptions
            .GroupBy(cs => cs.CollaboratorId)
            .Select(g =>
            {
                collaborators.TryGetValue(g.Key, out var col);
                return new CollaboratorConsumptionView(
                    col?.Name ?? "?", col?.Registration,
                    g.Select(cs => cs.CostCenterCode).FirstOrDefault() ?? string.Empty,
                    g.Count(),
                    g.SelectMany(cs => cs.Lines).Sum(l => l.Quantity));
            })
            .OrderByDescending(v => v.TotalItems)
            .ToList();
    }
}
