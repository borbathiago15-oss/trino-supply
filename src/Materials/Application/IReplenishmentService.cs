using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Materials.Application;

public sealed record ReplenishmentPolicyView(Guid ItemId, string ItemCode, decimal MinLevel, decimal MaxLevel, bool Active);

/// <summary>Sugestão de reposição: item abaixo do mínimo e quanto comprar para atingir o máximo (ADR-014).</summary>
public sealed record ReplenishmentSuggestion(
    Guid ItemId, string ItemCode, string ItemName, decimal Balance, decimal MinLevel, decimal MaxLevel, decimal SuggestedQuantity);

/// <summary>Motor de reposição (ADR-014): define políticas e calcula sugestões a partir do saldo.</summary>
public interface IReplenishmentService
{
    Task<Result> SetPolicyAsync(string itemCode, decimal minLevel, decimal maxLevel, CancellationToken ct = default);
    Task<Result<ReplenishmentPolicyView>> GetPolicyAsync(string itemCode, CancellationToken ct = default);

    /// <summary>Itens do tenant cujo saldo está no ponto de reposição, com a quantidade sugerida.</summary>
    Task<IReadOnlyList<ReplenishmentSuggestion>> GetSuggestionsAsync(CancellationToken ct = default);
}
