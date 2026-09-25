using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;
using static TrinoSupply.Foundation.Api.Rotas.Anexos;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// O download de anexo, com a autorização por papel e por vínculo — é aqui que
/// o fornecedor só alcança o que é dele, e o time interno só o que o papel
/// permite.
///
/// Saiu do Program.cs no ARQ-A. O conteúdo é o mesmo — grupo, filtros e
/// handlers na mesma ordem —, só mudou de arquivo.
/// </summary>
public static class DocumentoRotas
{
    public static void MapDocumentos(this WebApplication app)
    {
        // ==== Documentos (download autorizado por papel/vínculo) =====================
        app.MapGet("/api/v1/documents/{id:guid}", async (Guid id, AppDbContext db, Suporte.ChamadoService chamados, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var doc = await db.StoredDocuments.SingleOrDefaultAsync(d => d.Id == id);
            if (doc is null) return Error(ctx, 404, "DOC-ERR-404", "Documento não encontrado.");
            var role = RoleOf(p);
            if (role == "Supplier")
            {
                if (PortalSupplierId(p) != doc.SupplierId) return Error(ctx, 404, "DOC-ERR-404", "Documento não encontrado.");
            }
            else if (doc.EntityType == "REQUISITION")
            {
                // anexo de SC: o titular da solicitação sempre baixa o seu próprio documento
                var actor = BuildActor(p)!;
                var owner = await db.Requisitions.AnyAsync(r => r.Id == doc.EntityId && r.RequesterId == actor.Id);
                if (!owner && !actor.SeesAll && !QuotationService.CanView(role) && role != Roles.Auditor)
                    return Error(ctx, 403, "DOC-ERR-900", "Seu papel não acessa este documento.");
            }
            else if (doc.EntityType == Catalog.CatalogService.TipoDaFoto)
            {
                // a foto do produto é parte do catálogo, que todo papel interno consulta: quem
                // pede precisa ver o que está pedindo, e não só quem compra
            }
            else if (doc.EntityType == Suporte.ChamadoService.TipoDoAnexo)
            {
                // o print do chamado é de quem abriu e de quem atende — a mesma régua da leitura
                // do chamado, e o mesmo 404 para quem não o enxerga
                var quem = new Suporte.QuemChama(ActorId(p), "", Suporte.ChamadoService.Atende(role, ModulesOf(p)));
                if (!await chamados.PodeVerAsync(quem, doc.EntityId))
                    return Error(ctx, 404, "DOC-ERR-404", "Documento não encontrado.");
            }
            else if (!QuotationService.CanView(role) && role != Roles.Auditor)
                return Error(ctx, 403, "DOC-ERR-900", "Seu papel não acessa documentos do processo.");
            return Results.File(doc.Content, doc.ContentType, doc.FileName);
        }).RequireAuthorization();

        // ==== Cadastro da Empresa (cabeçalho da OC) ==================================
        app.MapGet("/api/v1/company", async (AppDbContext db, HttpContext ctx) =>
        {
            var c = await db.CompanyProfiles.FirstOrDefaultAsync();
            return Ok(c is null ? new { } : (object)c, ctx);
        }).RequireAuthorization().AddEndpointFilter(RejectSupplierRole());

        app.MapPut("/api/v1/company", async (CompanyProfileRequest body, AppDbContext db, TimeProvider clock, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (RoleOf(p) != Roles.SystemAdministrator)
                return Error(ctx, 403, "IAM-ERR-018", "Somente o administrador mantém o cadastro da empresa.");
            var c = await db.CompanyProfiles.FirstOrDefaultAsync();
            if (c is null) { c = new CompanyProfile(); db.CompanyProfiles.Add(c); }
            c.LegalName = body.LegalName.Trim();
            c.Address = body.Address.Trim();
            c.District = body.District?.Trim();
            c.City = body.City.Trim();
            c.State = body.State.Trim().ToUpperInvariant();
            c.Zip = body.Zip.Trim();
            c.TaxId = body.TaxId.Trim();
            c.StateRegistration = body.StateRegistration?.Trim();
            c.Phone = body.Phone?.Trim();
            c.Email = body.Email?.Trim();
            c.DeliveryAddress = body.DeliveryAddress?.Trim();
            c.DeliveryTaxId = body.DeliveryTaxId?.Trim();
            c.StandardClauses = body.StandardClauses?.Trim();
            c.PaymentPolicy = body.PaymentPolicy?.Trim();
            c.UpdatedAt = clock.GetUtcNow();
            c.UpdatedByLabel = p.FindFirstValue("name") ?? "Administrador";
            await db.SaveChangesAsync();
            return Ok(c, ctx);
        }).RequireAuthorization();
    }
}
