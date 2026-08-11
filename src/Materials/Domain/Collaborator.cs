using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Materials.Domain;

public readonly record struct CollaboratorId(Guid Value)
{
    public static CollaboratorId New() => new(Guid.NewGuid());
    public static CollaboratorId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("CollaboratorId não pode ser vazio.", nameof(value))
        : new CollaboratorId(value);
    public override string ToString() => Value.ToString();
}

public enum CollaboratorStatus { Active = 1, Inactive = 2 }

/// <summary>
/// Colaborador (spec Sistema de Almoxarifado) — a pessoa que recebe o EPI/fardamento na baixa de
/// consumo. Cadastro leve por tenant (RLS): nome, matrícula (opcional), e centro de custo/empresa
/// padrão (opcionais, apenas para pré-preencher a baixa). Alimenta o indicador "consumo por colaborador".
/// </summary>
public sealed class Collaborator : AggregateRoot<CollaboratorId>, IBelongsToTenant
{
    private Collaborator(CollaboratorId id, CompanyId companyId, string name, string? registration,
        string? costCenterCode, string? companyCode, DateOnly? admissionDate) : base(id)
    {
        CompanyId = companyId;
        Name = name;
        Registration = registration;
        CostCenterCode = costCenterCode;
        CompanyCode = companyCode;
        AdmissionDate = admissionDate;
        Status = CollaboratorStatus.Active;
    }

    private Collaborator() : base(default!) { } // EF

    public CompanyId CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Registration { get; private set; }     // matrícula (opcional)
    public string? CostCenterCode { get; private set; }   // centro de custo padrão (opcional)
    public string? CompanyCode { get; private set; }      // empresa padrão (opcional)
    public DateOnly? AdmissionDate { get; private set; }  // data de contratação (ficha de EPI)
    public CollaboratorStatus Status { get; private set; }

    private static string? Norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToUpperInvariant();

    public static Result<Collaborator> Create(CompanyId companyId, string name, string? registration,
        string? costCenterCode, string? companyCode, DateOnly? admissionDate = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Collaborator>(new Error("materials.collaborator.name_required", "Nome do colaborador é obrigatório."));

        return Result.Success(new Collaborator(
            CollaboratorId.New(), companyId, name.Trim(),
            string.IsNullOrWhiteSpace(registration) ? null : registration.Trim(),
            Norm(costCenterCode), Norm(companyCode), admissionDate));
    }

    public void Deactivate() { Status = CollaboratorStatus.Inactive; Version++; }
}
