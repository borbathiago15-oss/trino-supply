using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Api.Multitenancy;

/// <summary>
/// Resolve o tenant a partir do claim <c>company_id</c> do JWT (FD-001-01).
/// Toda operação filtra por este <see cref="CompanyId"/>; a RLS usa o mesmo valor
/// (SET LOCAL app.current_company — deploy/db/rls.sql). Recurso fora do escopo → 404.
/// </summary>
public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    private const string CompanyClaim = "company_id";

    public bool HasTenant => TryGet(out _);

    public CompanyId CompanyId => TryGet(out var id)
        ? CompanyId.From(id)
        : throw new InvalidOperationException("Requisição sem tenant (claim company_id ausente).");

    private bool TryGet(out Guid companyId)
    {
        companyId = Guid.Empty;
        var value = accessor.HttpContext?.User.FindFirst(CompanyClaim)?.Value;
        return value is not null && Guid.TryParse(value, out companyId) && companyId != Guid.Empty;
    }
}
