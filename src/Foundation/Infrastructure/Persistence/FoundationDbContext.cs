using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.BuildingBlocks.Outbox;
using TrinoSupply.Foundation.Domain.Audit;
using TrinoSupply.Foundation.Domain.Iam;
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
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

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

        b.Entity<Role>(e =>
        {
            e.ToTable("role");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasConversion(id => id.Value, v => RoleId.From(v));
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.PrimitiveCollection(x => x.Permissions).HasColumnName("permissions");
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();
            e.Ignore(x => x.DomainEvents);
        });

        b.Entity<User>(e =>
        {
            e.ToTable("app_user"); // "user" é palavra reservada no PostgreSQL
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasConversion(id => id.Value, v => UserId.From(v));
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(200).IsRequired();
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
            e.PrimitiveCollection<List<Guid>>("_roleIds").HasColumnName("role_ids");
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasIndex(x => new { x.CompanyId, x.Subject }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.Email }).IsUnique();
            e.Ignore(x => x.RoleIds);
            e.Ignore(x => x.DomainEvents);
        });

        b.Entity<AuditEntry>(e =>
        {
            e.ToTable("audit_entry");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CompanyId).HasColumnName("company_id").HasConversion(id => id.Value, v => CompanyId.From(v));
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            e.Property(x => x.ActorSubject).HasColumnName("actor_subject").HasMaxLength(200).IsRequired();
            e.Property(x => x.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
            e.Property(x => x.TargetType).HasColumnName("target_type").HasMaxLength(100);
            e.Property(x => x.TargetId).HasColumnName("target_id").HasMaxLength(100);
            e.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
            e.HasIndex(x => new { x.CompanyId, x.OccurredAt });
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

    /// <summary>
    /// Coleta eventos de domínio de TODAS as raízes rastreadas (via <see cref="IHasDomainEvents"/>,
    /// independente do tipo do Id) e os grava no Outbox na mesma transação (ARC-005 §3).
    /// </summary>
    private void WriteOutboxFromDomainEvents()
    {
        var roots = ChangeTracker.Entries<IHasDomainEvents>()
            .Where(x => x.Entity.DomainEvents.Count > 0)
            .ToList();

        foreach (var entry in roots)
        {
            var root = entry.Entity;
            var companyId = ResolveCompanyId(entry);
            var aggregateId = ExtractAggregateId(entry);

            foreach (var ev in root.DomainEvents)
            {
                Outbox.Add(new OutboxMessage
                {
                    Id = ev.EventId == Guid.Empty ? Guid.NewGuid() : ev.EventId,
                    CompanyId = companyId,
                    Type = ev.GetType().Name,
                    Payload = JsonSerializer.Serialize(ev, ev.GetType()),
                    AggregateType = root.GetType().Name,
                    AggregateId = aggregateId,
                    OccurredAt = ev.OccurredAt
                });
            }
            root.ClearDomainEvents();
        }
    }

    /// <summary>
    /// Id do agregado como <see cref="Guid"/>. Desembrulha IDs fortemente tipados do padrão
    /// <c>record struct X(Guid Value)</c> (ex.: <see cref="CompanyId"/>); aceita PK Guid direto.
    /// </summary>
    private static Guid ExtractAggregateId(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        if (entry.Metadata.FindPrimaryKey() is not { } pk)
        {
            return Guid.Empty;
        }

        var value = entry.Property(pk.Properties[0].Name).CurrentValue;
        return value switch
        {
            Guid g => g,
            null => Guid.Empty,
            _ => value.GetType().GetProperty("Value")?.GetValue(value) is Guid inner ? inner : Guid.Empty
        };
    }

    /// <summary>
    /// Tenant do evento (SEC-004, fail-closed). Preferimos o tenant da requisição; no bootstrap
    /// de empresa (evento <c>CompanyRegistered</c>, sem tenant ainda) usamos o próprio Id da
    /// <see cref="Company"/>. Sem nenhum dos dois, é erro de programação — nunca gravamos tenant
    /// arbitrário (evitaria vazamento cross-tenant no Outbox).
    /// </summary>
    private Guid ResolveCompanyId(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        if (tenant.HasTenant)
        {
            return tenant.CompanyId.Value;
        }

        // Bootstrap (sem tenant na requisição): resolve pelo próprio agregado.
        if (entry.Entity is IBelongsToTenant owned)
        {
            return owned.CompanyId.Value;
        }

        if (entry.Entity is Company company)
        {
            return company.Id.Value;
        }

        throw new InvalidOperationException(
            $"Não foi possível determinar o tenant do evento de {entry.Entity.GetType().Name} " +
            "(sem ITenantContext e agregado não é a raiz de tenant). Ver SEC-004.");
    }
}
