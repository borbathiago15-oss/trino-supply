using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Users;

/// <summary>
/// Cadastro de setores. Mesma régua dos centros de custo — é dimensão organizacional, e quem
/// mantém uma mantém a outra. Nenhum mecanismo de permissão novo: o módulo já existe.
/// </summary>
public class SectorService(AppDbContext db, TimeProvider clock)
{
    public static bool CanMaintain(string role) =>
        role is Roles.SupplyManager or Roles.SystemAdministrator;

    public Task<List<Sector>> ListAsync(bool incluirInativos, CancellationToken ct = default)
    {
        var q = db.Sectors.AsQueryable();
        if (!incluirInativos) q = q.Where(s => s.Active);
        return q.OrderBy(s => s.Code).Take(500).ToListAsync(ct);
    }

    public async Task<(Sector? setor, UserError? erro)> CreateAsync(
        Guid actorId, string? code, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2)
            return (null, new("SET-ERR-010", "Informe o nome do setor."));

        var codigo = string.IsNullOrWhiteSpace(code)
            ? DoNome(name)
            : code.Trim().ToUpperInvariant();
        if (codigo.Length == 0) return (null, new("SET-ERR-011", "Informe o código do setor."));
        if (await db.Sectors.AnyAsync(s => s.Code == codigo, ct))
            return (null, new("SET-ERR-012", "Já existe um setor com este código."));

        var agora = clock.GetUtcNow();
        var setor = new Sector
        {
            Code = codigo, Name = name.Trim(),
            CreatedAt = agora, UpdatedAt = agora, CreatedBy = actorId,
        };
        db.Sectors.Add(setor);
        await db.SaveChangesAsync(ct);
        return (setor, null);
    }

    /// <summary>
    /// O código é identidade e <b>não muda</b> depois de gravado — pessoas e ciclos já o
    /// carregam. O nome se corrige; setor fora de uso se inativa, nunca se apaga.
    /// </summary>
    public async Task<(Sector? setor, UserError? erro)> UpdateAsync(
        Guid id, string? name, bool? active, CancellationToken ct = default)
    {
        var setor = await db.Sectors.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (setor is null) return (null, new("SET-ERR-404", "Setor não encontrado."));
        if (name is not null && name.Trim().Length >= 2) setor.Name = name.Trim();
        if (active is not null) setor.Active = active.Value;
        setor.UpdatedAt = clock.GetUtcNow();
        setor.Version += 1;
        await db.SaveChangesAsync(ct);
        return (setor, null);
    }

    /// <summary>Código automático: as letras do nome, sem acento, até seis.</summary>
    private static string DoNome(string nome)
    {
        var letras = new string(nome.Normalize(System.Text.NormalizationForm.FormD)
            .Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return letras.Length <= 6 ? letras : letras[..6];
    }
}
