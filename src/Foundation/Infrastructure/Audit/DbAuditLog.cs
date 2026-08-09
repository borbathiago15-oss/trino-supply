using System.Text.Json;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.BuildingBlocks.Security;
using TrinoSupply.Foundation.Application.Audit;
using TrinoSupply.Foundation.Domain.Audit;
using TrinoSupply.Foundation.Infrastructure.Persistence;

namespace TrinoSupply.Foundation.Infrastructure.Audit;

/// <summary>
/// Grava a trilha de auditoria no mesmo <see cref="FoundationDbContext"/> (scoped) da operação, de
/// modo que o registro é persistido na MESMA transação (SEC-002 anti-repúdio). O ator vem do JWT.
/// </summary>
public sealed class DbAuditLog(
    FoundationDbContext db, ITenantContext tenant, ICurrentUser currentUser, IClock clock) : IAuditLog
{
    public void Record(string action, string? targetType = null, string? targetId = null, object? metadata = null)
    {
        if (!tenant.HasTenant)
            throw new InvalidOperationException("Auditoria requer tenant. Ver SEC-004.");

        var actor = currentUser.IsAuthenticated
            ? currentUser.Subject ?? "unknown"
            : "system";

        var json = metadata is null ? null : JsonSerializer.Serialize(metadata, metadata.GetType());

        db.AuditEntries.Add(AuditEntry.Create(
            tenant.CompanyId, clock.UtcNow, actor, action, targetType, targetId, json));
    }
}
