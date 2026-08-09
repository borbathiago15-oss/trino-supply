using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Foundation.Domain.Organization;

/// <summary>Estado do tenant (FD-001-02 Organization).</summary>
public enum CompanyStatus
{
    Active = 1,
    Inactive = 2
}

/// <summary>
/// Empresa (tenant) — raiz do isolamento multi-tenant (FD-001-02). Todo dado de negócio
/// referencia uma Company; o isolamento é reforçado por RLS no PostgreSQL (ADR-015 §3).
/// </summary>
public sealed class Company : AggregateRoot<CompanyId>
{
    private Company(CompanyId id, string legalName, string taxId, CompanyStatus status) : base(id)
    {
        LegalName = legalName;
        TaxId = taxId;
        Status = status;
    }

    // Construtor sem parâmetros exigido pelo EF Core.
    private Company() : base(default!) { }

    public string LegalName { get; private set; } = string.Empty;
    public string TaxId { get; private set; } = string.Empty;
    public CompanyStatus Status { get; private set; }

    /// <summary>Factory: cria a empresa Ativa e publica o evento de registro (ADR-010).</summary>
    public static Company Register(string legalName, string taxId)
    {
        if (string.IsNullOrWhiteSpace(legalName))
            throw new ArgumentException("Razão social obrigatória.", nameof(legalName));
        if (string.IsNullOrWhiteSpace(taxId))
            throw new ArgumentException("Documento fiscal obrigatório.", nameof(taxId));

        var company = new Company(CompanyId.New(), legalName.Trim(), taxId.Trim(), CompanyStatus.Active);
        company.Raise(new CompanyRegistered(Guid.NewGuid(), DateTimeOffset.UtcNow, company.Id.Value, legalName));
        return company;
    }

    public void Deactivate()
    {
        Status = CompanyStatus.Inactive;
        Version++;
    }
}

/// <summary>Evento de negócio: empresa registrada (ADR-010).</summary>
public sealed record CompanyRegistered(Guid EventId, DateTimeOffset OccurredAt, Guid CompanyId, string LegalName)
    : IDomainEvent;
