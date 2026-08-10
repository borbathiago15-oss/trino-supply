using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.BuildingBlocks.Security;
using TrinoSupply.Procurement.Application;
using TrinoSupply.Procurement.Domain;
using TrinoSupply.Procurement.Infrastructure.Persistence;

namespace TrinoSupply.Procurement.Infrastructure;

/// <summary>
/// Ordem de Compra (PR-001): emissão a partir de requisição aprovada, selecionando a empresa pagadora
/// (CNPJ responsável) e o fornecedor vencedor da concorrência/BID, com preços por linha. Atribui um
/// número sequencial por tenant. Fonte de dados do PDF da OC.
/// </summary>
public sealed class PurchaseOrderService(
    ProcurementDbContext db, ITenantContext tenant, ICurrentUser currentUser, IUsageMetrics metrics, IClock clock)
    : IPurchaseOrderService
{
    public async Task<Result<Guid>> IssueFromRequisitionAsync(
        Guid requisitionId, IssueOrderInput input, CancellationToken ct = default)
    {
        if (!tenant.HasTenant || string.IsNullOrWhiteSpace(currentUser.Subject))
            return Result.Failure<Guid>(new Error("purchases.no_context", "Requisição sem tenant/usuário."));

        var req = await db.Requisitions.AsNoTracking().Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == RequisitionId.From(requisitionId), ct);
        if (req is null)
            return Result.Failure<Guid>(new Error("purchases.not_found", "Requisição não encontrada."));
        if (req.Status != RequisitionStatus.Approved)
            return Result.Failure<Guid>(new Error("purchases.order.not_approved", "Só requisições aprovadas geram pedido."));

        var payingCode = (input.PayingCompanyCode ?? string.Empty).Trim().ToUpperInvariant();
        var paying = await db.PayingCompanies.AsNoTracking().FirstOrDefaultAsync(pc => pc.Code == payingCode, ct);
        if (paying is null)
            return Result.Failure<Guid>(new Error("purchases.paying_company.not_found", $"Empresa pagadora '{input.PayingCompanyCode}' não encontrada."));

        var supplierCode = (input.SupplierCode ?? string.Empty).Trim().ToUpperInvariant();
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Code == supplierCode, ct);
        if (supplier is null)
            return Result.Failure<Guid>(new Error("purchases.supplier.not_found", $"Fornecedor '{input.SupplierCode}' não encontrado."));

        // Casa os preços informados (vencedor) com as linhas da requisição, por código do item.
        var prices = (input.Lines ?? Array.Empty<IssueOrderLineInput>())
            .GroupBy(l => l.ItemCode.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.Last());

        var lineInputs = new List<OrderLineInput>();
        foreach (var l in req.Lines)
        {
            prices.TryGetValue(l.ItemCode.Trim().ToUpperInvariant(), out var price);
            lineInputs.Add(new OrderLineInput(
                l.ItemCode, l.ItemCode, l.Quantity, l.Unit,
                price?.UnitPrice ?? 0m, price?.IrrfPercent ?? 0m, price?.IssPercent ?? 0m, price?.DeliveryDate));
        }

        var totals = new OrderTotalsInput(
            input.IpiValue, input.IcmsValue, input.DiscountValue, input.OtherExpenses, input.FreightTerms);

        // Número sequencial por tenant. Colisão concorrente é resgatada pelo índice único → tenta de novo.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var maxNumber = await db.Orders.AsNoTracking()
                .Select(o => (long?)o.Number).MaxAsync(ct) ?? 0L;
            var number = maxNumber + 1;

            var order = PurchaseOrder.Issue(
                tenant.CompanyId, number, req.Id, paying.Id, supplier.Id,
                supplier.PaymentTerms, supplier.PaymentMethod, totals, currentUser.Subject!, clock.UtcNow, lineInputs);
            if (order.IsFailure) return Result.Failure<Guid>(order.Error);

            db.Orders.Add(order.Value);
            try
            {
                await db.SaveChangesAsync(ct);
                metrics.Record("purchases.order.issued", tenant.CompanyId.Value.ToString());
                return Result.Success(order.Value.Id.Value);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" } pg)
            {
                db.Entry(order.Value).State = EntityState.Detached;
                foreach (var ln in order.Value.Lines) db.Entry(ln).State = EntityState.Detached;

                // Índice (company, requisition_id): já existe pedido para esta requisição — não adianta repetir.
                if (pg.ConstraintName?.Contains("requisition_id") == true)
                    return Result.Failure<Guid>(new Error("purchases.order.already_exists", "Esta requisição já gerou um pedido."));
                // Índice (company, number): corrida no sequencial — tenta o próximo número.
            }
        }

        return Result.Failure<Guid>(new Error("purchases.order.number_conflict", "Não foi possível reservar o número da OC. Tente novamente."));
    }

    public async Task<Result> CancelAsync(Guid id, string reason, CancellationToken ct = default)
    {
        if (!tenant.HasTenant || string.IsNullOrWhiteSpace(currentUser.Subject))
            return Result.Failure(new Error("purchases.no_context", "Requisição sem tenant/usuário."));

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == PurchaseOrderId.From(id), ct);
        if (order is null)
            return Result.Failure(new Error("purchases.order.not_found", "Pedido não encontrado."));

        var result = order.Cancel(currentUser.Subject!, reason, clock.UtcNow);
        if (result.IsFailure) return result;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(new Error("purchases.conflict", "A OC foi alterada concorrentemente. Recarregue."));
        }

        metrics.Record("purchases.order.cancelled", tenant.CompanyId.Value.ToString());
        return Result.Success();
    }

    public async Task<Result<PurchaseOrderView>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == PurchaseOrderId.From(id), ct);
        if (order is null)
            return Result.Failure<PurchaseOrderView>(new Error("purchases.order.not_found", "Pedido não encontrado."));

        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == order.SupplierId, ct);
        var paying = await db.PayingCompanies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == order.PayingCompanyId, ct);
        return Result.Success(ToView(order, supplier, paying));
    }

    public async Task<IReadOnlyList<PurchaseOrderView>> ListAsync(CancellationToken ct = default)
    {
        var orders = await db.Orders.AsNoTracking().Include(o => o.Lines)
            .OrderByDescending(o => o.Number).ToListAsync(ct);
        var suppliers = (await db.Suppliers.AsNoTracking().ToListAsync(ct)).ToDictionary(s => s.Id.Value);
        var paying = (await db.PayingCompanies.AsNoTracking().ToListAsync(ct)).ToDictionary(p => p.Id.Value);
        return orders.Select(o => ToView(o,
            suppliers.GetValueOrDefault(o.SupplierId.Value),
            paying.GetValueOrDefault(o.PayingCompanyId.Value))).ToList();
    }

    private static PurchaseOrderView ToView(PurchaseOrder o, Supplier? supplier, PayingCompany? paying) => new(
        o.Id.Value, o.Number, o.RequisitionId.Value,
        o.PayingCompanyId.Value, paying?.LegalName ?? string.Empty,
        o.SupplierId.Value, supplier?.Code ?? string.Empty, supplier?.Name ?? string.Empty,
        o.Status.ToString(), o.IssuedBySubject, o.IssuedAt, o.PaymentTerms, o.PaymentMethod,
        o.ProductsValue, o.IpiValue, o.IcmsValue, o.DiscountValue, o.OtherExpenses, o.FreightTerms, o.NetValue,
        o.CancelledBySubject, o.CancelledAt, o.CancelReason,
        o.Lines.Select(l => new OrderLineView(
            l.ItemCode, l.Description, l.Quantity, l.Unit, l.UnitPrice, l.IrrfPercent, l.IssPercent,
            l.ServiceValue, l.IrrfValue, l.IssValue, l.DeliveryDate)).ToList());
}
