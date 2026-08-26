using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Domain;

/// <summary>Pessoa autorizada a aprovar no centro de custo, em um dos dois níveis.</summary>
public class CostCenterApprover
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CostCenterId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;   // snapshot
    public int Level { get; set; } = 1;                    // 1 = primeira alçada, 2 = segunda
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Alçadas por centro de custo (revisão de telas 2026-08-26): cada centro tem uma lista de
/// aprovadores de Nível 1 e outra de Nível 2, e qualquer pessoa do nível resolve a etapa.
/// Enquanto um centro não tiver listas, valem os vínculos antigos — o gerente cadastrado no
/// centro para o Nível 1 e o diretor vinculado a ele para o Nível 2 —, para nenhuma fila travar.
/// </summary>
public static class ApprovalLevels
{
    public const int Level1 = 1;
    public const int Level2 = 2;

    /// <summary>Aprovadores cadastrados no nível informado (lista vazia = nível não configurado).</summary>
    public static async Task<List<CostCenterApprover>> OfAsync(
        AppDbContext db, string costCenterCode, int level, CancellationToken ct = default)
    {
        var code = (costCenterCode ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0) return [];
        return await db.CostCenters.Where(c => c.Active && c.Code.ToUpper() == code)
            .SelectMany(c => c.Approvers.Where(a => a.Level == level))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Diz se a pessoa pode decidir neste nível. Sem lista cadastrada, devolve null: quem chama
    /// decide o que fazer com o vínculo antigo (gerente do centro / diretor do gerente).
    /// </summary>
    public static async Task<bool?> CanDecideAsync(
        AppDbContext db, string costCenterCode, int level, Guid userId, CancellationToken ct = default)
    {
        var approvers = await OfAsync(db, costCenterCode, level, ct);
        if (approvers.Count == 0) return null;
        return approvers.Any(a => a.UserId == userId);
    }

    /// <summary>Nomes do nível, para dizer ao usuário de quem a aprovação está esperando.</summary>
    public static async Task<string?> LabelAsync(
        AppDbContext db, string costCenterCode, int level, CancellationToken ct = default)
    {
        var approvers = await OfAsync(db, costCenterCode, level, ct);
        return approvers.Count == 0 ? null : string.Join(", ", approvers.Select(a => a.UserName));
    }
}
