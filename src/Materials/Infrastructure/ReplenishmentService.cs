using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.Materials.Application;
using TrinoSupply.Materials.Domain;
using TrinoSupply.Materials.Infrastructure.Persistence;

namespace TrinoSupply.Materials.Infrastructure;

/// <summary>Motor de reposição (ADR-014): políticas por item + cálculo de sugestões sobre o saldo.</summary>
public sealed class ReplenishmentService(MaterialsDbContext db, ITenantContext tenant, IUsageMetrics metrics)
    : IReplenishmentService
{
    public async Task<Result> SetPolicyAsync(string itemCode, decimal minLevel, decimal maxLevel, CancellationToken ct = default)
    {
        if (!tenant.HasTenant)
            return Result.Failure(new Error("materials.no_tenant", "Requisição sem tenant."));

        var item = await db.Items.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Code == itemCode.Trim().ToUpperInvariant(), ct);
        if (item is null)
            return Result.Failure(new Error("materials.item.not_found", $"Item '{itemCode}' não encontrado."));

        var existing = await db.ReplenishmentPolicies.FirstOrDefaultAsync(p => p.Id == item.Id, ct);
        if (existing is null)
        {
            var created = ReplenishmentPolicy.Define(tenant.CompanyId, item.Id, minLevel, maxLevel);
            if (created.IsFailure) return Result.Failure(created.Error);
            db.ReplenishmentPolicies.Add(created.Value);
        }
        else
        {
            var updated = existing.Update(minLevel, maxLevel);
            if (updated.IsFailure) return updated;
        }

        await db.SaveChangesAsync(ct);
        metrics.Record("materials.replenishment.policy_set", tenant.CompanyId.Value.ToString());
        return Result.Success();
    }

    public async Task<Result<ReplenishmentPolicyView>> GetPolicyAsync(string itemCode, CancellationToken ct = default)
    {
        var item = await db.Items.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Code == itemCode.Trim().ToUpperInvariant(), ct);
        if (item is null)
            return Result.Failure<ReplenishmentPolicyView>(new Error("materials.item.not_found", $"Item '{itemCode}' não encontrado."));

        var policy = await db.ReplenishmentPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == item.Id, ct);
        if (policy is null)
            return Result.Failure<ReplenishmentPolicyView>(new Error("materials.replenishment.not_found", "Item sem política de reposição."));

        return Result.Success(new ReplenishmentPolicyView(
            item.Id.Value, item.Code, policy.MinLevel, policy.MaxLevel, policy.Active));
    }

    public async Task<IReadOnlyList<ReplenishmentSuggestion>> GetSuggestionsAsync(CancellationToken ct = default)
    {
        // Tenant-scoped por RLS. Conjuntos pequenos por tenant → resolve em memória (join por ItemId).
        var policies = await db.ReplenishmentPolicies.AsNoTracking().Where(p => p.Active).ToListAsync(ct);
        if (policies.Count == 0) return [];

        var items = (await db.Items.AsNoTracking().ToListAsync(ct)).ToDictionary(i => i.Id.Value);
        var balances = (await db.StockBalances.AsNoTracking().ToListAsync(ct)).ToDictionary(b => b.Id.Value);

        var suggestions = new List<ReplenishmentSuggestion>();
        foreach (var policy in policies)
        {
            var key = policy.Id.Value;
            var balance = balances.TryGetValue(key, out var b) ? b.Quantity : 0m;
            var need = policy.NeedFor(balance);
            if (need <= 0 || !items.TryGetValue(key, out var item)) continue;

            suggestions.Add(new ReplenishmentSuggestion(
                key, item.Code, item.Name, balance, policy.MinLevel, policy.MaxLevel, need));
        }

        return suggestions.OrderByDescending(s => s.SuggestedQuantity).ToList();
    }
}
