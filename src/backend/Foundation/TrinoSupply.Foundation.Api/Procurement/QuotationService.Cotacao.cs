using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// Abertura do processo, convite aos fornecedores, propostas e negociação — a parte do
/// serviço que vai de "existe uma SC" até "há propostas para comparar".
///
/// Saiu do arquivo único no ARQ-B. Mesma classe, mesmos métodos, mesma ordem: partial
/// não muda comportamento nenhum, só permite achar o método pelo nome do arquivo.
/// </summary>
public partial class QuotationService
{
    // ---- abertura (RFQ-BR-001/002) ------------------------------------------
    public async Task<(Quotation? q, UserError? error)> CreateFromPrAsync(
        Actor actor, Guid prId, QuotationKind kind, DateOnly? deadline, string? notes, CancellationToken ct = default)
    {
        var pr = await db.Requisitions.Include(r => r.Items).SingleOrDefaultAsync(r => r.Id == prId, ct);
        if (pr is null || pr.Status is not (RequisitionStatus.Submitted or RequisitionStatus.Approved))
            return (null, new("RFQ-ERR-001", "A requisição de origem precisa estar enviada (ou aprovada) para virar cotação."));
        if (pr.Items.Count == 0)
            return (null, new("RFQ-ERR-001", "A requisição não possui itens."));
        // processo do fluxo antigo (sem rastreio por item) segura a SC inteira
        if (await db.Quotations.AnyAsync(q => q.SourcePrId == prId && q.Items.All(i => i.SourcePrItemId == null)
                && q.Status != QuotationStatus.Cancelled && q.Status != QuotationStatus.Rejected, ct))
            return (null, new("RFQ-ERR-001", "A requisição já possui um processo de cotação ativo."));
        // separação por item: o que já está em outro processo fica de fora, e o resto vem para cá
        var jaCotados = (await db.Quotations
            .Where(q => q.Status != QuotationStatus.Cancelled && q.Status != QuotationStatus.Rejected)
            .SelectMany(q => q.Items).Where(i => i.SourcePrId == prId && i.SourcePrItemId != null)
            .Select(i => i.SourcePrItemId!.Value).ToListAsync(ct)).ToHashSet();
        var itens = pr.Items.Where(i => !jaCotados.Contains(i.Id)).OrderBy(i => i.Sequence).ToList();
        if (itens.Count == 0)
            return (null, new("RFQ-ERR-001", "Todos os itens desta requisição já estão em processos de cotação."));

        var now = clock.GetUtcNow();
        var prefix = kind == QuotationKind.Bid ? "BID" : "RFQ";
        var seq = await NextSeqAsync(kind, ct);
        var q = new Quotation
        {
            Number = $"{prefix}-{now.Year}-{seq:000000}",
            Kind = kind,
            SourcePrId = pr.Id,
            SourcePrNumber = pr.Number,
            CostCenter = pr.CostCenter,
            Justification = pr.Justification,
            Deadline = deadline,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedBy = actor.Id,
            CreatedByLabel = actor.Label,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var familias = await FamiliesOfAsync(itens.Select(i => i.CatalogItemId), ct);
        var seqNo = 0;
        foreach (var i in itens)
            q.Items.Add(new QuotationItem
            {
                QuotationId = q.Id, Sequence = ++seqNo, CatalogItemId = i.CatalogItemId,
                CatalogCode = i.CatalogCode, Description = i.Description,
                Quantity = i.Quantity, UnitOfMeasure = i.UnitOfMeasure, Family = FamilyOf(i, familias),
                SourcePrId = pr.Id, SourcePrNumber = pr.Number, SourcePrItemId = i.Id,
            });
        db.Quotations.Add(q);
        AddEvent(q, "COTACAO_ABERTA", itens.Count == pr.Items.Count
                ? $"Cotação {q.Number} aberta a partir da requisição {pr.Number}."
                : $"Cotação {q.Number} aberta com {itens.Count} de {pr.Items.Count} itens da requisição {pr.Number} " +
                  $"(o restante segue em outro processo).",
            actor, null, QuotationStatus.Open);
        await db.SaveChangesAsync(ct);
        return (q, null);
    }

    // ---- abertura agrupada (V2 — regra 1 generalizada) -----------------------
    /// <summary>
    /// Abre um processo a partir de itens aprovados de UMA OU MAIS SCs do mesmo centro de custo.
    /// A SC mais antiga vira a primária do cabeçalho (compatibilidade); cada item guarda a origem.
    /// Alçadas preservadas: o centro de custo único mantém a semântica de aprovação por CC.
    /// </summary>
    public async Task<(Quotation? q, UserError? error)> CreateFromItemsAsync(
        Actor actor, IReadOnlyList<Guid> prItemIds, QuotationKind kind, DateOnly? deadline, string? notes,
        CancellationToken ct = default)
    {
        var ids = prItemIds.Distinct().ToList();
        if (ids.Count == 0)
            return (null, new("RFQ-ERR-060", "Selecione ao menos um item de solicitação para abrir o processo."));

        var prs = await db.Requisitions.Include(r => r.Items)
            .Where(r => r.DeletedAt == null && r.Items.Any(i => ids.Contains(i.Id)))
            .ToListAsync(ct);
        var found = prs.SelectMany(r => r.Items).Where(i => ids.Contains(i.Id)).Select(i => i.Id).ToHashSet();
        if (found.Count != ids.Count)
            return (null, new("RFQ-ERR-060", "Há item selecionado que não existe mais — atualize a lista."));

        foreach (var pr0 in prs)
            if (pr0.Status is not (RequisitionStatus.Submitted or RequisitionStatus.Approved))
                return (null, new("RFQ-ERR-060",
                    $"A solicitação {pr0.Number} precisa estar enviada (ou aprovada) para entrar em cotação."));

        var centros = prs.Select(r => r.CostCenter.Trim().ToUpperInvariant()).Distinct().ToList();
        if (centros.Count > 1)
            return (null, new("RFQ-ERR-061",
                "O agrupamento só aceita solicitações do MESMO centro de custo — as alçadas de aprovação são do centro. " +
                $"Selecionados: {string.Join(", ", centros)}."));

        // nenhum item pode estar em outro processo ativo (item a item, não mais SC a SC)
        var emProcesso = await db.Quotations
            .Where(q => q.Status != QuotationStatus.Cancelled && q.Status != QuotationStatus.Rejected)
            .SelectMany(q => q.Items.Where(i => i.SourcePrItemId != null && ids.Contains(i.SourcePrItemId.Value))
                .Select(i => new { i.SourcePrNumber, q.Number }))
            .FirstOrDefaultAsync(ct);
        if (emProcesso is not null)
            return (null, new("RFQ-ERR-062",
                $"Item da solicitação {emProcesso.SourcePrNumber} já está no processo ativo {emProcesso.Number}."));
        // SC de fluxo antigo (sem rastreio por item) em processo ativo também bloqueia — os
        // processos novos travam item a item, então a SC pode ter outra parte já cotada
        var prIds = prs.Select(r => r.Id).ToList();
        var scAtiva = await db.Quotations
            .Where(q => prIds.Contains(q.SourcePrId) && q.Items.All(i => i.SourcePrItemId == null)
                        && q.Status != QuotationStatus.Cancelled && q.Status != QuotationStatus.Rejected)
            .Select(q => q.Number).FirstOrDefaultAsync(ct);
        if (scAtiva is not null)
            return (null, new("RFQ-ERR-062", $"Solicitação selecionada já possui o processo ativo {scAtiva}."));

        var now = clock.GetUtcNow();
        var ordered = prs.OrderBy(r => r.SubmittedAt ?? r.CreatedAt).ToList();
        var primary = ordered[0];
        var prefix = kind == QuotationKind.Bid ? "BID" : "RFQ";
        var seq = await NextSeqAsync(kind, ct);
        var numbers = ordered.Select(r => r.Number).ToList();
        var q = new Quotation
        {
            Number = $"{prefix}-{now.Year}-{seq:000000}",
            Kind = kind,
            SourcePrId = primary.Id,
            SourcePrNumber = primary.Number,
            CostCenter = primary.CostCenter,
            Justification = ordered.Count == 1 ? primary.Justification
                : $"Agrupamento de demandas: {string.Join(", ", numbers)}.",
            Deadline = deadline,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedBy = actor.Id,
            CreatedByLabel = actor.Label,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var familias = await FamiliesOfAsync(
            ordered.SelectMany(r => r.Items).Where(i => ids.Contains(i.Id)).Select(i => i.CatalogItemId), ct);
        var seqNo = 0;
        foreach (var pr0 in ordered)
            foreach (var i in pr0.Items.Where(i => ids.Contains(i.Id)).OrderBy(i => i.Sequence))
                q.Items.Add(new QuotationItem
                {
                    QuotationId = q.Id, Sequence = ++seqNo, CatalogItemId = i.CatalogItemId,
                    CatalogCode = i.CatalogCode, Description = i.Description,
                    Quantity = i.Quantity, UnitOfMeasure = i.UnitOfMeasure, Family = FamilyOf(i, familias),
                    SourcePrId = pr0.Id, SourcePrNumber = pr0.Number, SourcePrItemId = i.Id,
                });
        db.Quotations.Add(q);
        AddEvent(q, "COTACAO_ABERTA", ordered.Count == 1
                ? $"Cotação {q.Number} aberta a partir da requisição {primary.Number}."
                : $"Cotação {q.Number} aberta agrupando as requisições {string.Join(", ", numbers)} ({q.Items.Count} itens).",
            actor, null, QuotationStatus.Open);
        await db.SaveChangesAsync(ct);
        return (q, null);
    }

    // ---- fornecedores convidados (RFQ-BR-003) --------------------------------
    /// <param name="prazo">
    /// Até quando estes convidados têm para responder. Nulo cai no prazo do processo — que é
    /// o que mantém legível todo convite anterior a esta regra.
    /// </param>
    public async Task<(Quotation? q, UserError? error)> InviteSuppliersAsync(
        Actor actor, Guid id, IReadOnlyList<Guid> supplierIds, CancellationToken ct = default,
        DateOnly? prazo = null)
    {
        var q = await GetAsync(id, ct);
        if (q is null) return (null, new("RFQ-ERR-404", "Cotação não encontrada."));
        // o comprador ainda inclui fornecedores durante a análise, enquanto não escolheu o vencedor
        if (q.Status is not (QuotationStatus.Open or QuotationStatus.Analysis) || q.WinnerSupplierId is not null)
            return (null, new("RFQ-ERR-020", "Só dá para convidar fornecedores enquanto o processo está em cotação/análise."));

        var suppliers = await db.Suppliers.Where(s => supplierIds.Contains(s.Id)).ToListAsync(ct);
        foreach (var sid in supplierIds.Distinct())
        {
            var s = suppliers.SingleOrDefault(x => x.Id == sid);
            if (s is null || !s.Active || s.HomologationStatus == SupplierHomologation.Bloqueado)
                return (null, new("RFQ-ERR-010", "Fornecedor inativo, bloqueado ou inexistente não pode ser convidado."));
            // reconvidar quem foi dispensado reabre o convite: é o caminho que a mensagem do
            // RFQ-ERR-070 promete, e a alternativa seria um fornecedor dispensado por engano
            // ficar de fora do processo para sempre
            if (q.Suppliers.SingleOrDefault(x => x.SupplierId == sid) is { } jaConvidado)
            {
                if (jaConvidado.WaivedAt is null && prazo is null) continue;
                if (jaConvidado.WaivedAt is not null)
                {
                    jaConvidado.WaivedAt = null;
                    jaConvidado.WaivedByLabel = null;
                    jaConvidado.WaivedReason = null;
                    AddEvent(q, "FORNECEDOR_CONVIDADO",
                        $"Fornecedor {jaConvidado.SupplierName} convidado de novo.", actor);
                }
                if (prazo is not null) jaConvidado.ResponseDeadline = prazo;
                continue;
            }
            var invite = new QuotationSupplier
            {
                QuotationId = q.Id, SupplierId = s.Id,
                SupplierName = s.TradeName ?? s.LegalName, TaxId = s.TaxId ?? "",
                InvitedBy = actor.Id, InvitedByLabel = actor.Label, InvitedAt = clock.GetUtcNow(),
                ResponseDeadline = prazo,
            };
            // Add explícito (chave pré-gerada em pai já rastreado); o EF liga o convite à cotação
            // sozinho — adicionar à coleção aqui deixava o fornecedor duplicado na resposta
            db.QuotationSuppliers.Add(invite);
            AddEvent(q, "FORNECEDOR_CONVIDADO", $"Fornecedor {s.TradeName ?? s.LegalName} convidado.", actor);
        }
        await TouchAndSaveAsync(q, ct);
        return (q, null);
    }

    // ---- propostas (RFQ-BR-004) ---------------------------------------------
    public async Task<(Proposal? proposal, UserError? error)> SubmitProposalAsync(
        Guid quotationId, Guid supplierId, ProposalInput input, string via, string submittedByLabel,
        CancellationToken ct = default)
    {
        var q = await GetAsync(quotationId, ct);
        if (q is null) return (null, new("RFQ-ERR-404", "Cotação não encontrada."));
        if (q.Status is not (QuotationStatus.Open or QuotationStatus.Analysis) || q.WinnerSupplierId is not null)
            return (null, new("RFQ-ERR-020", "A cotação não recebe mais propostas."));
        var convite = q.Suppliers.SingleOrDefault(s => s.SupplierId == supplierId);
        if (convite is null)
            return (null, new("RFQ-ERR-050", "Fornecedor não convidado para esta cotação."));
        if (convite.WaivedAt is not null)
            return (null, new("RFQ-ERR-050",
                "O processo seguiu sem este fornecedor. Convide-o de novo para voltar a receber proposta."));
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        // o prazo que barra é o **deste convite**: é o que faz "dar novo prazo" ter efeito de
        // verdade, e não só apagar o aviso de atraso da tela
        var prazoDoConvite = convite.PrazoEfetivo(q.Deadline);
        if (prazoDoConvite is not null && today > prazoDoConvite)
            return (null, new("RFQ-ERR-020",
                $"O prazo de resposta deste fornecedor encerrou em {prazoDoConvite:dd/MM/yyyy}. "
                + "Dê um novo prazo para reabrir."));
        if (input.Items.Count == 0 || input.Items.Any(i => i.UnitPrice < 0))
            return (null, new("RFQ-ERR-021", "Informe o preço de todos os itens (valores não negativos)."));
        var itemIds = q.Items.Select(i => i.Id).ToHashSet();
        if (input.Items.Any(i => !itemIds.Contains(i.QuotationItemId)))
            return (null, new("RFQ-ERR-021", "Item da proposta não pertence à cotação."));

        var supplier = await db.Suppliers.SingleAsync(s => s.Id == supplierId, ct);
        var version = q.Proposals.Where(p => p.SupplierId == supplierId)
            .Select(p => p.VersionNumber).DefaultIfEmpty(0).Max() + 1;
        var proposal = new Proposal
        {
            QuotationId = q.Id, SupplierId = supplierId,
            SupplierName = supplier.TradeName ?? supplier.LegalName,
            VersionNumber = version,
            DeliveryDays = input.DeliveryDays,
            PaymentTerms = string.IsNullOrWhiteSpace(input.PaymentTerms) ? null : input.PaymentTerms.Trim(),
            PaymentMethodName = string.IsNullOrWhiteSpace(input.PaymentMethodName) ? null : input.PaymentMethodName.Trim(),
            PaymentDays = input.PaymentDays,
            FreightValue = input.FreightValue,
            TaxValue = input.TaxValue,
            OtherCosts = input.OtherCosts,
            DiscountValue = input.DiscountValue,
            Currency = string.IsNullOrWhiteSpace(input.Currency) ? "BRL" : input.Currency.Trim().ToUpperInvariant(),
            ValidUntil = input.ValidUntil,
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            SubmittedVia = via,
            SubmittedByLabel = submittedByLabel,
            SubmittedAt = clock.GetUtcNow(),
        };
        foreach (var i in input.Items)
        {
            var qi = q.Items.Single(x => x.Id == i.QuotationItemId);
            proposal.Items.Add(new ProposalItem
            {
                ProposalId = proposal.Id, QuotationItemId = qi.Id,
                UnitPrice = i.UnitPrice, Quantity = i.Quantity ?? qi.Quantity,
            });
        }
        // custo total (V2-P2): itens + frete + impostos + outros − desconto
        proposal.TotalValue = proposal.Items.Sum(i => i.UnitPrice * i.Quantity)
            + (proposal.FreightValue ?? 0) + (proposal.TaxValue ?? 0) + (proposal.OtherCosts ?? 0)
            - (proposal.DiscountValue ?? 0);
        if (proposal.TotalValue < 0)
            return (null, new("RFQ-ERR-021", "O desconto não pode ser maior que o total da proposta."));
        db.Proposals.Add(proposal);
        AddEvent(q, "PROPOSTA_RECEBIDA",
            $"Proposta v{version} de {proposal.SupplierName} recebida ({via}) — total {proposal.TotalValue:0.00}.",
            new Actor(Guid.Empty, submittedByLabel, "Supplier"));
        await TouchAndSaveAsync(q, ct);
        return (proposal, null);
    }

    // ---- negociação e ganho (saving) ----------------------------------------
    /// <summary>
    /// Registra o resultado da negociação com um fornecedor: o comprador informa o valor
    /// fechado (ou o desconto em %) e o sistema grava uma nova versão da proposta e o ganho
    /// em relação à primeira proposta daquele fornecedor.
    /// </summary>
    public async Task<(Proposal? proposal, UserError? error)> RegisterNegotiationAsync(
        Actor actor, Guid id, Guid supplierId, decimal? closedValue, decimal? discountPercent,
        string? notes, CancellationToken ct = default)
    {
        var q = await GetAsync(id, ct);
        if (q is null) return (null, new("RFQ-ERR-404", "Cotação não encontrada."));
        if (q.Status is not (QuotationStatus.Open or QuotationStatus.Analysis) || q.WinnerSupplierId is not null)
            return (null, new("RFQ-ERR-020", "A negociação é registrada antes da escolha do fornecedor."));

        var versoes = q.Proposals.Where(p => p.SupplierId == supplierId).OrderBy(p => p.VersionNumber).ToList();
        if (versoes.Count == 0)
            return (null, new("RFQ-ERR-021", "Registre a proposta deste fornecedor antes de negociar."));
        var atual = versoes[^1];

        var fechado = closedValue;
        if (fechado is null)
        {
            if (discountPercent is null or <= 0)
                return (null, new("RFQ-ERR-022", "Informe o valor fechado ou o desconto negociado (%)."));
            if (discountPercent >= 100)
                return (null, new("RFQ-ERR-022", "O desconto negociado precisa ser menor que 100%."));
            fechado = Math.Round(atual.TotalValue * (1 - discountPercent.Value / 100m), 2);
        }
        if (fechado <= 0)
            return (null, new("RFQ-ERR-022", "O valor fechado precisa ser maior que zero."));
        if (fechado > atual.TotalValue)
            return (null, new("RFQ-ERR-022",
                $"O valor fechado ({fechado:0.00}) é maior que a proposta atual ({atual.TotalValue:0.00}): isso não é um ganho de negociação."));

        // a nova versão repete itens e frete e concentra a negociação no desconto
        var bruto = atual.Items.Sum(i => i.UnitPrice * i.Quantity) + (atual.FreightValue ?? 0)
            + (atual.TaxValue ?? 0) + (atual.OtherCosts ?? 0);
        var input = new ProposalInput(
            atual.DeliveryDays, atual.PaymentTerms, atual.FreightValue, atual.ValidUntil,
            string.IsNullOrWhiteSpace(notes) ? atual.Notes : notes.Trim(),
            atual.Items.Select(i => new ProposalItemInput(i.QuotationItemId, i.UnitPrice, i.Quantity)).ToList(),
            bruto - fechado.Value, atual.Currency, atual.PaymentDays, atual.TaxValue, atual.OtherCosts,
            atual.PaymentMethodName);
        var (nova, error) = await SubmitProposalAsync(q.Id, supplierId, input, "NEGOCIACAO", actor.Label, ct);
        if (error is not null) return (null, error);

        var primeira = versoes[0].TotalValue;
        q.NegotiationNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        q.NegotiatedByLabel = actor.Label;
        q.NegotiatedAt = clock.GetUtcNow();
        ApplySaving(q, nova!);
        AddEvent(q, "NEGOCIACAO_REGISTRADA",
            $"Negociação com {nova!.SupplierName}: de {primeira:0.00} para {nova.TotalValue:0.00} " +
            $"(ganho de {primeira - nova.TotalValue:0.00}).", actor, null, null, q.NegotiationNotes);
        await TouchAndSaveAsync(q, ct);
        return (nova, null);
    }

    /// <summary>Ganho da negociação: primeira proposta do fornecedor menos o valor fechado com ele.</summary>
    private void ApplySaving(Quotation q, Proposal fechada)
    {
        var primeira = q.Proposals.Where(p => p.SupplierId == fechada.SupplierId)
            .OrderBy(p => p.VersionNumber).First().TotalValue;
        q.BaselineValue = primeira;
        q.NegotiatedValue = fechada.TotalValue;
        q.SavingValue = primeira - fechada.TotalValue;
        q.SavingPercent = primeira > 0 ? Math.Round((primeira - fechada.TotalValue) / primeira * 100m, 2) : 0m;
    }

    // ---- encerramento p/ análise --------------------------------------------
    public Task<(Quotation? q, UserError? error)> CloseForAnalysisAsync(Actor actor, Guid id, CancellationToken ct = default) =>
        TransitionAsync(actor, id, QuotationStatus.Open, QuotationStatus.Analysis, "COTACAO_ENCERRADA",
            "Cotação encerrada para análise das propostas.", null, ct,
            guard: q => q.Proposals.Count == 0 ? new("RFQ-ERR-021", "Não há propostas recebidas para analisar.") : null);
}
