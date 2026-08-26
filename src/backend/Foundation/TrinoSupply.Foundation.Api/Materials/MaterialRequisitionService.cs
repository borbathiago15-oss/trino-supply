using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Inventory;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Materials;

public record MaterialItemInput(Guid CatalogItemId, decimal Quantity);

/// <summary>
/// Serviço da Solicitação de Material (MMS-003 MVP): itens do catálogo (MMS-002),
/// atendimento com baixa via Estoque (MMS-004, somente disponível — MMS-RG-09),
/// rota mista por item: sem saldo → rota de compra (MMS-RG-09/PR-001).
/// </summary>
public class MaterialRequisitionService(AppDbContext db, CatalogService catalog, InventoryService inventory, TimeProvider clock)
{
    public static bool CanRequest(string role) =>
        role is Roles.Requester or Roles.SupplyManager or Roles.SystemAdministrator;

    public static bool CanFulfill(string role) => InventoryService.CanOperate(role);

    public static bool CanSeeAll(string role) =>
        CanFulfill(role) || role is Roles.Auditor;

    public async Task<List<MaterialRequisition>> ListAsync(Actor actor, bool queueOnly, CancellationToken ct = default)
    {
        var query = db.MaterialRequisitions.Include(r => r.Items).AsQueryable();
        // a fila do estoque só recebe o que o responsável do centro (Nível 1) aprovou
        if (queueOnly) query = query.Where(r => r.Status == MaterialRequisitionStatus.Approved);
        else if (!CanSeeAll(actor.Role)) query = query.Where(r => r.RequesterId == actor.Id);
        return await query.OrderByDescending(r => r.CreatedAt).Take(100).ToListAsync(ct);
    }

    public async Task<(MaterialRequisition? mr, UserError? error)> CreateAsync(
        Actor actor, string costCenter, string? notes, IReadOnlyList<MaterialItemInput> items, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(costCenter))
            return (null, new("MR-ERR-021", "Informe o centro de custo."));
        if (items.Count == 0)
            return (null, new("MR-ERR-030", "Inclua ao menos um item do catálogo."));
        if (items.Any(i => i.Quantity <= 0))
            return (null, new("MR-ERR-010", "As quantidades devem ser maiores que zero."));

        var (catalogItems, catalogError) = await catalog.ResolveForRequisitionAsync(
            items.Select(i => i.CatalogItemId).Distinct().ToList(), ct);
        if (catalogError is not null) return (null, catalogError);

        var now = clock.GetUtcNow();
        var mr = new MaterialRequisition
        {
            Number = $"MR-{now.Year}-{await NextSeqAsync(ct):000000}",
            CostCenter = costCenter.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            RequesterId = actor.Id,
            RequesterLabel = actor.Label,
            CreatedAt = now,
            UpdatedAt = now,
        };
        foreach (var input in items)
        {
            var c = catalogItems![input.CatalogItemId];
            mr.Items.Add(new MaterialRequisitionItem
            {
                CatalogItemId = c.Id,
                CatalogCode = c.Code,
                Description = c.Description,
                UnitOfMeasure = c.UnitOfMeasure,
                Quantity = input.Quantity,
                CreatedAt = now,
            });
        }
        db.MaterialRequisitions.Add(mr);
        await db.SaveChangesAsync(ct);
        return (mr, null);
    }

    /// <summary>
    /// Atendimento (rota mista por item, MMS-003): para cada item, se o disponível no local
    /// cobrir a quantidade, gera a saída vinculada; senão o item vai para rota de compra.
    /// </summary>
    public record ApprovalLine(Guid ItemId, decimal Quantity);

    /// <summary>
    /// Aprovação do responsável do centro (Nível 1): libera a solicitação para o estoque e pode
    /// reduzir a quantidade de cada item — a quantidade pedida pelo solicitante nunca muda.
    /// </summary>
    public async Task<(MaterialRequisition? mr, UserError? error)> ApproveAsync(
        Actor actor, Guid id, IReadOnlyList<ApprovalLine>? lines, string? notes, CancellationToken ct = default)
    {
        var mr = await db.MaterialRequisitions.Include(r => r.Items).SingleOrDefaultAsync(r => r.Id == id, ct);
        if (mr is null) return (null, new("MR-ERR-404", "Solicitação não encontrada."));
        if (mr.Status != MaterialRequisitionStatus.Submitted)
            return (null, new("MR-ERR-040", "A solicitação não está aguardando aprovação."));
        if (await ApprovalScopeErrorAsync(actor, mr, ct) is { } scopeError) return (null, scopeError);

        foreach (var item in mr.Items)
        {
            var line = lines?.FirstOrDefault(l => l.ItemId == item.Id);
            var qty = line?.Quantity ?? item.Quantity;
            if (qty < 0) return (null, new("MR-ERR-031", "Quantidade aprovada não pode ser negativa."));
            if (qty > item.Quantity)
                return (null, new("MR-ERR-031",
                    $"{item.Description}: a quantidade aprovada não pode passar da pedida ({item.Quantity:0.##})."));
            item.ApprovedQuantity = qty;
        }
        if (mr.Items.All(i => i.EffectiveQuantity == 0))
            return (null, new("MR-ERR-031", "Aprove ao menos um item com quantidade maior que zero."));

        mr.Status = MaterialRequisitionStatus.Approved;
        mr.ApprovedById = actor.Id;
        mr.ApprovedByLabel = actor.Label;
        mr.ApprovedAt = clock.GetUtcNow();
        mr.DecisionReason = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Touch(mr);
        await db.SaveChangesAsync(ct);
        return (mr, null);
    }

    public async Task<(MaterialRequisition? mr, UserError? error)> RejectAsync(
        Actor actor, Guid id, string? reason, CancellationToken ct = default)
    {
        var mr = await db.MaterialRequisitions.Include(r => r.Items).SingleOrDefaultAsync(r => r.Id == id, ct);
        if (mr is null) return (null, new("MR-ERR-404", "Solicitação não encontrada."));
        if (mr.Status != MaterialRequisitionStatus.Submitted)
            return (null, new("MR-ERR-040", "A solicitação não está aguardando aprovação."));
        if (await ApprovalScopeErrorAsync(actor, mr, ct) is { } scopeError) return (null, scopeError);
        if (string.IsNullOrWhiteSpace(reason))
            return (null, new("MR-ERR-030", "Informe a justificativa da recusa."));

        mr.Status = MaterialRequisitionStatus.Rejected;
        mr.DecisionReason = reason.Trim();
        mr.ApprovedById = actor.Id;
        mr.ApprovedByLabel = actor.Label;
        mr.ApprovedAt = clock.GetUtcNow();
        Touch(mr);
        await db.SaveChangesAsync(ct);
        return (mr, null);
    }

    /// <summary>Quem aprova é quem está no Nível 1 do centro de custo — ou o administrador.</summary>
    private async Task<UserError?> ApprovalScopeErrorAsync(Actor actor, MaterialRequisition mr, CancellationToken ct)
    {
        if (actor.IsAdmin) return null;
        // alçada do centro: qualquer pessoa do Nível 1 resolve a etapa
        if (await ApprovalLevels.CanDecideAsync(db, mr.CostCenter, ApprovalLevels.Level1, actor.Id, ct) is { } noNivel)
            return noNivel
                ? null
                : new("MR-ERR-002", "Esta solicitação é aprovada pelo Nível 1 do centro de custo: " +
                    await ApprovalLevels.LabelAsync(db, mr.CostCenter, ApprovalLevels.Level1, ct) + ".");

        // centro sem Nível 1 cadastrado: vale o responsável antigo do centro
        var cc = await db.CostCenters
            .SingleOrDefaultAsync(c => c.Code.ToUpper() == mr.CostCenter.ToUpper() && c.Active, ct);
        // centro sem responsável definido: qualquer aprovador destrava, para a fila não parar
        if (cc?.ManagerUserId is null) return null;
        return cc.ManagerUserId == actor.Id
            ? null
            : new("MR-ERR-002", $"Esta solicitação é aprovada por {cc.ManagerName ?? "o responsável do centro"}.");
    }

    public record FulfillLine(Guid ItemId, decimal Quantity);

    /// <summary>
    /// Atendimento do estoque: o responsável informa quanto entregou de cada item. O que faltar
    /// vira solicitação de compra em nome do solicitante original, no mesmo centro de custo
    /// (revisão do módulo de estoque, 2026-08-26). A posição de estoque fica no sistema de
    /// almoxarifado da operação — aqui guardamos o atendimento.
    /// </summary>
    public async Task<(MaterialRequisition? mr, UserError? error)> FulfillAsync(
        Actor actor, Guid id, IReadOnlyList<FulfillLine> lines, RequisitionService purchases,
        CancellationToken ct = default)
    {
        var mr = await db.MaterialRequisitions.Include(r => r.Items).SingleOrDefaultAsync(r => r.Id == id, ct);
        if (mr is null) return (null, new("MR-ERR-404", "Solicitação não encontrada."));
        if (mr.Status != MaterialRequisitionStatus.Approved)
            return (null, new("MR-ERR-040", "A solicitação não está liberada para atendimento."));

        foreach (var item in mr.Items)
        {
            var entregue = lines.FirstOrDefault(l => l.ItemId == item.Id)?.Quantity ?? 0;
            if (entregue < 0) return (null, new("MR-ERR-032", "Quantidade entregue não pode ser negativa."));
            if (entregue > item.EffectiveQuantity)
                return (null, new("MR-ERR-032",
                    $"{item.Description}: entregue {entregue:0.##}, mas o aprovado é {item.EffectiveQuantity:0.##}."));
            item.FulfilledQuantity = entregue;
            item.Status = entregue >= item.EffectiveQuantity && entregue > 0 ? MaterialItemStatus.Fulfilled
                : entregue > 0 ? MaterialItemStatus.PartiallyFulfilled
                : MaterialItemStatus.PurchaseRoute;
        }

        // o que faltou vira uma SC no nome de quem pediu, para seguir a alçada do centro
        var faltantes = mr.Items
            .Where(i => i.EffectiveQuantity - i.FulfilledQuantity > 0)
            .Select(i => new ItemInput("", i.EffectiveQuantity - i.FulfilledQuantity, i.UnitOfMeasure,
                null, $"Faltante do atendimento {mr.Number}", i.CatalogItemId))
            .ToList();
        if (faltantes.Count > 0)
        {
            var requester = new Actor(mr.RequesterId, mr.RequesterLabel, Roles.Requester);
            var (pr, prError) = await purchases.CreateAsync(requester,
                $"Reposição do que faltou no atendimento {mr.Number}", mr.CostCenter, "NORMAL", null,
                faltantes, "AVULSA", ct: ct);
            if (prError is not null) return (null, prError);
            var (submitted, submitError) = await purchases.SubmitAsync(requester, pr!.Id, ct);
            if (submitError is not null) return (null, submitError);
            mr.PurchaseRequisitionId = submitted!.Id;
            mr.PurchaseRequisitionNumber = submitted.Number;
        }

        var atendidos = mr.Items.Count(i => i.FulfilledQuantity > 0);
        mr.Status = faltantes.Count == 0 ? MaterialRequisitionStatus.Fulfilled
            : atendidos > 0 ? MaterialRequisitionStatus.PartiallyFulfilled
            : MaterialRequisitionStatus.PurchaseRoute;
        mr.FulfilledBy = actor.Id;
        mr.FulfilledByLabel = actor.Label;
        mr.FulfilledAt = clock.GetUtcNow();
        Touch(mr);
        await db.SaveChangesAsync(ct);
        return (mr, null);
    }

    private void Touch(MaterialRequisition mr)
    {
        mr.UpdatedAt = clock.GetUtcNow();
        mr.Version += 1;
    }

    public async Task<(MaterialRequisition? mr, UserError? error)> CancelAsync(
        Actor actor, Guid id, string? reason, CancellationToken ct = default)
    {
        var mr = await db.MaterialRequisitions.Include(r => r.Items).SingleOrDefaultAsync(r => r.Id == id, ct);
        if (mr is null || (!CanSeeAll(actor.Role) && mr.RequesterId != actor.Id))
            return (null, new("MR-ERR-404", "Solicitação não encontrada."));
        if (mr.RequesterId != actor.Id && actor.Role != Roles.SystemAdministrator)
            return (null, new("MR-ERR-001", "Somente o solicitante pode cancelar."));
        if (mr.Status is not (MaterialRequisitionStatus.Submitted or MaterialRequisitionStatus.Approved))
            return (null, new("MR-ERR-040", "Somente solicitações ainda não atendidas podem ser canceladas."));
        if (string.IsNullOrWhiteSpace(reason))
            return (null, new("MR-ERR-030", "Informe o motivo do cancelamento."));

        mr.Status = MaterialRequisitionStatus.Cancelled;
        mr.CancelReason = reason.Trim();
        mr.UpdatedAt = clock.GetUtcNow();
        mr.Version += 1;
        await db.SaveChangesAsync(ct);
        return (mr, null);
    }

    private async Task<long> NextSeqAsync(CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return await db.MaterialRequisitions.LongCountAsync(ct) + 1;
        return await db.Database
            .SqlQueryRaw<long>("SELECT nextval('materials.mr_number_seq') AS \"Value\"")
            .SingleAsync(ct);
    }
}
