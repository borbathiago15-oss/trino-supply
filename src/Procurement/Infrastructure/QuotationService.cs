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
/// Cotação (RFQ — Fase 03): abertura a partir da requisição aprovada, recebimento das propostas,
/// mapa de equalização e adjudicação por item, terminando na emissão das OCs.
/// </summary>
public sealed class QuotationService(
    ProcurementDbContext db, ITenantContext tenant, ICurrentUser currentUser, IClock clock,
    IPurchaseOrderService orders, ISupplierScorecardService scorecards,
    TrinoSupply.Foundation.Infrastructure.Audit.IBusinessAudit audit)
    : IQuotationService
{
    /// <summary>Acima disso a proposta acende o alerta de sobrepreço contra o último preço pago.</summary>
    private const decimal OverpriceThresholdPercent = 15m;

    public async Task<Result<Guid>> OpenAsync(Guid requisitionId, OpenQuotationInput input, CancellationToken ct = default)
    {
        if (!tenant.HasTenant || string.IsNullOrWhiteSpace(currentUser.Subject))
            return Result.Failure<Guid>(new Error("purchases.no_context", "Requisição sem tenant/usuário."));

        var req = await db.Requisitions.AsNoTracking().Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == RequisitionId.From(requisitionId), ct);
        if (req is null)
            return Result.Failure<Guid>(new Error("purchases.not_found", "Requisição não encontrada."));
        if (req.Status is not (RequisitionStatus.Approved or RequisitionStatus.PartiallyOrdered))
            return Result.Failure<Guid>(new Error("purchases.quotation.not_approved",
                "Só requisições aprovadas (ou parcialmente atendidas) vão a cotação."));

        // Só item ainda sem OC entra em concorrência — o que já foi comprado não se cota de novo.
        var pedidos = new HashSet<Guid>(input.RequisitionLineIds ?? Array.Empty<Guid>());
        var candidatas = req.Lines.Where(l => pedidos.Count == 0 || pedidos.Contains(l.Id)).ToList();
        var jaPedida = candidatas.FirstOrDefault(l => !l.IsPending);
        if (jaPedida is not null)
            return Result.Failure<Guid>(new Error("purchases.quotation.line_already_ordered",
                $"O item '{jaPedida.ItemCode}' já está em uma OC — não pode ir a cotação."));
        if (candidatas.Count == 0)
            return Result.Failure<Guid>(new Error("purchases.quotation.lines_required",
                "Selecione ao menos um item pendente para cotar."));

        // Um item só pode estar em uma concorrência viva por vez (senão duas cotações adjudicam o mesmo).
        var emCotacao = await LinhasEmCotacaoAbertaAsync(ct);
        var duplicada = candidatas.FirstOrDefault(l => emCotacao.Contains(l.Id));
        if (duplicada is not null)
            return Result.Failure<Guid>(new Error("purchases.quotation.line_in_open_quotation",
                $"O item '{duplicada.ItemCode}' já está em uma cotação aberta."));

        var codes = (input.SupplierCodes ?? Array.Empty<string>())
            .Select(c => (c ?? string.Empty).Trim().ToUpperInvariant()).Where(c => c.Length > 0).Distinct().ToList();
        var fornecedores = await db.Suppliers.AsNoTracking().Where(s => codes.Contains(s.Code)).ToListAsync(ct);
        var faltando = codes.Except(fornecedores.Select(s => s.Code)).ToList();
        if (faltando.Count > 0)
            return Result.Failure<Guid>(new Error("purchases.supplier.not_found",
                $"Fornecedor não encontrado: {string.Join(", ", faltando)}."));

        var linhas = candidatas
            .Select(l => new QuotationLineInput(l.Id, l.ItemCode, l.Quantity, l.Unit))
            .ToList();

        // Número sequencial por tenant. Colisão concorrente é resgatada pelo índice único → tenta de novo.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var maxNumber = await db.Quotations.AsNoTracking().Select(q => (long?)q.Number).MaxAsync(ct) ?? 0L;

            var quotation = Quotation.Open(
                tenant.CompanyId, maxNumber + 1, req.Id, currentUser.Subject!, clock.UtcNow,
                input.ClosesAt, input.Notes, linhas, fornecedores.Select(s => s.Id.Value));
            if (quotation.IsFailure) return Result.Failure<Guid>(quotation.Error);

            db.Quotations.Add(quotation.Value);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                Detach(quotation.Value);
                continue;
            }

            await audit.RecordAsync("purchases.quotation.opened", "Quotation", quotation.Value.Id.Value.ToString(),
                new
                {
                    number = quotation.Value.Number, requisitionId,
                    itens = linhas.Count, convidados = fornecedores.Count, closesAt = input.ClosesAt,
                }, ct);
            return Result.Success(quotation.Value.Id.Value);
        }

        return Result.Failure<Guid>(new Error("purchases.quotation.number_conflict",
            "Não foi possível reservar o número da cotação. Tente novamente."));
    }

    public async Task<Result> SubmitProposalAsync(Guid quotationId, SubmitProposalInput input, CancellationToken ct = default)
    {
        var quotation = await LoadAsync(quotationId, ct);
        if (quotation is null)
            return Result.Failure(new Error("purchases.quotation.not_found", "Cotação não encontrada."));

        var code = (input.SupplierCode ?? string.Empty).Trim().ToUpperInvariant();
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Code == code, ct);
        if (supplier is null)
            return Result.Failure(new Error("purchases.supplier.not_found", $"Fornecedor '{input.SupplierCode}' não encontrado."));

        var header = new ProposalHeader(input.PaymentTerms, input.FreightTerms, input.ValidUntil, input.Notes);
        var bids = (input.Bids ?? Array.Empty<ProposalBidInput>())
            .Select(b => new BidInput(b.LineId, b.UnitPrice, b.DeliveryDays, b.Notes)).ToList();

        var antes = quotation.Bids.Select(b => b.Id).ToHashSet();
        var result = quotation.SubmitProposal(supplier.Id.Value, header, bids, clock.UtcNow);
        if (result.IsFailure) return result;

        // O agregado já está rastreado, e as ofertas nascem do domínio com a PK preenchida — sem
        // isso o EF as interpretaria como UPDATE de linha inexistente. As substituídas saem da
        // coleção e o EF as marca como excluídas (recotação apaga a proposta anterior inteira).
        foreach (var novo in quotation.Bids.Where(b => !antes.Contains(b.Id)))
            db.Entry(novo).State = EntityState.Added;

        if (!await SaveAsync(ct)) return Conflict();

        await audit.RecordAsync("purchases.quotation.proposal_submitted", "Quotation", quotationId.ToString(),
            new { number = quotation.Number, supplier = supplier.Code, itens = bids.Count }, ct);
        return Result.Success();
    }

    public async Task<Result> AwardAsync(Guid quotationId, AwardQuotationInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(currentUser.Subject))
            return Result.Failure(new Error("purchases.no_context", "Requisição sem tenant/usuário."));

        var quotation = await LoadAsync(quotationId, ct);
        if (quotation is null)
            return Result.Failure(new Error("purchases.quotation.not_found", "Cotação não encontrada."));

        var pedidos = (input.Awards ?? Array.Empty<AwardLineInput>()).ToList();
        var codes = pedidos.Select(a => (a.SupplierCode ?? string.Empty).Trim().ToUpperInvariant()).Distinct().ToList();
        var fornecedores = (await db.Suppliers.AsNoTracking().Where(s => codes.Contains(s.Code)).ToListAsync(ct))
            .ToDictionary(s => s.Code, s => s.Id.Value);

        var awards = new List<AwardInput>(pedidos.Count);
        foreach (var a in pedidos)
        {
            var code = (a.SupplierCode ?? string.Empty).Trim().ToUpperInvariant();
            if (!fornecedores.TryGetValue(code, out var supplierId))
                return Result.Failure(new Error("purchases.supplier.not_found", $"Fornecedor '{a.SupplierCode}' não encontrado."));
            awards.Add(new AwardInput(a.LineId, supplierId, a.Note));
        }

        var result = quotation.Award(awards, currentUser.Subject!, clock.UtcNow);
        if (result.IsFailure) return result;

        if (!await SaveAsync(ct)) return Conflict();

        await audit.RecordAsync("purchases.quotation.awarded", "Quotation", quotationId.ToString(),
            new
            {
                number = quotation.Number, itens = awards.Count, situacao = quotation.Status.ToString(),
                // A exceção ao menor preço é o que um auditor procura: fica explícita no log.
                comJustificativa = quotation.Lines.Count(l => l.AwardNote is not null),
            }, ct);
        return Result.Success();
    }

    public async Task<Result> CancelAsync(Guid quotationId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(currentUser.Subject))
            return Result.Failure(new Error("purchases.no_context", "Requisição sem tenant/usuário."));

        var quotation = await LoadAsync(quotationId, ct);
        if (quotation is null)
            return Result.Failure(new Error("purchases.quotation.not_found", "Cotação não encontrada."));

        var result = quotation.Cancel(currentUser.Subject!, reason, clock.UtcNow);
        if (result.IsFailure) return result;

        if (!await SaveAsync(ct)) return Conflict();

        await audit.RecordAsync("purchases.quotation.cancelled", "Quotation", quotationId.ToString(),
            new { number = quotation.Number, reason }, ct);
        return Result.Success();
    }

    /// <summary>
    /// Fecha o ciclo: cada fornecedor vencedor recebe uma OC com os itens que ganhou, no preço
    /// congelado na adjudicação e com a data de entrega derivada do prazo que ele mesmo prometeu —
    /// que é exatamente o que o scorecard OTIF vai cobrar dele no recebimento.
    /// </summary>
    public async Task<Result<QuotationOrdersResult>> IssueOrdersAsync(Guid quotationId, CancellationToken ct = default)
    {
        var quotation = await LoadAsync(quotationId, ct);
        if (quotation is null)
            return Result.Failure<QuotationOrdersResult>(new Error("purchases.quotation.not_found", "Cotação não encontrada."));
        if (quotation.Status == QuotationStatus.Cancelled)
            return Result.Failure<QuotationOrdersResult>(new Error("purchases.quotation.cancelled", "Esta cotação foi cancelada."));

        var req = await db.Requisitions.AsNoTracking().Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == quotation.RequisitionId, ct);
        if (req is null)
            return Result.Failure<QuotationOrdersResult>(new Error("purchases.not_found", "Requisição não encontrada."));

        var paying = await db.PayingCompanies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == req.PayingCompanyId, ct);
        if (paying is null)
            return Result.Failure<QuotationOrdersResult>(
                new Error("purchases.paying_company.not_found", "Empresa pagadora da requisição não encontrada."));

        // Só adjudicação cujo item continua sem OC — reemitir não duplica pedido.
        var pendentes = new HashSet<Guid>(req.Lines.Where(l => l.IsPending).Select(l => l.Id));
        var adjudicadas = quotation.Lines
            .Where(l => l.IsAwarded && pendentes.Contains(l.RequisitionLineId))
            .ToList();
        if (adjudicadas.Count == 0)
            return Result.Failure<QuotationOrdersResult>(new Error("purchases.quotation.nothing_to_order",
                "Não há item adjudicado pendente de OC nesta cotação."));

        var supplierCodes = (await db.Suppliers.AsNoTracking().ToListAsync(ct))
            .ToDictionary(s => s.Id.Value, s => s.Code);
        var prazoPorLinha = PrazoPorLinha(quotation);

        var criadas = new List<Guid>();
        foreach (var grupo in adjudicadas.GroupBy(l => l.AwardedSupplierId!.Value))
        {
            if (!supplierCodes.TryGetValue(grupo.Key, out var supplierCode)) continue;

            var lines = grupo.Select(l => new IssueOrderLineInput(
                l.ItemCode, l.AwardedUnitPrice ?? 0m,
                DeliveryDate: prazoPorLinha.TryGetValue(l.Id, out var dias) && dias is not null
                    ? clock.UtcNow.AddDays(dias.Value)
                    : null)).ToList();

            var issued = await orders.IssueFromRequisitionAsync(
                req.Id.Value, new IssueOrderInput(paying.Code, supplierCode, lines), ct);
            if (issued.IsFailure) return Result.Failure<QuotationOrdersResult>(issued.Error);
            criadas.Add(issued.Value);
        }

        await audit.RecordAsync("purchases.quotation.orders_issued", "Quotation", quotationId.ToString(),
            new { number = quotation.Number, ocs = criadas.Count, itens = adjudicadas.Count }, ct);
        return Result.Success(new QuotationOrdersResult(criadas));
    }

    public async Task<Result<QuotationView>> GetAsync(Guid quotationId, CancellationToken ct = default)
    {
        var q = await db.Quotations.AsNoTracking()
            .Include(x => x.Lines).Include(x => x.Participants).Include(x => x.Bids)
            .FirstOrDefaultAsync(x => x.Id == QuotationId.From(quotationId), ct);
        if (q is null)
            return Result.Failure<QuotationView>(new Error("purchases.quotation.not_found", "Cotação não encontrada."));

        var suppliers = (await db.Suppliers.AsNoTracking().ToListAsync(ct)).ToDictionary(s => s.Id.Value);
        var ultimoPreco = await UltimoPrecoPagoAsync(q.Lines.Select(l => l.ItemCode).Distinct().ToList(), ct);
        var otif = (await scorecards.ListAsync(12, ct)).ToDictionary(s => s.SupplierId);

        var linhas = q.Lines.Select(line =>
        {
            var doItem = q.Bids.Where(b => b.LineId == line.Id).ToList();
            var menor = doItem.Count > 0 ? doItem.Min(b => b.UnitPrice) : 0m;
            ultimoPreco.TryGetValue(line.ItemCode, out var referencia);

            var bids = doItem.Select(b =>
            {
                var participant = q.Participants.First(p => p.Id == b.ParticipantId);
                suppliers.TryGetValue(participant.SupplierId, out var s);
                var acimaDoUltimo = referencia.Price is > 0
                    ? Round((b.UnitPrice - referencia.Price.Value) / referencia.Price.Value * 100m)
                    : (decimal?)null;

                return new QuotationBidView(
                    participant.SupplierId, s?.Code ?? string.Empty, s?.Name ?? string.Empty,
                    b.UnitPrice, Round(b.UnitPrice * line.Quantity), b.DeliveryDays, b.Notes,
                    IsLowest: menor > 0 && b.UnitPrice == menor,
                    IsLate: participant.IsLate,
                    PercentAboveLowest: menor > 0 ? Round((b.UnitPrice - menor) / menor * 100m) : 0m,
                    PercentAboveLastPaid: acimaDoUltimo,
                    OverpriceAlert: acimaDoUltimo > OverpriceThresholdPercent);
            }).OrderBy(b => b.UnitPrice).ThenBy(b => b.SupplierName, StringComparer.OrdinalIgnoreCase).ToList();

            string? vencedorCode = null;
            if (line.AwardedSupplierId is not null && suppliers.TryGetValue(line.AwardedSupplierId.Value, out var v))
                vencedorCode = v.Code;

            return new QuotationLineView(
                line.Id, line.RequisitionLineId, line.ItemCode, line.Quantity, line.Unit,
                line.AwardedSupplierId, vencedorCode, line.AwardedUnitPrice, line.AwardNote,
                referencia.Price, referencia.At, bids);
        }).ToList();

        var participantes = q.Participants.Select(p =>
        {
            suppliers.TryGetValue(p.SupplierId, out var s);
            var seus = q.Bids.Where(b => b.ParticipantId == p.Id).ToList();
            var total = seus.Sum(b => b.UnitPrice * (q.Lines.FirstOrDefault(l => l.Id == b.LineId)?.Quantity ?? 0m));
            otif.TryGetValue(p.SupplierId, out var nota);

            return new QuotationParticipantView(
                p.SupplierId, s?.Code ?? string.Empty, s?.Name ?? string.Empty,
                p.HasResponded, p.IsLate, p.RespondedAt, p.PaymentTerms, p.FreightTerms, p.ValidUntil, p.Notes,
                seus.Count, Round(total),
                nota?.Tier, nota is null ? null : nota.OtifIndex);
        }).OrderByDescending(p => p.HasResponded).ThenBy(p => p.SupplierName, StringComparer.OrdinalIgnoreCase).ToList();

        return Result.Success(new QuotationView(
            q.Id.Value, q.Number, q.RequisitionId.Value, q.Status.ToString(), q.CreatedBySubject,
            q.CreatedAt, q.ClosesAt, clock.UtcNow > q.ClosesAt, q.Notes,
            q.CancelledBySubject, q.CancelledAt, q.CancelReason, linhas, participantes));
    }

    public async Task<IReadOnlyList<QuotationSummaryView>> ListAsync(int limit = 200, CancellationToken ct = default)
    {
        var agora = clock.UtcNow;
        var list = await db.Quotations.AsNoTracking()
            .Include(x => x.Lines).Include(x => x.Participants)
            .OrderByDescending(x => x.Number).Take(Math.Clamp(limit, 1, 1000)).ToListAsync(ct);

        return list.Select(q => new QuotationSummaryView(
            q.Id.Value, q.Number, q.RequisitionId.Value, q.Status.ToString(), q.CreatedAt, q.ClosesAt,
            agora > q.ClosesAt, q.Lines.Count, q.Participants.Count, q.ResponseCount,
            q.Lines.Count(l => l.IsAwarded))).ToList();
    }

    // ---- apoio ------------------------------------------------------------------------------

    private async Task<Quotation?> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Quotations.Include(x => x.Lines).Include(x => x.Participants).Include(x => x.Bids)
            .FirstOrDefaultAsync(x => x.Id == QuotationId.From(id), ct);

    private async Task<bool> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private static Result Conflict() =>
        Result.Failure(new Error("purchases.conflict", "A cotação foi alterada concorrentemente. Recarregue."));

    private void Detach(Quotation q)
    {
        db.Entry(q).State = EntityState.Detached;
        foreach (var l in q.Lines) db.Entry(l).State = EntityState.Detached;
        foreach (var p in q.Participants) db.Entry(p).State = EntityState.Detached;
        foreach (var b in q.Bids) db.Entry(b).State = EntityState.Detached;
    }

    /// <summary>Linhas de requisição presas a alguma cotação ainda viva (aberta ou parcialmente adjudicada).</summary>
    private async Task<HashSet<Guid>> LinhasEmCotacaoAbertaAsync(CancellationToken ct)
    {
        var vivas = await db.Quotations.AsNoTracking().Include(q => q.Lines)
            .Where(q => q.Status == QuotationStatus.Open || q.Status == QuotationStatus.PartiallyAwarded)
            .ToListAsync(ct);
        return vivas.SelectMany(q => q.Lines)
            .Where(l => !l.IsAwarded)   // item já adjudicado saiu da disputa
            .Select(l => l.RequisitionLineId).ToHashSet();
    }

    /// <summary>
    /// Último preço unitário efetivamente pago por item, em OC não cancelada — a referência interna
    /// contra a qual o sobrepreço é medido (vale mais que tabela de preço: é o que a empresa pagou).
    /// </summary>
    private async Task<Dictionary<string, (decimal? Price, DateTimeOffset? At)>> UltimoPrecoPagoAsync(
        IReadOnlyCollection<string> itemCodes, CancellationToken ct)
    {
        if (itemCodes.Count == 0) return new Dictionary<string, (decimal?, DateTimeOffset?)>();

        var historico = await db.Orders.AsNoTracking().Include(o => o.Lines)
            .Where(o => o.Status != PurchaseOrderStatus.Cancelled)
            .SelectMany(o => o.Lines.Select(l => new { l.ItemCode, l.UnitPrice, o.IssuedAt }))
            .Where(x => itemCodes.Contains(x.ItemCode) && x.UnitPrice > 0)
            .ToListAsync(ct);

        return historico.GroupBy(x => x.ItemCode).ToDictionary(
            g => g.Key,
            g =>
            {
                var ultima = g.OrderByDescending(x => x.IssuedAt).First();
                return ((decimal?)ultima.UnitPrice, (DateTimeOffset?)ultima.IssuedAt);
            });
    }

    /// <summary>Prazo prometido pelo vencedor de cada linha adjudicada (dias), para virar data na OC.</summary>
    private static Dictionary<Guid, int?> PrazoPorLinha(Quotation q)
    {
        var map = new Dictionary<Guid, int?>();
        foreach (var line in q.Lines.Where(l => l.IsAwarded))
        {
            var participant = q.Participants.FirstOrDefault(p => p.SupplierId == line.AwardedSupplierId!.Value);
            var bid = participant is null
                ? null
                : q.Bids.FirstOrDefault(b => b.ParticipantId == participant.Id && b.LineId == line.Id);
            map[line.Id] = bid?.DeliveryDays;
        }
        return map;
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
