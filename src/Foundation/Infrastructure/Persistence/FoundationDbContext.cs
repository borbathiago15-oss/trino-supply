using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.BuildingBlocks.Outbox;
using TrinoSupply.Foundation.Domain.Organization;

namespace TrinoSupply.Foundation.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência do Foundation (ADR-009: o banco é projeção do domínio).
/// - Isolamento multi-tenant por <c>company_id</c> + RLS no PostgreSQL (ADR-015 §3, deploy/db/rls.sql).
/// - Transactional Outbox: eventos de domínio viram OutboxMessage na MESMA transação (ARC-005 §3).
/// </summary>
public sealed class FoundationDbContext(DbContextOptions<FoundationDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("foundation");

        b.Entity<Company>(e =>
        {
            e.ToTable("company");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id)
                .HasColumnName("id")
                .HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.LegalName).HasColumnName("legal_name").HasMaxLength(200).IsRequired();
            e.Property(x => x.TaxId).HasColumnName("tax_id").HasMaxLength(30).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasIndex(x => x.TaxId).IsUnique();
            e.Ignore(x => x.DomainEvents);
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CompanyId).HasColumnName("company_id");
            e.Property(x => x.Type).HasColumnName("type").HasMaxLength(200).IsRequired();
            e.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.AggregateType).HasColumnName("aggregate_type").HasMaxLength(100);
            e.Property(x => x.AggregateId).HasColumnName("aggregate_id");
            e.Property(x => x.CorrelationId).HasColumnName("correlation_id");
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            e.Property(x => x.PublishedAt).HasColumnName("published_at");
            e.Property(x => x.RetryCount).HasColumnName("retry_count");
            e.Property(x => x.LastError).HasColumnName("last_error");
            e.HasIndex(x => x.PublishedAt); // varredura de pendentes pelo publisher
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        WriteOutboxFromDomainEvents();
        return await base.SaveChangesAsync(ct);
    }

    /// <summary>Coleta eventos de domínio das raízes rastreadas e os grava no Outbox (ARC-005 §3).</summary>
    private void WriteOutboxFromDomainEvents()
    {
        var roots = ChangeTracker.Entries<AggregateRoot<CompanyId>>()
            .Select(x => x.Entity)
            .Where(x => x.DomainEvents.Count > 0)
            .ToList();

        foreach (var root in roots)
        {
            foreach (var ev in root.DomainEvents)
            {
                Outbox.Add(new OutboxMessage
                {
                    Id = ev.EventId == Guid.Empty ? Guid.NewGuid() : ev.EventId,
                    CompanyId = tenant.HasTenant ? tenant.CompanyId.Value : root.Id.Value,
                    Type = ev.GetType().Name,
                    Payload = JsonSerializer.Serialize(ev, ev.GetType()),
                    AggregateType = root.GetType().Name,
                    AggregateId = root.Id.Value,
                    OccurredAt = ev.OccurredAt
                });
            }
            root.ClearDomainEvents();
        }
    }
}
