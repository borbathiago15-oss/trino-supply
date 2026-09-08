using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Comunicados;

/// <summary>Dados que o administrador escreve; a imagem entra por upload à parte.</summary>
public record AnnouncementInput(string? Title, string? Body, DateOnly? StartsOn, DateOnly? EndsOn, bool? Active);

/// <summary>
/// Comunicados do administrador. Quem escreve é o administrador; quem lê é todo
/// mundo, uma vez, ao abrir o sistema.
/// </summary>
public class AnnouncementService(AppDbContext db, TimeProvider clock)
{
    /// <summary>Escrever comunicado é ato de administração — quem lê é todo usuário autenticado.</summary>
    public static bool CanManage(string role) => role == Roles.SystemAdministrator;

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<List<Announcement>> ListAsync(CancellationToken ct = default) =>
        await db.Announcements.Include(a => a.Dismissals)
            .OrderByDescending(a => a.StartsOn).ThenByDescending(a => a.CreatedAt)
            .Take(200).ToListAsync(ct);

    /// <summary>
    /// O que esta pessoa precisa ver agora: no ar hoje e que ela ainda não fechou.
    /// É a consulta de todo login, então é enxuta de propósito — sem `Include` das
    /// leituras, que só interessam ao administrador.
    /// </summary>
    public async Task<List<Announcement>> CurrentForAsync(Guid userId, CancellationToken ct = default)
    {
        var hoje = Today;
        var fechados = db.AnnouncementDismissals.Where(d => d.UserId == userId).Select(d => d.AnnouncementId);
        return await db.Announcements
            .Where(a => a.Active && a.StartsOn <= hoje && a.EndsOn >= hoje && !fechados.Contains(a.Id))
            .OrderBy(a => a.StartsOn).ThenBy(a => a.CreatedAt)
            .Take(20).ToListAsync(ct);
    }

    public async Task<Announcement?> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.Announcements.Include(a => a.Dismissals).SingleOrDefaultAsync(a => a.Id == id, ct);

    public async Task<(Announcement? item, UserError? error)> CreateAsync(
        Guid actorId, string actorLabel, AnnouncementInput input, CancellationToken ct = default)
    {
        var (erro, titulo, de, ate) = Validar(input, null);
        if (erro is not null) return (null, erro);

        var now = clock.GetUtcNow();
        var item = new Announcement
        {
            Title = titulo!,
            Body = string.IsNullOrWhiteSpace(input.Body) ? null : input.Body.Trim(),
            StartsOn = de!.Value, EndsOn = ate!.Value,
            Active = input.Active ?? true,
            CreatedBy = actorId, CreatedByLabel = actorLabel,
            CreatedAt = now, UpdatedAt = now,
        };
        db.Announcements.Add(item);
        await db.SaveChangesAsync(ct);
        return (item, null);
    }

    public async Task<(Announcement? item, UserError? error)> UpdateAsync(
        Guid id, AnnouncementInput input, CancellationToken ct = default)
    {
        var item = await GetAsync(id, ct);
        if (item is null) return (null, new("COM-ERR-404", "Comunicado não encontrado."));

        var (erro, titulo, de, ate) = Validar(input, item);
        if (erro is not null) return (null, erro);

        item.Title = titulo!;
        if (input.Body is not null) item.Body = string.IsNullOrWhiteSpace(input.Body) ? null : input.Body.Trim();
        item.StartsOn = de!.Value;
        item.EndsOn = ate!.Value;
        if (input.Active is not null) item.Active = input.Active.Value;
        item.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return (item, null);
    }

    public async Task<UserError?> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var item = await db.Announcements.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (item is null) return new("COM-ERR-404", "Comunicado não encontrado.");
        db.Announcements.Remove(item);   // as leituras vão junto (cascade)
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>
    /// "Já li este." Fechar de novo não é erro nem cria segundo registro — a pessoa
    /// pode ter duas abas, e o índice único recusaria a segunda gravação.
    /// </summary>
    public async Task<UserError?> DismissAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        if (!await db.Announcements.AnyAsync(a => a.Id == id, ct))
            return new("COM-ERR-404", "Comunicado não encontrado.");
        if (await db.AnnouncementDismissals.AnyAsync(d => d.AnnouncementId == id && d.UserId == userId, ct))
            return null;
        db.AnnouncementDismissals.Add(new AnnouncementDismissal
        {
            AnnouncementId = id, UserId = userId, DismissedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<(Announcement? item, UserError? error)> SetImageAsync(
        Guid id, Guid documentId, string fileName, CancellationToken ct = default)
    {
        var item = await GetAsync(id, ct);
        if (item is null) return (null, new("COM-ERR-404", "Comunicado não encontrado."));
        item.ImageDocumentId = documentId;
        item.ImageFileName = fileName;
        item.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return (item, null);
    }

    /// <summary>
    /// Título obrigatório e vigência com começo e fim que fazem sentido. A data final
    /// no passado é aceita de propósito: é assim que se encerra um comunicado ainda
    /// no ar sem apagá-lo — e o histórico de quem leu fica de pé.
    /// </summary>
    private static (UserError? erro, string? titulo, DateOnly? de, DateOnly? ate) Validar(
        AnnouncementInput input, Announcement? atual)
    {
        var titulo = (input.Title ?? atual?.Title ?? "").Trim();
        if (titulo.Length < 3)
            return (new("COM-ERR-010", "Informe o título do comunicado (mín. 3 caracteres)."), null, null, null);
        if (titulo.Length > 200)
            return (new("COM-ERR-010", "O título tem no máximo 200 caracteres."), null, null, null);

        var de = input.StartsOn ?? atual?.StartsOn;
        var ate = input.EndsOn ?? atual?.EndsOn;
        if (de is null || ate is null)
            return (new("COM-ERR-011", "Informe a vigência do comunicado: de e até."), null, null, null);
        if (ate < de)
            return (new("COM-ERR-012", "A data final da vigência não pode ser anterior à inicial."), null, null, null);

        return (null, titulo, de, ate);
    }
}
