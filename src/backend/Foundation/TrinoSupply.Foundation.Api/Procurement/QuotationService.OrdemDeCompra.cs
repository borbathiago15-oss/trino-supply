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
    // ---- o pedido nasce na aprovação; a O.C. do ERP é registrada nele ----------
    /// <summary>
    /// Cria os pedidos do processo — um por fornecedor vencedor, com as famílias que ele
    /// ganhou — no momento da aprovação do Nível 2. Nascem sem O.C. do ERP: registrar o
    /// número (ou a observação da exceção), faturar e receber acontece na tela do pedido,
    /// numa tela só. O processo continua "aprovado para emissão" até todos os pedidos dele
    /// terem O.C. (ou a observação); daí fecha como "O.C. emitida", como sempre fechou.
    /// </summary>
    public async Task<UserError?> CriarPedidosAsync(Quotation q, Actor actor, CancellationToken ct = default)
    {
        await EnsureAwardsAsync(q, ct);
        var pendentes = q.AwardList.Where(a => a.PurchaseOrderId is null).ToList();
        foreach (var fornecedorId in pendentes.Select(a => a.SupplierId).Distinct().ToList())
        {
            var doFornecedor = pendentes.Where(a => a.SupplierId == fornecedorId).ToList();
            var (order, error) = await MontarPedidoAsync(q, doFornecedor, actor, null, ct);
            if (error is not null) return error;
            db.PurchaseOrders.Add(order!);
            foreach (var a in doFornecedor) { a.PurchaseOrderId = order!.Id; a.PurchaseOrderNumber = order.Number; }
            q.PurchaseOrderId ??= order!.Id;              // o primeiro pedido mantém o vínculo histórico do cabeçalho
            q.PurchaseOrderNumber ??= order!.Number;
            AddEvent(q, "PEDIDO_CRIADO",
                $"Pedido {order!.Number} criado para {order.SupplierName} — total {order.TotalValue:0.00}. " +
                "A O.C. do ERP, o faturamento e a entrega são registrados na tela do pedido.", actor);
        }
        return null;
    }

    /// <summary>
    /// Rota antiga do registro pelo processo, mantida por compatibilidade. Com o pedido já
    /// criado na aprovação, ela registra a O.C. <b>no pedido</b> daquele fornecedor, cobrindo
    /// tudo o que falta; num processo aprovado antes desta regra (adjudicação ainda sem
    /// pedido), cria o pedido na hora, como antes.
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
            if (string.IsNullOrWhiteSpace(motivo) || motivo.Length < OcDoErp.MotivoMinimo)
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
            if (await db.PurchaseOrders.AnyAsync(o => o.Number == numero || o.ErpNumber == numero, ct)
                || await db.PurchaseOrderErpDocuments.AnyAsync(d => d.Number == numero, ct))
                return (null, new("RFQ-ERR-041", $"A OC {numero} já está registrada em outro processo."));
        }

        // processos anteriores à adjudicação por família continuam valendo: viram uma adjudicação única
        await EnsureAwardsAsync(q, ct);
        var semPedido = q.AwardList.Where(a => a.PurchaseOrderId is null).ToList();
        if (semPedido.Count == 0)
        {
            // o caminho de agora: os pedidos existem desde a aprovação; falta a O.C. em algum deles
            var pedidosDoProcesso = await db.PurchaseOrders.Include(o => o.Items).Include(o => o.Invoices)
                .Include(o => o.ErpDocuments).ThenInclude(d => d.Items)
                .Where(o => o.QuotationId == q.Id && o.Status != PurchaseOrderStatus.Cancelled).ToListAsync(ct);
            var aguardando = pedidosDoProcesso.Where(o => o.ErpNumber is null && o.NoErpReason is null).ToList();
            if (aguardando.Count == 0)
                return (null, new("RFQ-ERR-040", "Todas as O.C.s deste processo já foram registradas."));
            PurchaseOrder alvo;
            if (supplierId is { } escolhido)
            {
                var doEscolhido = aguardando.FirstOrDefault(o => o.SupplierId == escolhido);
                if (doEscolhido is null)
                    return (null, new("RFQ-ERR-042", "Este fornecedor não tem família pendente de O.C. neste processo."));
                alvo = doEscolhido;
            }
            else if (aguardando.Count == 1) alvo = aguardando[0];
            else return (null, new("RFQ-ERR-042",
                $"A compra foi dividida entre {aguardando.Count} fornecedores: informe de qual fornecedor é esta O.C."));

            var fornecedorAtivo = await db.Suppliers.AnyAsync(s => s.Id == alvo.SupplierId && s.Active, ct);
            if (!fornecedorAtivo)
                return (null, new("RFQ-ERR-040", "Fornecedor vencedor inativo: regularize o cadastro ou solicite ajustes."));

            if (!string.IsNullOrWhiteSpace(notes)) alvo.Notes = notes.Trim();
            var (_, erro) = await OcDoErp.AplicarAsync(db, clock, alvo, actor, numero, issuedOn, motivo,
                null, overLimitJustification, "RFQ-ERR-043", ct);
            if (erro is not null) return (null, erro);
            await OcDoErp.SincronizarProcessoAsync(db, clock, alvo, actor, ct);
            alvo.UpdatedAt = clock.GetUtcNow();
            alvo.Version += 1;
            await db.SaveChangesAsync(ct);
            return (alvo, null);
        }

        // o caminho antigo: processo aprovado antes de o pedido nascer na aprovação
        var esperando = semPedido.Select(a => a.SupplierId).Distinct().ToList();
        Guid fornecedorAlvo;
        if (supplierId is { } pedido)
        {
            if (!esperando.Contains(pedido))
                return (null, new("RFQ-ERR-042", "Este fornecedor não tem família pendente de O.C. neste processo."));
            fornecedorAlvo = pedido;
        }
        else if (esperando.Count == 1) fornecedorAlvo = esperando[0];
        else return (null, new("RFQ-ERR-042",
            $"A compra foi dividida entre {esperando.Count} fornecedores: informe de qual fornecedor é esta O.C."));

        var doFornecedor = semPedido.Where(a => a.SupplierId == fornecedorAlvo).ToList();
        var (order, error) = await MontarPedidoAsync(q, doFornecedor, actor, notes, ct);
        if (error is not null) return (null, error);
        db.PurchaseOrders.Add(order!);
        foreach (var a in doFornecedor) { a.PurchaseOrderId = order!.Id; a.PurchaseOrderNumber = order.Number; }
        q.PurchaseOrderId ??= order!.Id;
        q.PurchaseOrderNumber ??= order!.Number;
        var (_, erroOc) = await OcDoErp.AplicarAsync(db, clock, order!, actor, numero, issuedOn, motivo,
            null, overLimitJustification, "RFQ-ERR-043", ct);
        if (erroOc is not null) return (null, erroOc);
        await OcDoErp.SincronizarProcessoAsync(db, clock, order!, actor, ct);
        await TouchAndSaveAsync(q, ct);
        return (order, null);
    }

    /// <summary>
    /// Monta o pedido de um fornecedor a partir do que ele ganhou: os itens (na quantidade
    /// adjudicada, não na cotada), o rateio do frete, a condição e o prazo da proposta, o
    /// último preço pago de cada item (saving de referência) e o total das fatias. Sem O.C.
    /// do ERP: ela é registrada depois, na tela do pedido. Não grava.
    /// </summary>
    private async Task<(PurchaseOrder? order, UserError? error)> MontarPedidoAsync(
        Quotation q, IReadOnlyList<QuotationAward> doFornecedor, Actor actor, string? notes, CancellationToken ct)
    {
        var proposal = q.Proposals.Single(p => p.Id == doFornecedor[0].ProposalId);
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == doFornecedor[0].SupplierId, ct);
        if (supplier is null || !supplier.Active)
            return (null, new("RFQ-ERR-040", "Fornecedor vencedor inativo: regularize o cadastro ou solicite ajustes."));

        var now = clock.GetUtcNow();
        var familias = doFornecedor.Select(a => a.Family).Distinct().OrderBy(f => f).ToList();
        // os itens da O.C. saem do que este fornecedor ganhou de fato. Antes saíam das
        // famílias dele, o que passou a ser diferente quando a mesma família se divide
        // entre dois fornecedores: a O.C. de um levaria também o item que o outro venceu
        var itensDaOc = doFornecedor.SelectMany(a => ItemsCovered(q, a)).Distinct().ToHashSet();
        // quanto de cada item saiu com ESTE fornecedor: com o item partido, a O.C. dele
        // leva a fatia dele, e não a quantidade que ele cotou
        var quantidades = QuantidadesDoFornecedor(q, doFornecedor);
        var order = new PurchaseOrder
        {
            // o pedido usa a própria numeração; o número do SENIOR entra depois, em `ErpNumber`
            Number = await PurchaseOrderService.NextOrderNumberAsync(db, now, ct),
            SupplierId = supplier.Id,
            SupplierName = supplier.TradeName ?? supplier.LegalName,
            SourcePrId = q.SourcePrId,
            SourcePrNumber = q.SourcePrNumber,
            QuotationId = q.Id,
            QuotationNumber = q.Number,
            Families = q.Families.Count > 1 ? string.Join(", ", familias) : null,
            PaymentTerms = proposal.PaymentTerms,
            DeliveryDays = proposal.DeliveryDays,
            FreightValue = FreightShare(proposal, itensDaOc, quantidades),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            IssuedBy = actor.Id,
            IssuedByLabel = actor.Label,
            CreatedAt = now,
            UpdatedAt = now,
        };
        // saving de referência (V2-P2): último preço pago de cada item de catálogo, congelado agora
        var catalogIds = q.Items.Where(i => itensDaOc.Contains(i.Id) && i.CatalogItemId is not null)
            .Select(i => i.CatalogItemId!.Value).Distinct().ToList();
        var historico = await new HistoricoDePrecoService(db).ResumoAsync(catalogIds, ct);

        foreach (var pi in proposal.Items.Where(pi => itensDaOc.Contains(pi.QuotationItemId)))
        {
            var qi = q.Items.Single(x => x.Id == pi.QuotationItemId);
            var ultimo = qi.CatalogItemId is not null && historico.TryGetValue(qi.CatalogItemId.Value, out var h)
                ? h.Ultimo : (decimal?)null;
            // a quantidade da O.C. é a adjudicada, não a cotada: o fornecedor cota o lote
            // inteiro para dar preço, e pode ter ganhado só parte dele
            var quantidade = quantidades.GetValueOrDefault(pi.QuotationItemId, pi.Quantity);
            order.Items.Add(new PurchaseOrderItem
            {
                Description = qi.Description, UnitOfMeasure = qi.UnitOfMeasure,
                Quantity = quantidade, UnitPrice = pi.UnitPrice,
                CatalogItemId = qi.CatalogItemId, CatalogCode = qi.CatalogCode,
                LastPaidUnitPrice = ultimo,
                ReferenceSaving = ultimo is not null ? (ultimo.Value - pi.UnitPrice) * quantidade : null,
                SourcePrNumber = qi.SourcePrNumber ?? q.SourcePrNumber,
                Family = QuotationAward.FamilyKey(qi.Family),
                CreatedAt = now,
            });
        }
        // o valor da O.C. é a soma das fatias adjudicadas (itens + rateio de frete/impostos/desconto)
        order.TotalValue = doFornecedor.Sum(a => a.TotalValue);
        if (order.Items.Count == 0 || order.TotalValue <= 0)
            return (null, new("RFQ-ERR-040", "A OC precisa de itens e valor."));
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
    private static decimal? FreightShare(
        Proposal p, IReadOnlyCollection<Guid> quotationItemIds,
        IReadOnlyDictionary<Guid, decimal>? quantidades = null)
    {
        if (p.FreightValue is not { } frete) return null;
        decimal Quanto(ProposalItem i) =>
            quantidades is not null && quantidades.TryGetValue(i.QuotationItemId, out var q) ? q : i.Quantity;
        // frete inteiro só para quem levou tudo — inclusive a quantidade toda de cada item.
        // Com o item partido, dar o frete cheio aos dois cobraria o mesmo frete duas vezes
        if (p.Items.All(i => quotationItemIds.Contains(i.QuotationItemId) && Quanto(i) == i.Quantity)) return frete;
        var cotado = p.Items.Sum(i => i.UnitPrice * i.Quantity);
        if (cotado <= 0) return frete;
        var fatia = p.Items.Where(i => quotationItemIds.Contains(i.QuotationItemId))
            .Sum(i => i.UnitPrice * Quanto(i));
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
