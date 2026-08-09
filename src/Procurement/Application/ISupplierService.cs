using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Procurement.Application;

public sealed record SupplierView(Guid Id, string Code, string Name, string TaxId, string Status);

/// <summary>Cadastro de fornecedores (PR-001 / PRC-005).</summary>
public interface ISupplierService
{
    Task<Result<Guid>> CreateAsync(string code, string name, string taxId, CancellationToken ct = default);
    Task<IReadOnlyList<SupplierView>> ListAsync(CancellationToken ct = default);
}
