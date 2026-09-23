using System.Security.Claims;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Suporte;
using static TrinoSupply.Foundation.Api.Rotas.Anexos;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// O chamado de suporte. Toda pessoa autenticada abre e acompanha os seus; a fila inteira é
/// de quem atende (<see cref="ChamadoService.Atende(string, IReadOnlyCollection{string})"/>).
/// </summary>
public static class SuporteRotas
{
    public static void MapSuporte(this WebApplication app)
    {
        var suporte = app.MapGroup("/api/v1/support").RequireAuthorization();
        suporte.AddEndpointFilter(RejectSupplierRole());

        static QuemChama Quem(ClaimsPrincipal p) => new(
            ActorId(p), p.FindFirstValue("name") ?? p.FindFirstValue(ClaimTypes.Email) ?? "Usuário",
            ChamadoService.Atende(RoleOf(p), ModulesOf(p)));

        static object Linha(SupportTicket t) => new
        {
            id = t.Id, number = t.Number, status = t.Status, category = t.Category, subject = t.Subject,
            screen = t.Screen, screenLabel = t.ScreenLabel,
            createdByLabel = t.CreatedByLabel, createdAt = t.CreatedAt, updatedAt = t.UpdatedAt,
            assignedToLabel = t.AssignedToLabel,
            resolvedAt = t.ResolvedAt, resolvedByLabel = t.ResolvedByLabel,
        };

        static object Mensagem(SupportTicketMessage m) => new
        {
            id = m.Id, authorLabel = m.AuthorLabel, fromSupport = m.FromSupport, text = m.Text,
            attachmentId = m.AttachmentId, attachmentName = m.AttachmentName, createdAt = m.CreatedAt,
        };

        static IResult Falha(HttpContext ctx, Users.UserError e) =>
            Error(ctx, e.Code.EndsWith("404") ? 404 : 400, e.Code, e.Message);

        suporte.MapGet("/summary", async (ChamadoService svc, ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            var quem = Quem(p);
            var (paraMim, fila) = await svc.ResumoAsync(quem, ct);
            return Ok(new { paraMim, fila, atende = quem.Atende }, ctx);
        });

        suporte.MapGet("/tickets", async (ChamadoService svc, ClaimsPrincipal p, HttpContext ctx,
            string? escopo, string? situacao, CancellationToken ct) =>
        {
            var quem = Quem(p);
            var fila = escopo == "fila";
            if (fila && !quem.Atende)
                return Error(ctx, 403, "CH-ERR-900", "A fila de chamados é de quem atende o suporte.");
            var itens = await svc.ListarAsync(quem, fila, situacao, ct);
            return Ok(new { items = itens.Select(Linha) }, ctx);
        });

        suporte.MapPost("/tickets", async (AbrirChamado body, ChamadoService svc, ClaimsPrincipal p,
            HttpContext ctx, CancellationToken ct) =>
        {
            var (chamado, erro) = await svc.AbrirAsync(Quem(p), body, ct);
            if (erro is not null) return Falha(ctx, erro);
            return Results.Json(new
            {
                data = new { ticket = Linha(chamado!), firstMessageId = chamado!.Messages.FirstOrDefault()?.Id },
                correlationId = CorrelationId(ctx),
            }, statusCode: 201);
        });

        suporte.MapGet("/tickets/{id:guid}", async (Guid id, ChamadoService svc, ClaimsPrincipal p,
            HttpContext ctx, CancellationToken ct) =>
        {
            var quem = Quem(p);
            var chamado = await svc.AbrirParaLerAsync(quem, id, ct);
            if (chamado is null) return Error(ctx, 404, "CH-ERR-404", "Chamado não encontrado.");
            return Ok(new
            {
                ticket = Linha(chamado), clientInfo = chamado.ClientInfo,
                messages = chamado.Messages.Select(Mensagem),
                // o que a tela pode oferecer — decidido aqui, com a mesma régua da gravação
                souDoSuporte = quem.Atende && chamado.CreatedById != quem.Id,
            }, ctx);
        });

        suporte.MapPost("/tickets/{id:guid}/messages", async (Guid id, ResponderChamado body,
            ChamadoService svc, ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            var (mensagem, chamado, erro) = await svc.ResponderAsync(Quem(p), id, body.Text, body.Resolve == true, ct);
            if (erro is not null) return Falha(ctx, erro);
            return Ok(new { ticket = Linha(chamado!), message = mensagem is null ? null : Mensagem(mensagem) }, ctx);
        });

        suporte.MapPost("/tickets/{id:guid}/messages/{mensagemId:guid}/attachment", async (Guid id, Guid mensagemId,
            HttpRequest request, ChamadoService svc, AppDbContext db, TimeProvider clock, ClaimsPrincipal p,
            HttpContext ctx, CancellationToken ct) =>
        {
            var (mensagem, erro) = await svc.MensagemParaAnexarAsync(Quem(p), id, mensagemId, ct);
            if (erro is not null) return Falha(ctx, erro);
            var (doc, falha) = await StoreUploadAsync(request, db, clock, p, ChamadoService.TipoDoAnexo, id);
            if (falha is not null) return Error(ctx, 400, falha.Code, falha.Message);
            mensagem!.AttachmentId = doc!.Id;
            mensagem.AttachmentName = doc.FileName;
            await db.SaveChangesAsync(ct);
            return Ok(new { documentId = doc.Id, fileName = doc.FileName }, ctx);
        }).RequireRateLimiting("upload").ComTetoDeUpload(StoredDocument.MaxRequestBytes);
    }
}

public record ResponderChamado(string? Text, bool? Resolve);
