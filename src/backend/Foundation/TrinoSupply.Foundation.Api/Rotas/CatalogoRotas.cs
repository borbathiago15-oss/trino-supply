using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;
using static TrinoSupply.Foundation.Api.Rotas.Anexos;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// MMS-002 — o catálogo: itens, importação por planilha, imagem do item,
/// famílias de produtos e os locais de entrega.
///
/// Saiu do Program.cs no ARQ-A. O conteúdo é o mesmo — grupo, filtros e
/// handlers na mesma ordem —, só mudou de arquivo.
/// </summary>
public static class CatalogoRotas
{
    public static void MapCatalogo(this WebApplication app)
    {
        // ---- MMS-002 — Catálogo de Itens (MVP: famílias + itens) ---------------------
        static object CatalogView(CatalogItem i) => new
        {
            id = i.Id, code = i.Code, description = i.Description, family = i.Family,
            unitOfMeasure = i.UnitOfMeasure, referencePrice = i.ReferencePrice, active = i.Active,
            stockControlled = i.StockControlled, purchasable = i.Purchasable, minimumQty = i.MinimumQty,
            productType = i.ProductType, productTypeLabel = i.ProductType is null ? null : ProductTypes.LabelOf(i.ProductType),
            baseCode = i.BaseCode, size = i.Size,
            imageDocumentId = i.ImageDocumentId, imageFileName = i.ImageFileName,
            // o C.A. é do par produto+fornecedor: a mesma bota tem um C.A. no fornecedor X e outro no Y
            compliancePending = ProductTypes.RequiresCa(i.ProductType)
                                && !i.Suppliers.Any(s => !string.IsNullOrWhiteSpace(s.CaNumber)),
            suppliers = i.Suppliers.OrderBy(s => s.SupplierName).Select(s => new
            {
                id = s.Id, supplierId = s.SupplierId, supplierName = s.SupplierName, taxId = s.TaxId, contact = s.Contact,
                supplierItemCode = s.SupplierItemCode, lastPrice = s.LastPrice, caNumber = s.CaNumber, notes = s.Notes,
            }),
        };

        var catalogGroup = app.MapGroup("/api/v1/items").RequireAuthorization();
        catalogGroup.AddEndpointFilter(RejectSupplierRole());

        // Memória de preço do produto: o que se pagou, de quem e quando. É o que permite
        // perguntar "estamos pagando acima do que já pagamos?" — antes o preço vivia solto
        // em cada O.C. e ninguém conseguia olhar a série.
        catalogGroup.MapGet("/{id:guid}/price-history",
            async (Guid id, HistoricoDePrecoService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!QuotationService.CanView(RoleOf(p)))
                return Error(ctx, 403, "MMS-ERR-900", "Seu papel não acessa o histórico de preço.");
            var resumo = (await svc.ResumoAsync([id])).GetValueOrDefault(id);
            return Ok(new
            {
                summary = resumo is null ? null : new
                {
                    last = resumo.Ultimo, lastAt = resumo.UltimoEm,
                    lastSupplierId = resumo.UltimoFornecedorId, lastSupplier = resumo.UltimoFornecedor,
                    average = resumo.Medio, min = resumo.Minimo, max = resumo.Maximo,
                    purchases = resumo.Compras, suppliers = resumo.Fornecedores,
                },
                items = (await svc.SerieAsync(id)).Select(c => new
                {
                    at = c.Em, supplierId = c.SupplierId, supplierName = c.SupplierName,
                    orderNumber = c.OrderNumber, unitPrice = c.UnitPrice,
                    quantity = c.Quantity, family = c.Family,
                }),
            }, ctx);
        }).AddEndpointFilter(RejectSupplierRole());

        catalogGroup.MapGet("/families", async (CatalogService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = p.FindFirstValue(ClaimTypes.Role) ?? "";
            return Ok(new { families = await svc.FamiliesAsync(onlyActive: !CatalogService.CanMaintain(role)) }, ctx);
        });

        // números do catálogo (a tela de produtos é por busca e não baixa o acervo inteiro)
        catalogGroup.MapGet("/summary", async (CatalogService svc, HttpContext ctx) =>
        {
            var s = await svc.SummaryAsync();
            return Ok(new
            {
                total = s.Total, active = s.Active, inactive = s.Inactive, compliancePending = s.CompliancePending,
                families = s.Families.Select(f => new { family = f.Family, count = f.Count }),
            }, ctx);
        });

        catalogGroup.MapGet("/", async (CatalogService svc, ClaimsPrincipal p, HttpContext ctx, string? family, string? q, bool? all, bool? stock) =>
        {
            var role = p.FindFirstValue(ClaimTypes.Role) ?? "";
            var includeInactive = all == true && CatalogService.CanMaintain(role);
            var items = await svc.ListAsync(family, q, includeInactive, stock == true);
            return Ok(new { items = items.Select(CatalogView) }, ctx);
        });

        // importação de produtos por planilha (.xlsx/.csv) — preview e confirmação
        app.MapPost("/api/v1/items/import", async (HttpRequest request, CatalogImportService svc,
            ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!CatalogService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.Produtos))
                return Error(ctx, 403, "IC-ERR-900", "Seu usuário não mantém o catálogo.");
            if (!request.HasFormContentType) return Error(ctx, 400, "IMP-ERR-001", "Envie a planilha como multipart/form-data.");

            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0) return Error(ctx, 400, "IMP-ERR-001", "Nenhuma planilha enviada.");
            if (file.Length > StoredDocument.MaxSizeBytes) return Error(ctx, 400, "DOC-ERR-002", "Arquivo acima de 10 MB.");

            List<string[]> rows;
            try
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                ms.Position = 0;
                rows = SpreadsheetReader.Read(ms, file.FileName);
            }
            catch (Exception)
            {
                return Error(ctx, 400, "IMP-ERR-002", "Não consegui ler a planilha: envie um arquivo .xlsx ou .csv válido.");
            }

            string? Field(string name) => form.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.ToString() : null;
            var options = new ImportOptions(
                Family: Field("family") ?? "",
                ProductType: Field("productType"),
                StockControlled: Field("stockControlled") == "true",
                Purchasable: Field("purchasable") != "false",
                MinimumQty: decimal.TryParse(Field("minimumQty"), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var min) ? min : null,
                Unit: Field("unit"),
                Sizes: (Field("sizes") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            var commit = Field("commit") == "true";
            var (result, error) = await svc.ImportAsync(ActorId(p), rows, options, commit);
            if (error is not null) return Error(ctx, 400, error.Code, error.Message);

            // as linhas com problema aparecem primeiro: são elas que o usuário precisa corrigir na planilha
            var problems = result!.Rows.Where(r => r.Status != "NOVO").ToList();
            var shown = problems.Take(400)
                .Concat(result.Rows.Where(r => r.Status == "NOVO").Take(Math.Max(0, 400 - problems.Count)))
                .ToList();

            return Ok(new
            {
                fileName = file.FileName,
                totalLines = result.TotalLines, toCreate = result.ToCreate,
                duplicates = result.Duplicates, errors = result.Errors, committed = result.Committed,
                warnings = result.Warnings,
                rowsTruncated = result.Rows.Count > shown.Count,
                rows = shown.Select(r => new
                {
                    line = r.Line, code = r.Code, description = r.Description,
                    size = r.Size, status = r.Status, message = r.Message,
                }),
                sizeSuggestions = new { letters = CatalogImportService.LetterSizes, numbers = CatalogImportService.NumberSizes },
            }, ctx);
        }).RequireAuthorization().AddEndpointFilter(RejectSupplierRole())
            .RequireRateLimiting("upload").ComTetoDeUpload(StoredDocument.MaxRequestBytes);

        // tipos de produto (lista fixa do catálogo, com as exigências de conformidade)
        app.MapGet("/api/v1/product-types", (HttpContext ctx) => Ok(new
        {
            items = ProductTypes.All.Select(t => new
            {
                key = t.Key, label = t.Label,
                requiresCa = ProductTypes.RequiresCa(t.Key),
            }),
        }, ctx)).RequireAuthorization().AddEndpointFilter(RejectSupplierRole());

        // foto do produto (multipart): miniatura na lista e imagem ampliada ao clicar
        app.MapPost("/api/v1/items/{id:guid}/image", async (Guid id, HttpRequest request, AppDbContext db,
            CatalogService svc, TimeProvider clock, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!CatalogService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.Produtos))
                return Error(ctx, 403, "IC-ERR-900", "Seu usuário não mantém o catálogo.");
            var item = await db.CatalogItems.SingleOrDefaultAsync(i => i.Id == id);
            if (item is null) return Error(ctx, 404, "IC-ERR-404", "Item não encontrado.");
            if (!request.HasFormContentType) return Error(ctx, 400, "DOC-ERR-001", "Envie o arquivo como multipart/form-data.");
            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0) return Error(ctx, 400, "DOC-ERR-001", "Nenhum arquivo enviado.");
            if (file.Length > StoredDocument.MaxSizeBytes) return Error(ctx, 400, "DOC-ERR-002", "Arquivo acima de 10 MB.");
            if (file.ContentType is not ("image/png" or "image/jpeg" or "image/webp"))
                return Error(ctx, 400, "DOC-ERR-003", "A foto do produto precisa ser PNG, JPG ou WEBP.");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var doc = new StoredDocument
            {
                FileName = Path.GetFileName(file.FileName), ContentType = file.ContentType, SizeBytes = file.Length,
                Content = ms.ToArray(), EntityType = CatalogService.TipoDaFoto, EntityId = item.Id,
                UploadedByLabel = p.FindFirstValue("name") ?? "Cadastro", UploadedAt = clock.GetUtcNow(),
            };
            db.StoredDocuments.Add(doc);
            await db.SaveChangesAsync();
            var (updated, error) = await svc.AttachImageAsync(id, doc.Id, doc.FileName);
            return error is not null ? Error(ctx, 400, error.Code, error.Message)
                : Ok(new { documentId = doc.Id, fileName = doc.FileName, item = CatalogView(updated!) }, ctx);
        }).RequireAuthorization().AddEndpointFilter(RejectSupplierRole())
            .RequireRateLimiting("upload").ComTetoDeUpload(StoredDocument.MaxRequestBytes);

        // ---- famílias de produtos (cadastro próprio: evita a mesma família escrita de vários jeitos)
        static object FamilyView(ProductFamily f) => new
        {
            id = f.Id, name = f.Name, notes = f.Notes, active = f.Active, category = f.Category,
            // prazos-meta do processo, em dias: o dashboard compara com o realizado
            leadRequestToQuote = f.LeadRequestToQuote, leadQuoteToApproval = f.LeadQuoteToApproval,
            leadApprovalToPo = f.LeadApprovalToPo, leadPoToDelivery = f.LeadPoToDelivery,
            leadTotal = f.LeadTotal,
        };

        // prazos por etapa só chegam ao serviço quando algum deles vem no corpo
        static CatalogService.FamilyLeadTimes? LeadOf(IFamilyLeadTimes body) =>
            body.ApplyLeadTimes != true && body.LeadRequestToQuote is null && body.LeadQuoteToApproval is null
                && body.LeadApprovalToPo is null && body.LeadPoToDelivery is null
                ? null
                : new(body.LeadRequestToQuote, body.LeadQuoteToApproval, body.LeadApprovalToPo, body.LeadPoToDelivery,
                      body.ApplyLeadTimes == true);

        var families = app.MapGroup("/api/v1/product-families").RequireAuthorization();
        families.AddEndpointFilter(RejectSupplierRole());

        families.MapGet("/", async (CatalogService svc, ClaimsPrincipal p, HttpContext ctx, bool? all) =>
        {
            var includeInactive = all == true && CatalogService.CanMaintain(RoleOf(p));
            return Ok(new { items = (await svc.ListFamiliesAsync(includeInactive)).Select(FamilyView) }, ctx);
        });

        families.MapPost("/", async (ProductFamilyRequest body, CatalogService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!CatalogService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.Produtos))
                return Error(ctx, 403, "IC-ERR-900", "Seu usuário não mantém o catálogo.");
            var (family, error) = await svc.CreateFamilyAsync(ActorId(p), body.Name ?? "", body.Notes, LeadOf(body), body.Category);
            return error is not null ? Error(ctx, error.Code == "IC-ERR-021" ? 409 : 400, error.Code, error.Message)
                : Results.Json(new { data = FamilyView(family!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        families.MapPatch("/{id:guid}", async (Guid id, UpdateProductFamilyRequest body, CatalogService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!CatalogService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.Produtos))
                return Error(ctx, 403, "IC-ERR-900", "Seu usuário não mantém o catálogo.");
            var (family, error) = await svc.UpdateFamilyAsync(id, body.Name, body.Notes, body.Active, LeadOf(body),
                body.Category, body.ClearCategory == true);
            return error is not null ? Error(ctx, error.Code == "IC-ERR-404" ? 404 : 400, error.Code, error.Message)
                : Ok(FamilyView(family!), ctx);
        });

        // excluir a família: só vazia (IC-ERR-031) — com produto dentro, o caminho é inativar
        families.MapDelete("/{id:guid}", async (Guid id, CatalogService svc, ClaimsPrincipal p, HttpContext ctx,
            CancellationToken ct) =>
        {
            if (!CatalogService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.Produtos))
                return Error(ctx, 403, "IC-ERR-900", "Seu usuário não mantém o catálogo.");
            var erro = await svc.ExcluirFamiliaAsync(id, ct);
            return erro is null ? Ok(new { deleted = true }, ctx)
                : Error(ctx, erro.Code == "IC-ERR-404" ? 404 : 409, erro.Code, erro.Message);
        });

        // Locais de entrega para os formulários de SC (sem dados de estoque; aberto a papéis internos).
        // A regra de quais cadastros entram na lista vive em Domain/LocaisDeEntrega.
        app.MapGet("/api/v1/delivery-locations", async (AppDbContext db, HttpContext ctx) =>
            Ok(new { items = await LocaisDeEntrega.ListarAsync(db) }, ctx))
            .RequireAuthorization().AddEndpointFilter(RejectSupplierRole());

        // grade da Solicitação em Lote: saldo, previsão de entrada, consumo médio e cobertura por produto
        catalogGroup.MapGet("/batch-view", async (TrinoSupply.Foundation.Api.Analytics.AnalyticsService svc,
            HttpContext ctx, string? family, string? q, Guid? locationId) =>
            Ok(await svc.BatchViewAsync(family, q, locationId), ctx));

        catalogGroup.MapPost("/", async (CreateCatalogItemRequest body, CatalogService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = p.FindFirstValue(ClaimTypes.Role) ?? "";
            if (!CatalogService.CanMaintain(role))
                return Error(ctx, 403, "IC-ERR-001", "Somente o gestor de suprimentos ou o administrador mantêm o catálogo.");
            if (!ModulesOf(p).Contains(AppModules.Produtos))
                return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para o cadastro de produtos.");
            var suppliers = body.Suppliers?.Select(x => new ItemSupplierInput(
                x.SupplierName ?? "", x.TaxId, x.Contact, x.SupplierItemCode, x.LastPrice, x.Notes, x.SupplierId,
                x.CaNumber)).ToList();
            var (item, error) = await svc.CreateAsync(ActorId(p), body.Code, body.Description, body.Family,
                body.UnitOfMeasure, body.ReferencePrice, body.StockControlled ?? true, body.MinimumQty, suppliers,
                body.Purchasable ?? true, body.ProductType);
            return error is not null
                ? Error(ctx, error.Code == "IC-ERR-010" ? 409 : 400, error.Code, error.Message)
                : Results.Json(new { data = CatalogView(item!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        // grade de tamanhos: um produto por tamanho, de uma vez (bota do 38 ao 44)
        catalogGroup.MapPost("/grade", async (CreateSizeGradeRequest body, CatalogService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!CatalogService.CanMaintain(RoleOf(p)))
                return Error(ctx, 403, "IC-ERR-001", "Somente o gestor de suprimentos ou o administrador mantêm o catálogo.");
            if (!ModulesOf(p).Contains(AppModules.Produtos))
                return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para o cadastro de produtos.");
            var suppliers = body.Suppliers?.Select(x => new ItemSupplierInput(
                x.SupplierName ?? "", x.TaxId, x.Contact, x.SupplierItemCode, x.LastPrice, x.Notes, x.SupplierId,
                x.CaNumber)).ToList();
            var (itens, error) = await svc.CriarGradeAsync(ActorId(p), body.BaseCode, body.Description, body.Family,
                body.UnitOfMeasure, body.ReferencePrice, body.Sizes ?? [], body.StockControlled ?? true,
                body.MinimumQty, suppliers, body.Purchasable ?? true, body.ProductType);
            return error is not null
                ? Error(ctx, error.Code == "IC-ERR-010" ? 409 : 400, error.Code, error.Message)
                : Results.Json(new { data = new { items = itens.Select(CatalogView) }, correlationId = CorrelationId(ctx) },
                    statusCode: 201);
        });

        // o catálogo como quem pede enxerga: um produto por linha, com a grade de tamanhos junto
        catalogGroup.MapGet("/picker", async (CatalogService svc, HttpContext ctx, string? family, string? q) =>
        {
            var itens = await svc.ParaEscolhaAsync(family, q);
            return Ok(new
            {
                items = itens.Select(i => new
                {
                    key = i.Key, baseCode = i.BaseCode, description = i.Description, family = i.Family,
                    unitOfMeasure = i.UnitOfMeasure, productType = i.ProductType, productTypeLabel = i.ProductTypeLabel,
                    hasGrade = i.TemGrade, compliancePending = i.CompliancePending,
                    sizes = i.Sizes.Select(v => new
                    {
                        id = v.Id, code = v.Code, size = v.Size, referencePrice = v.ReferencePrice,
                        compliancePending = v.CompliancePending, imageDocumentId = v.ImageDocumentId,
                    }),
                }),
            }, ctx);
        });

        // a ficha do produto: o que a busca da SC mostra ao clicar, antes de escolher
        catalogGroup.MapGet("/{id:guid}", async (Guid id, CatalogService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var item = await svc.DetalheAsync(id, ct);
            return item is null ? Error(ctx, 404, "IC-ERR-404", "Item não encontrado.") : Ok(CatalogView(item), ctx);
        });

        // excluir de verdade: só o produto que nunca circulou (IC-ERR-030); o resto se inativa
        catalogGroup.MapDelete("/{id:guid}", async (Guid id, CatalogService svc, ClaimsPrincipal p, HttpContext ctx,
            CancellationToken ct) =>
        {
            if (!CatalogService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.Produtos))
                return Error(ctx, 403, "IC-ERR-900", "Seu usuário não mantém o catálogo.");
            var erro = await svc.ExcluirAsync(id, ct);
            return erro is null ? Ok(new { deleted = true }, ctx)
                : Error(ctx, erro.Code == "IC-ERR-404" ? 404 : 409, erro.Code, erro.Message);
        });

        catalogGroup.MapPatch("/{id:guid}", async (Guid id, UpdateCatalogItemRequest body, CatalogService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = p.FindFirstValue(ClaimTypes.Role) ?? "";
            if (!CatalogService.CanMaintain(role))
                return Error(ctx, 403, "IC-ERR-001", "Somente o gestor de suprimentos ou o administrador mantêm o catálogo.");
            if (!ModulesOf(p).Contains(AppModules.Produtos))
                return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para o cadastro de produtos.");
            var suppliers = body.Suppliers?.Select(x => new ItemSupplierInput(
                x.SupplierName ?? "", x.TaxId, x.Contact, x.SupplierItemCode, x.LastPrice, x.Notes, x.SupplierId,
                x.CaNumber)).ToList();
            var (item, error) = await svc.UpdateAsync(id, body.Description, body.Family, body.UnitOfMeasure,
                body.ReferencePrice, body.Active, body.StockControlled, body.MinimumQty, body.ClearMinimum == true, suppliers,
                body.Purchasable, body.ProductType);
            return error is not null
                ? Error(ctx, error.Code == "IC-ERR-404" ? 404 : 400, error.Code, error.Message)
                : Ok(CatalogView(item!), ctx);
        });

        /// <summary>
        /// Quem opera o almoxarifado: os papéis históricos ou qualquer usuário com o módulo
        /// "Estoque / Almoxarifado" autorizado — os papéis de almoxarifado saíram do cadastro
        /// de usuário na revisão de telas de 2026-08-26.
        /// </summary>
    }
}
