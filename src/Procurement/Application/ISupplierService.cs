using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Procurement.Application;

public sealed record SupplierView(
    Guid Id, string Code, string Name, string TaxId, string StateRegistration, string Address,
    string District, string City, string State, string ZipCode, string Phone, string Email,
    string PaymentTerms, string PaymentMethod, string Status);

/// <summary>Dados cadastrais do fornecedor (inclui campos fiscais usados na OC).</summary>
public sealed record SupplierInput(
    string Code, string Name, string TaxId, string? StateRegistration = null, string? Address = null,
    string? District = null, string? City = null, string? State = null, string? ZipCode = null,
    string? Phone = null, string? Email = null, string? PaymentTerms = null, string? PaymentMethod = null);

/// <summary>Histórico/estatística de compras por fornecedor (projeção alimentada por eventos).</summary>
public sealed record SupplierStatsView(
    Guid SupplierId, string Code, string Name, int OrdersCount, decimal TotalValue, DateTimeOffset? LastOrderAt);

/// <summary>Cadastro de fornecedores (PR-001 / PRC-005). O vencedor da concorrência vira o fornecedor da OC.</summary>
public interface ISupplierService
{
    Task<Result<Guid>> CreateAsync(SupplierInput input, CancellationToken ct = default);
    Task<Result> UpdateAsync(Guid id, SupplierInput input, CancellationToken ct = default);
    Task<Result<SupplierView>> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SupplierView>> ListAsync(CancellationToken ct = default);

    /// <summary>Estatística de compras por fornecedor (read model assíncrono), ordenada por valor.</summary>
    Task<IReadOnlyList<SupplierStatsView>> ListStatsAsync(CancellationToken ct = default);
}
