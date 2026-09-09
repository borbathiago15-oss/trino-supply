using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// O fim do ciclo: o registro da O.C. que foi fechada no ERP SENIOR — o sistema nunca
/// a emite (RFQ-ERR-040/041) —, o consumo do contrato do fornecedor e o cancelamento
/// do processo.
/// </summary>
public partial class QuotationService
{
    // ---- registro da OC fechada no ERP (SENIOR) -----------------------------
    /// <summary>
    /// A OC não é mais emitida aqui: ela é fechada no SENIOR (que conversa com o financeiro) e o
    /// comprador registra o número dela no processo, dando origem ao pedido que recebe o
    /// faturamento e a entrega (revisão de telas 2026-08-26).
    /// Compra dividida: uma O.C. POR FORNECEDOR — as famílias que o mesmo fornecedor ganhou
    /// entram na mesma O.C., com a família visível linha a linha. O processo só fecha quando
    /// todas as O.C.s estiverem registradas.
    /// </summary>
    public async Task<(PurchaseOrder? order, UserError? error)> RegisterErpPurchaseOrderAsync(
        Actor actor, Guid id, string? erpNumber, DateOnly? issuedOn, string? notes,
        string? overLimitJustification = null, Guid? supplierId = null, string? noErpReason = null,
        CancellationToken ct = default)
    {
        var q = await GetAsync(id, ct);
        if (q is null) return (null, new("RFQ-ERR-404", "Cotação não encontrada."));
        if (q.Status != QuotationStatus.ApprovedForIssue)
            return (null, new("RFQ-ERR-040", "A OC só é registrada depois das duas aprovações."));

        var numero = erpNumber?.Trim();
        var semOc = string.IsNullOrWhiteSpace(numero);
        var motivo = noErpReason?.Trim();
        if (semOc)
        {
            // a regra é a O.C. do SENIOR: sem ela o processo não fecha. A única
            // exceção é a observação explicando por que não houve O.C. (PO-BR-011)
            if (string.IsNullOrWhiteSpace(motivo) || motivo.Length < 10)
                return (null, new("RFQ-ERR-043",
                    "A O.C. é gerada no ERP e sem ela o processo não fecha. Para fechar assim mesmo, "
                    + "informe na observação, em pelo menos 10 caracteres, por que a O.C. não foi gerada."));
            if (motivo.Length > 500)
                return (null, new("RFQ-ERR-043", "A observação tem no máximo 500 caracteres."));
        }
        else
        {
            motivo = null;
            if (numero!.Length > 30) return (null, new("RFQ-ERR-041", "O número da OC tem no máximo 30 caracteres."));
            // `ErpNumber` junto com `Number`, e não só `Number`: por aqui o pedido nasce
            // com os dois iguais, então olhar só o número pegava a repetição vinda deste
            // mesmo caminho — mas não a que vem da tela do pedido, onde o pedido guarda a
            // própria numeração PO-ano-sequência e o número do SENIOR fica só no
            // `ErpNumber`. A trava do outro lado já existia; esta era de mão única.
            if (await db.PurchaseOrders.AnyAsync(o => o.Number == numero || o.ErpNumber == numero, ct))
                return (null, new("RFQ-ERR-041", $"A OC {numero} já está registrada em outro processo."));
        }

        // processos anteriores à adjudicação por família continuam valendo: viram uma adjudicação única
        await EnsureAwardsAsync(q, ct);
        var pendentes = q.AwardList.Where(a => a.PurchaseOrderId is null).ToList();
        if (pendentes.Count == 0)
            return (null, new("RFQ-ERR-040", "Todas as O.C.s deste processo já foram registradas."));
        var aguardando = pendentes.Select(a => a.SupplierId).Distinct().ToList();
        Guid alvo;
        if (supplierId is { } escolhido)
        {
            if (!aguardando.Contains(escolhido))
                return (null, new("RFQ-ERR-042", "Este fornecedor não tem família pendente de O.C. neste processo."));
            alvo = escolhido;
        }
        else if (aguardando.Count == 1) alvo = aguardando[0];
        else return (null, new("RFQ-ERR-042",
            $"A compra foi dividida entre {aguardando.Count} fornecedores: informe de qual fornecedor é esta O.C."));

        var doFornecedor = pendentes.Where(a => a.SupplierId == alvo).ToList();
        var proposal = q.Proposals.Single(p => p.Id == doFornecedor[0].ProposalId);
        // itens do contrato incluídos: a vigência (ContractIsCurrent) depende deles para o teto
        var supplier = await db.Suppliers.Include(s => s.ContractItems)
            .SingleOrDefaultAsync(s => s.Id == alvo, ct);
        if (supplier is null || !supplier.Active)
            return (null, new("RFQ-ERR-040", "Fornecedor vencedor inativo: regularize o cadastro ou solicite ajustes."));

        var now = clock.GetUtcNow();
        var familias = doFornecedor.Select(a => a.Family).Distinct().OrderBy(f => f).ToList();
        // os itens da O.C. saem do que este fornecedor ganhou de fato. Antes saíam das
        // famílias dele, o que passou a ser diferente quando a mesma família se divide
        // entre dois fornecedores: a O.C. de um levaria também o item que o outro venceu
        var itensDaOc = doFornecedor.SelectMany(a => ItemsCovered(q, a)).Distinct().ToHashSet();
        // sem O.C. do ERP o pedido usa a própria numeração de pedido, a mesma das
        // compras que não vêm de cotação — nada aqui se parece com número do SENIOR
        var referencia = semOc ? await PurchaseOrderService.NextOrderNumberAsync(db, now, ct) : numero!;
        var order = new PurchaseOrder
        {
            Number = referencia,
            ErpNumber = semOc ? null : numero,
            NoErpReason = motivo,
            ErpIssuedOn = issuedOn ?? DateOnly.FromDateTime(now.UtcDateTime),
            // congela a data prometida para o OTIF: data da O.C. + prazo de entrega da proposta
            PromisedDate = proposal.DeliveryDays is { } prazo
                ? (issuedOn ?? DateOnly.FromDateTime(now.UtcDateTime)).AddDays(prazo)
                : null,
            SupplierId = supplier.Id,
            SupplierName = supplier.TradeName ?? supplier.LegalName,
            SourcePrId = q.SourcePrId,
            SourcePrNumber = q.SourcePrNumber,
            QuotationId = q.Id,
            QuotationNumber = q.Number,
            Families = q.Families.Count > 1 ? string.Join(", ", familias) : null,
            PaymentTerms = proposal.PaymentTerms,
            DeliveryDays = proposal.DeliveryDays,
            FreightValue = FreightShare(proposal, itensDaOc),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            IssuedBy = actor.Id,
            IssuedByLabel = actor.Label,
            CreatedAt = now,
            UpdatedAt = now,
        };
        // saving de referência (V2-P2): último preço pago de cada item de catálogo, congelado agora
        var catalogIds = q.Items.Where(i => itensDaOc.Contains(i.Id) && i.CatalogItemId is not null)
            .Select(i => i.CatalogItemId!.Value).Distinct().ToList();
        // o último preço pago vem do histórico, que é onde essa regra passou a morar.
        // Antes a consulta vivia aqui, privada: nada mais no sistema conseguia perguntar
        // "quanto já pagamos por isto?", e era a mesma pergunta
        var historico = await new HistoricoDePrecoService(db).ResumoAsync(catalogIds, ct);

        foreach (var pi in proposal.Items.Where(pi => itensDaOc.Contains(pi.QuotationItemId)))
        {
            var qi = q.Items.Single(x => x.Id == pi.QuotationItemId);
            var ultimo = qi.CatalogItemId is not null && historico.TryGetValue(qi.CatalogItemId.Value, out var h)
                ? h.Ultimo : (decimal?)null;
            order.Items.Add(new PurchaseOrderItem
            {
                Description = qi.Description, UnitOfMeasure = qi.UnitOfMeasure,
                Quantity = pi.Quantity, UnitPrice = pi.UnitPrice,
                CatalogItemId = qi.CatalogItemId, CatalogCode = qi.CatalogCode,
                LastPaidUnitPrice = ultimo,
                ReferenceSaving = ultimo is not null ? (ultimo.Value - pi.UnitPrice) * pi.Quantity : null,
                SourcePrNumber = qi.SourcePrNumber ?? q.SourcePrNumber,
                Family = QuotationAward.FamilyKey(qi.Family),
                CreatedAt = now,
            });
        }
        // o valor da O.C. é a soma das fatias adjudicadas (itens + rateio de frete/impostos/desconto)
        order.TotalValue = doFornecedor.Sum(a => a.TotalValue);
        if (order.Items.Count == 0 || order.TotalValue <= 0)
            return (null, new("RFQ-ERR-040", "A OC precisa de itens e valor."));

        // teto do contrato de parceria (V2-P2): exceder exige justificativa — mede, não trava cego
        if (supplier.ContractValueLimit is { } teto && supplier.ContractIsCurrent(DateOnly.FromDateTime(now.UtcDateTime)))
        {
            var consumido = await ContractConsumedAsync(supplier, ct);
            if (consumido + order.TotalValue > teto && string.IsNullOrWhiteSpace(overLimitJustification))
                return (null, new("CT-ERR-010",
                    $"Esta O.C. ({order.TotalValue:0.00}) ultrapassa o saldo do contrato {supplier.ContractNumber} " +
                    $"(teto {teto:0.00}, consumido {consumido:0.00}). Informe a justificativa para prosseguir."));
            if (consumido + order.TotalValue > teto)
                AddEvent(q, "CONTRATO_TETO_EXCEDIDO",
                    $"O.C. {numero} excede o teto do contrato {supplier.ContractNumber} " +
                    $"({consumido + order.TotalValue:0.00} de {teto:0.00}).", actor, null, null, overLimitJustification!.Trim());
        }
        db.PurchaseOrders.Add(order);
        foreach (var a in doFornecedor) { a.PurchaseOrderId = order.Id; a.PurchaseOrderNumber = order.Number; }

        var from = q.Status;
        q.PurchaseOrderId ??= order.Id;              // a primeira O.C. mantém o vínculo histórico do cabeçalho
        q.PurchaseOrderNumber ??= order.Number;
        var restantes = q.AwardList.Where(a => a.PurchaseOrderId is null).Select(a => a.SupplierName)
            .Distinct().ToList();
        var lote = q.Families.Count > 1 ? $" (família(s) {string.Join(", ", familias)})" : "";
        if (restantes.Count == 0)
        {
            q.Status = QuotationStatus.PoIssued;
            AddEvent(q, "OC_REGISTRADA",
                $"OC {order.Number} do SENIOR registrada para {order.SupplierName}{lote} — total {order.TotalValue:0.00}." +
                (q.IsSplitAward ? " Todas as O.C.s da compra dividida estão registradas." : ""),
                actor, from, q.Status);
        }
        else
        {
            AddEvent(q, "OC_PARCIAL_REGISTRADA",
                $"OC {order.Number} do SENIOR registrada para {order.SupplierName}{lote} — total {order.TotalValue:0.00}. " +
                $"Falta registrar a O.C. de: {string.Join(", ", restantes)}.", actor);
        }
        await TouchAndSaveAsync(q, ct);
        return (order, null);
    }

    /// <summary>
    /// Cotação escolhida antes da adjudicação por família (ou pelo caminho de vencedor único legado):
    /// materializa uma adjudicação por família apontando para o vencedor do cabeçalho, para que o
    /// registro da O.C. tenha sempre a mesma origem.
    /// </summary>
    private async Task EnsureAwardsAsync(Quotation q, CancellationToken ct)
    {
        if (q.AwardList.Count > 0 || q.WinnerProposalId is null) return;
        var proposal = q.Proposals.Single(p => p.Id == q.WinnerProposalId);
        foreach (var familia in q.Families)
        {
            var (valorItens, total) = ShareOf(proposal, ItemsOfFamily(q, familia));
            var award = new QuotationAward
            {
                QuotationId = q.Id, Family = familia,
                SupplierId = proposal.SupplierId, SupplierName = proposal.SupplierName,
                ProposalId = proposal.Id, ProposalVersion = proposal.VersionNumber,
                ItemsValue = valorItens, TotalValue = total,
                Criteria = q.SelectionCriteria, Justification = q.SelectionJustification ?? string.Empty,
                SelectedBy = q.SelectedBy ?? Guid.Empty, SelectedByLabel = q.SelectedByLabel ?? string.Empty,
                SelectedAt = q.SelectedAt ?? clock.GetUtcNow(),
            };
            db.QuotationAwards.Add(award);
            q.Awards.Add(award);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Frete da fatia: proporcional ao valor dos itens que entram nesta O.C.</summary>
    private static decimal? FreightShare(Proposal p, IReadOnlyCollection<Guid> quotationItemIds)
    {
        if (p.FreightValue is not { } frete) return null;
        if (p.Items.All(i => quotationItemIds.Contains(i.QuotationItemId))) return frete;
        var cotado = p.Items.Sum(i => i.UnitPrice * i.Quantity);
        if (cotado <= 0) return frete;
        var fatia = p.Items.Where(i => quotationItemIds.Contains(i.QuotationItemId)).Sum(i => i.UnitPrice * i.Quantity);
        return Math.Round(frete * (fatia / cotado), 2);
    }

    /// <summary>Consumo do contrato: soma das O.C.s não canceladas do fornecedor dentro da vigência.</summary>
    public async Task<decimal> ContractConsumedAsync(Supplier supplier, CancellationToken ct = default)
    {
        var inicio = supplier.ContractValidFrom?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;
        var fim = supplier.ContractValidUntil?.ToDateTime(TimeOnly.MaxValue) ?? DateTime.MaxValue;
        var i0 = new DateTimeOffset(inicio, TimeSpan.Zero);
        var f0 = new DateTimeOffset(fim, TimeSpan.Zero);
        return await db.PurchaseOrders
            .Where(o => o.SupplierId == supplier.Id && o.Status != PurchaseOrderStatus.Cancelled
                        && o.CreatedAt >= i0 && o.CreatedAt <= f0)
            .SumAsync(o => (decimal?)o.TotalValue, ct) ?? 0m;
    }

    public Task<(Quotation? q, UserError? error)> CancelAsync(Actor actor, Guid id, string? reason, CancellationToken ct = default) =>
        TransitionAsync(actor, id, null, QuotationStatus.Cancelled, "COTACAO_CANCELADA",
            "Processo de cotação cancelado.", reason, ct,
            guard: q => q.Status is QuotationStatus.PoIssued or QuotationStatus.Rejected or QuotationStatus.Cancelled
                ? new("RFQ-ERR-020", "Processo já concluído não pode ser cancelado.")
                : string.IsNullOrWhiteSpace(reason) ? new("RFQ-ERR-021", "Informe o motivo do cancelamento.") : null);

    public void RecordPdfEvent(Quotation q, Actor actor, Guid documentId, string poNumber)
        => AddEvent(q, "PDF_GERADO", $"PDF da OC {poNumber} gerado.", actor, null, null, null, documentId);

    /// <summary>Arquiva no histórico a cotação recebida fora do portal (PDF/planilha do fornecedor).</summary>
    public void RecordAttachmentEvent(Quotation q, Actor actor, string supplierName, string fileName)
        => AddEvent(q, "COTACAO_ANEXADA", $"Cotação de {supplierName} anexada ao processo ({fileName}).", actor);
}
