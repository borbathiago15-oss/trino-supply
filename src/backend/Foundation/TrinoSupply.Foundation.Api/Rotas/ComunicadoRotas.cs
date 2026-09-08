using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Comunicados;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// Comunicados do administrador: o recado que aparece ao abrir o sistema.
///
/// Duas audiências no mesmo recurso, e por isso dois níveis de permissão:
/// `/current` e `/dismiss` são de **todo usuário autenticado** (é o recado dele);
/// o resto é do administrador. O fornecedor do Portal fica de fora — comunicado
/// interno não é assunto dele.
/// </summary>
public static class ComunicadoRotas
{
    public static void MapComunicados(this WebApplication app)
    {
        var com = app.MapGroup("/api/v1/announcements").RequireAuthorization();
        com.AddEndpointFilter(RejectSupplierRole());

        static object View(Announcement a) => new
        {
            id = a.Id, title = a.Title, body = a.Body,
            imageDocumentId = a.ImageDocumentId, imageFileName = a.ImageFileName,
            startsOn = a.StartsOn, endsOn = a.EndsOn, active = a.Active,
            createdByLabel = a.CreatedByLabel, createdAt = a.CreatedAt,
            dismissedCount = a.Dismissals.Count,
        };

        // o recado desta pessoa, agora — a consulta que a tela faz ao abrir
        com.MapGet("/current", async (AnnouncementService svc, ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            var itens = await svc.CurrentForAsync(ActorId(p), ct);
            return Ok(new { items = itens.Select(View) }, ctx);
        });

        com.MapPost("/{id:guid}/dismiss", async (Guid id, AnnouncementService svc,
            ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            var erro = await svc.DismissAsync(id, ActorId(p), ct);
            return erro is not null ? Error(ctx, 404, erro.Code, erro.Message) : Ok(new { dismissed = true }, ctx);
        });

        // a imagem é do comunicado, e comunicado é para todo mundo: quem enxerga o
        // recado enxerga o cartaz dele, sem passar pela autorização documental de anexo
        com.MapGet("/{id:guid}/image", async (Guid id, AppDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            var alvo = await db.Announcements.Where(a => a.Id == id)
                .Select(a => a.ImageDocumentId).SingleOrDefaultAsync(ct);
            if (alvo is null) return Error(ctx, 404, "COM-ERR-404", "Comunicado sem imagem.");
            var doc = await db.StoredDocuments.SingleOrDefaultAsync(d => d.Id == alvo.Value, ct);
            return doc is null ? Error(ctx, 404, "DOC-ERR-404", "Imagem não encontrada.")
                : Results.File(doc.Content, doc.ContentType, doc.FileName);
        });

        // ---- daqui para baixo, só o administrador -------------------------------
        com.MapGet("/", async (AnnouncementService svc, ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            if (!AnnouncementService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "COM-ERR-900", "Somente o administrador mantém os comunicados.");
            var itens = await svc.ListAsync(ct);
            return Ok(new { items = itens.Select(View) }, ctx);
        });

        com.MapPost("/", async (AnnouncementInput body, AnnouncementService svc,
            ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            if (!AnnouncementService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "COM-ERR-900", "Somente o administrador mantém os comunicados.");
            var (item, erro) = await svc.CreateAsync(ActorId(p), p.FindFirstValue("name") ?? "Administrador", body, ct);
            return erro is not null ? Error(ctx, 400, erro.Code, erro.Message)
                : Results.Json(new { data = View(item!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        com.MapPatch("/{id:guid}", async (Guid id, AnnouncementInput body, AnnouncementService svc,
            ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            if (!AnnouncementService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "COM-ERR-900", "Somente o administrador mantém os comunicados.");
            var (item, erro) = await svc.UpdateAsync(id, body, ct);
            return erro is not null ? Error(ctx, erro.Code == "COM-ERR-404" ? 404 : 400, erro.Code, erro.Message)
                : Ok(View(item!), ctx);
        });

        com.MapDelete("/{id:guid}", async (Guid id, AnnouncementService svc,
            ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            if (!AnnouncementService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "COM-ERR-900", "Somente o administrador mantém os comunicados.");
            var erro = await svc.DeleteAsync(id, ct);
            return erro is not null ? Error(ctx, 404, erro.Code, erro.Message) : Ok(new { deleted = true }, ctx);
        });

        app.MapPost("/api/v1/announcements/{id:guid}/image", async (Guid id, HttpRequest request,
            AppDbContext db, AnnouncementService svc, TimeProvider clock,
            ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            if (!AnnouncementService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "COM-ERR-900", "Somente o administrador mantém os comunicados.");
            if (!request.HasFormContentType) return Error(ctx, 400, "DOC-ERR-001", "Envie o arquivo como multipart/form-data.");
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0) return Error(ctx, 400, "DOC-ERR-001", "Nenhum arquivo enviado.");
            if (file.Length > StoredDocument.MaxSizeBytes) return Error(ctx, 400, "DOC-ERR-002", "Arquivo acima de 10 MB.");
            if (file.ContentType is not ("image/png" or "image/jpeg" or "image/webp"))
                return Error(ctx, 400, "DOC-ERR-003", "A imagem do comunicado precisa ser PNG, JPG ou WEBP.");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var doc = new StoredDocument
            {
                FileName = Path.GetFileName(file.FileName), ContentType = file.ContentType, SizeBytes = file.Length,
                Content = ms.ToArray(), EntityType = "COMUNICADO_IMAGEM", EntityId = id,
                UploadedByLabel = p.FindFirstValue("name") ?? "Administrador", UploadedAt = clock.GetUtcNow(),
            };
            db.StoredDocuments.Add(doc);
            await db.SaveChangesAsync(ct);
            var (item, erro) = await svc.SetImageAsync(id, doc.Id, doc.FileName, ct);
            return erro is not null ? Error(ctx, 404, erro.Code, erro.Message)
                : Ok(new { documentId = doc.Id, fileName = doc.FileName, announcement = View(item!) }, ctx);
        }).RequireAuthorization().AddEndpointFilter(RejectSupplierRole())
            .RequireRateLimiting("upload").ComTetoDeUpload(StoredDocument.MaxRequestBytes);
    }
}
