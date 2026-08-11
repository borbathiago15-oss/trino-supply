using TrinoSupply.Foundation.Application.Audit;
using TrinoSupply.Foundation.Infrastructure.Persistence;

namespace TrinoSupply.Foundation.Infrastructure.Audit;

/// <summary>
/// Auditoria para serviços de OUTROS bounded contexts (Procurement/Materials), cujo DbContext não é o
/// do Foundation. Registra e persiste imediatamente na trilha (transação própria) — chamada APÓS a
/// operação de negócio confirmar. Trade-off consciente: em caso de crash entre commits, a ação vale
/// sem o registro; aceitável para trilha (SEC-002) sem acoplar transações entre contexts.
/// </summary>
public interface IBusinessAudit
{
    Task RecordAsync(string action, string? targetType = null, string? targetId = null,
        object? metadata = null, CancellationToken ct = default);
}

public sealed class BusinessAudit(IAuditLog audit, FoundationDbContext db) : IBusinessAudit
{
    public async Task RecordAsync(string action, string? targetType = null, string? targetId = null,
        object? metadata = null, CancellationToken ct = default)
    {
        audit.Record(action, targetType, targetId, metadata);
        await db.SaveChangesAsync(ct);
    }
}
