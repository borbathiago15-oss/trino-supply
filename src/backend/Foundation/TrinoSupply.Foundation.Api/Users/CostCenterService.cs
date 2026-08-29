using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Users;

/// <summary>Cadastro de centros de custo (dimensões regional/gerente/cliente dos dashboards).</summary>
public class CostCenterService(AppDbContext db, TimeProvider clock)
{
    public static bool CanMaintain(string role) =>
        role is Roles.SupplyManager or Roles.SystemAdministrator;

    public Task<List<CostCenter>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        var q = db.CostCenters.Include(c => c.Approvers).AsQueryable();
        if (!includeInactive) q = q.Where(c => c.Active);
        return q.OrderBy(c => c.Code).Take(1000).ToListAsync(ct);
    }

    /// <summary>Limite de valor por nível (V2-P3): só mede — negativo é o único inválido.</summary>
    private static UserError? LimitError(decimal? limit) =>
        limit is < 0 ? new("CC-ERR-016", "O limite de valor por nível não pode ser negativo.") : null;

    public async Task<(CostCenter? cc, UserError? error)> CreateAsync(
        Guid actorId, string? code, string name, string? region, Guid? managerUserId, string? client,
        Guid? companyId = null, IReadOnlyList<Guid>? level1 = null, IReadOnlyList<Guid>? level2 = null,
        decimal? level1ValueLimit = null, decimal? level2ValueLimit = null,
        CancellationToken ct = default)
    {
        if (LimitError(level1ValueLimit) is { } l1e) return (null, l1e);
        if (LimitError(level2ValueLimit) is { } l2e) return (null, l2e);
        if (name.Trim().Length < 3) return (null, new("CC-ERR-012", "Informe o nome do centro de custo."));
        var regionClean = Clean(region)?.ToUpperInvariant();

        code = Clean(code)?.ToUpperInvariant();
        if (code is null)
            code = await GenerateCodeAsync(regionClean, ct);   // regra automática: sigla da regional + sequência
        else if (await db.CostCenters.AnyAsync(c => c.Code == code, ct))
            return (null, new("CC-ERR-010", "Já existe um centro de custo com este código."));

        string? managerName = null;
        if (managerUserId is not null)
        {
            var manager = await db.Users.SingleOrDefaultAsync(u => u.Id == managerUserId && u.Active, ct);
            if (manager is null) return (null, new("CC-ERR-013", "Gerente responsável inválido: escolha um usuário ativo."));
            managerName = manager.Name;
        }
        if (companyId is not null && !await db.Companies.AnyAsync(c => c.Id == companyId && c.Active, ct))
            return (null, new("CC-ERR-014", "CNPJ (empresa) inválido: escolha uma empresa ativa do grupo."));

        var now = clock.GetUtcNow();
        var cc = new CostCenter
        {
            Code = code,
            Name = name.Trim(),
            Region = regionClean,
            CompanyId = companyId,
            ManagerUserId = managerUserId,
            ManagerName = managerName,
            Level1ValueLimit = level1ValueLimit,
            Level2ValueLimit = level2ValueLimit,
            ClientName = Clean(client),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = actorId,
        };
        db.CostCenters.Add(cc);
        if (await ReplaceApproversAsync(cc, level1, level2, ct) is { } approverError) return (null, approverError);
        await db.SaveChangesAsync(ct);
        return (cc, null);
    }

    /// <summary>
    /// Alçadas do centro: quem aprova no Nível 1 e no Nível 2. Qualquer pessoa do nível resolve
    /// a etapa (revisão de telas 2026-08-26). Passar null mantém a lista como está.
    /// </summary>
    private async Task<UserError?> ReplaceApproversAsync(
        CostCenter cc, IReadOnlyList<Guid>? level1, IReadOnlyList<Guid>? level2, CancellationToken ct)
    {
        foreach (var (ids, level) in new[] { (level1, ApprovalLevels.Level1), (level2, ApprovalLevels.Level2) })
        {
            if (ids is null) continue;
            var atuais = cc.Approvers.Where(a => a.Level == level).ToList();
            if (atuais.Count > 0)
            {
                db.CostCenterApprovers.RemoveRange(atuais);
                foreach (var a in atuais) cc.Approvers.Remove(a);
            }
            foreach (var userId in ids.Distinct())
            {
                var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.Active, ct);
                if (user is null)
                    return new("CC-ERR-015", $"Aprovador do Nível {level} inválido: escolha usuários ativos.");
                db.CostCenterApprovers.Add(new CostCenterApprover
                {
                    CostCenterId = cc.Id, UserId = user.Id, UserName = user.Name,
                    Level = level, CreatedAt = clock.GetUtcNow(),
                });
            }
        }
        return null;
    }

    /// <summary>Código automático: 2–3 letras da regional (sem acentos; "CC" sem regional) + sequência.</summary>
    private async Task<string> GenerateCodeAsync(string? region, CancellationToken ct)
    {
        var prefix = "CC";
        if (!string.IsNullOrWhiteSpace(region))
        {
            var letters = new string(region.Normalize(System.Text.NormalizationForm.FormD)
                .Where(char.IsLetter).ToArray()).ToUpperInvariant();
            if (letters.Length >= 2) prefix = letters[..Math.Min(3, letters.Length)];
        }
        var seq = await db.CostCenters.CountAsync(c => c.Code.StartsWith(prefix + "-"), ct) + 1;
        var code = $"{prefix}-{seq:000}";
        while (await db.CostCenters.AnyAsync(c => c.Code == code, ct))
            code = $"{prefix}-{++seq:000}";
        return code;
    }

    public async Task<(CostCenter? cc, UserError? error)> UpdateAsync(
        Guid id, string? name, string? region, Guid? managerUserId, string? client, bool? active,
        Guid? companyId = null, IReadOnlyList<Guid>? level1 = null, IReadOnlyList<Guid>? level2 = null,
        decimal? level1ValueLimit = null, decimal? level2ValueLimit = null, bool clearValueLimits = false,
        CancellationToken ct = default)
    {
        if (LimitError(level1ValueLimit) is { } l1e) return (null, l1e);
        if (LimitError(level2ValueLimit) is { } l2e) return (null, l2e);
        var cc = await db.CostCenters.Include(c => c.Approvers).SingleOrDefaultAsync(c => c.Id == id, ct);
        if (cc is null) return (null, new("CC-ERR-404", "Centro de custo não encontrado."));
        if (clearValueLimits) { cc.Level1ValueLimit = null; cc.Level2ValueLimit = null; }
        if (level1ValueLimit is not null) cc.Level1ValueLimit = level1ValueLimit;
        if (level2ValueLimit is not null) cc.Level2ValueLimit = level2ValueLimit;
        if (name is not null && name.Trim().Length >= 3) cc.Name = name.Trim();
        if (region is not null) cc.Region = Clean(region)?.ToUpperInvariant();
        if (companyId is not null)
        {
            if (!await db.Companies.AnyAsync(c => c.Id == companyId && c.Active, ct))
                return (null, new("CC-ERR-014", "CNPJ (empresa) inválido: escolha uma empresa ativa do grupo."));
            cc.CompanyId = companyId;
        }
        if (managerUserId is not null)
        {
            var manager = await db.Users.SingleOrDefaultAsync(u => u.Id == managerUserId && u.Active, ct);
            if (manager is null) return (null, new("CC-ERR-013", "Gerente responsável inválido: escolha um usuário ativo."));
            cc.ManagerUserId = manager.Id;
            cc.ManagerName = manager.Name;
        }
        if (client is not null) cc.ClientName = Clean(client);
        if (active is not null) cc.Active = active.Value;
        if (await ReplaceApproversAsync(cc, level1, level2, ct) is { } approverError) return (null, approverError);
        cc.UpdatedAt = clock.GetUtcNow();
        cc.Version += 1;
        await db.SaveChangesAsync(ct);
        return (cc, null);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
