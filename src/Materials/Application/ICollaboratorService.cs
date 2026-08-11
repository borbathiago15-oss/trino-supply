using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Materials.Application;

public sealed record CollaboratorView(
    Guid Id, string Name, string? Registration, string? CostCenterCode, string? CompanyCode,
    DateOnly? AdmissionDate, string Status);

public sealed record CollaboratorInput(
    string Name, string? Registration, string? CostCenterCode, string? CompanyCode, DateOnly? AdmissionDate);

/// <summary>Cadastro de colaboradores (spec Almoxarifado) — quem recebe o EPI/fardamento na baixa.</summary>
public interface ICollaboratorService
{
    Task<Result<Guid>> CreateAsync(CollaboratorInput input, CancellationToken ct = default);
    Task<IReadOnlyList<CollaboratorView>> ListAsync(int limit = 500, CancellationToken ct = default);
}
