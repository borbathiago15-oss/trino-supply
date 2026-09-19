using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Procurement.Domain;

public readonly record struct QuotationId(Guid Value)
{
    public static QuotationId New() => new(Guid.NewGuid());
    public static QuotationId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("QuotationId não pode ser vazio.", nameof(value))
        : new QuotationId(value);
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Situação da cotação. A adjudicação é <b>por item</b>, então a cotação passa por
/// <see cref="PartiallyAwarded"/> enquanto sobrar item sem vencedor escolhido.
/// </summary>
public enum QuotationStatus
{
    Open = 1,              // aberta a propostas
    PartiallyAwarded = 2,  // parte dos itens já tem vencedor
    Awarded = 3,           // todos os itens adjudicados — pronta para virar OC
    Cancelled = 4,
}

/// <summary>Item em concorrência: uma linha da requisição levada a mercado.</summary>
public sealed class QuotationLine : IBelongsToTenant
{
    private QuotationLine(
        Guid id, CompanyId companyId, QuotationId quotationId, Guid requisitionLineId,
        string itemCode, decimal quantity, string unit)
    {
        Id = id;
        CompanyId = companyId;
        QuotationId = quotationId;
        RequisitionLineId = requisitionLineId;
        ItemCode = itemCode;
        Quantity = quantity;
        Unit = unit;
    }

    private QuotationLine() { } // EF

    public Guid Id { get; private set; }
    public CompanyId CompanyId { get; private set; }
    public QuotationId QuotationId { get; private set; }

    /// <summary>Linha da requisição que originou o item — rastreabilidade cotação→pedido.</summary>
    public Guid RequisitionLineId { get; private set; }
    public string ItemCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string Unit { get; private set; } = string.Empty;

    /// <summary>Fornecedor vencedor deste item (adjudicação mista: cada item pode ter o seu).</summary>
    public Guid? AwardedSupplierId { get; private set; }

    /// <summary>Preço unitário congelado na adjudicação — é o que vai para a OC.</summary>
    public decimal? AwardedUnitPrice { get; private set; }

    /// <summary>Justificativa da escolha. Obrigatória quando o vencedor NÃO é o menor preço.</summary>
    public string? AwardNote { get; private set; }
    public string? AwardedBySubject { get; private set; }
    public DateTimeOffset? AwardedAt { get; private set; }

    public bool IsAwarded => AwardedSupplierId is not null;

    internal void Award(Guid supplierId, decimal unitPrice, string? note, string bySubject, DateTimeOffset now)
    {
        AwardedSupplierId = supplierId;
        AwardedUnitPrice = unitPrice;
        AwardNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        AwardedBySubject = bySubject;
        AwardedAt = now;
    }

    internal static QuotationLine Create(
        CompanyId companyId, QuotationId quotationId, Guid requisitionLineId,
        string itemCode, decimal quantity, string unit) =>
        new(Guid.NewGuid(), companyId, quotationId, requisitionLineId,
            itemCode.Trim().ToUpperInvariant(), quantity, unit.Trim().ToLowerInvariant());
}

/// <summary>
/// Fornecedor convidado a cotar, com o cabeçalho da proposta dele (condições que valem para todos
/// os itens). Enquanto <see cref="RespondedAt"/> é nulo, o convite está em aberto.
/// </summary>
public sealed class QuotationParticipant : IBelongsToTenant
{
    private QuotationParticipant(Guid id, CompanyId companyId, QuotationId quotationId, Guid supplierId, DateTimeOffset invitedAt)
    {
        Id = id;
        CompanyId = companyId;
        QuotationId = quotationId;
        SupplierId = supplierId;
        InvitedAt = invitedAt;
    }

    private QuotationParticipant() { } // EF

    public Guid Id { get; private set; }
    public CompanyId CompanyId { get; private set; }
    public QuotationId QuotationId { get; private set; }
    public Guid SupplierId { get; private set; }
    public DateTimeOffset InvitedAt { get; private set; }
    public DateTimeOffset? RespondedAt { get; private set; }

    /// <summary>Proposta entregue depois do prazo. Não invalida — fica visível ao comprador.</summary>
    public bool IsLate { get; private set; }
    public string? PaymentTerms { get; private set; }
    public string? FreightTerms { get; private set; }
    public DateOnly? ValidUntil { get; private set; }
    public string? Notes { get; private set; }

    public bool HasResponded => RespondedAt is not null;

    internal void RecordResponse(ProposalHeader header, bool late, DateTimeOffset now)
    {
        RespondedAt = now;
        IsLate = late;
        PaymentTerms = Trim(header.PaymentTerms);
        FreightTerms = Trim(header.FreightTerms);
        ValidUntil = header.ValidUntil;
        Notes = Trim(header.Notes);
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    internal static QuotationParticipant Create(CompanyId companyId, QuotationId quotationId, Guid supplierId, DateTimeOffset invitedAt) =>
        new(Guid.NewGuid(), companyId, quotationId, supplierId, invitedAt);
}

/// <summary>Preço e prazo que um participante ofereceu para um item da cotação.</summary>
public sealed class QuotationBid : IBelongsToTenant
{
    private QuotationBid(
        Guid id, CompanyId companyId, QuotationId quotationId, Guid participantId, Guid lineId,
        decimal unitPrice, int? deliveryDays, string? notes)
    {
        Id = id;
        CompanyId = companyId;
        QuotationId = quotationId;
        ParticipantId = participantId;
        LineId = lineId;
        UnitPrice = unitPrice;
        DeliveryDays = deliveryDays;
        Notes = notes;
    }

    private QuotationBid() { } // EF

    public Guid Id { get; private set; }
    public CompanyId CompanyId { get; private set; }
    public QuotationId QuotationId { get; private set; }
    public Guid ParticipantId { get; private set; }
    public Guid LineId { get; private set; }
    public decimal UnitPrice { get; private set; }

    /// <summary>Prazo de entrega em dias corridos a partir da OC. Nulo = o fornecedor não informou.</summary>
    public int? DeliveryDays { get; private set; }
    public string? Notes { get; private set; }

    internal static QuotationBid Create(
        CompanyId companyId, QuotationId quotationId, Guid participantId, Guid lineId,
        decimal unitPrice, int? deliveryDays, string? notes) =>
        new(Guid.NewGuid(), companyId, quotationId, participantId, lineId, unitPrice, deliveryDays,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
}

/// <summary>Item que entra na concorrência, tirado de uma linha pendente da requisição.</summary>
public sealed record QuotationLineInput(Guid RequisitionLineId, string ItemCode, decimal Quantity, string Unit);

/// <summary>Condições gerais da proposta do fornecedor (valem para todos os itens que ele cotou).</summary>
public sealed record ProposalHeader(string? PaymentTerms, string? FreightTerms, DateOnly? ValidUntil, string? Notes);

/// <summary>Preço e prazo ofertados para um item. Item não cotado simplesmente não vem na lista.</summary>
public sealed record BidInput(Guid LineId, decimal UnitPrice, int? DeliveryDays, string? Notes);

/// <summary>Escolha do vencedor de um item, com a justificativa quando não for o menor preço.</summary>
public sealed record AwardInput(Guid LineId, Guid SupplierId, string? Note);

/// <summary>
/// Cotação / concorrência (RFQ — Fase 03). Leva os itens <b>ainda não pedidos</b> de uma requisição
/// aprovada a um conjunto de fornecedores, recolhe as propostas (preço e prazo por item) e registra
/// a <b>adjudicação por item</b> — cada item pode ter um vencedor diferente, que é exatamente o que
/// a compra dividida (uma OC por fornecedor) sabe executar.
/// <para>
/// O controle que dá sentido ao processo: escolher <b>quem não é o menor preço exige justificativa
/// registrada</b>. Preço não é o único critério legítimo (prazo, OTIF, condição de pagamento), mas a
/// exceção fica auditável em vez de invisível.
/// </para>
/// A adjudicação não é reversível item a item: para mudar de ideia, cancela-se a cotação e abre-se
/// outra — a decisão de compra é um registro, não um rascunho.
/// </summary>
public sealed class Quotation : AggregateRoot<QuotationId>, IBelongsToTenant
{
    private readonly List<QuotationLine> _lines = new();
    private readonly List<QuotationParticipant> _participants = new();
    private readonly List<QuotationBid> _bids = new();

    private Quotation(
        QuotationId id, CompanyId companyId, long number, RequisitionId requisitionId,
        string createdBySubject, DateTimeOffset createdAt, DateTimeOffset closesAt, string? notes) : base(id)
    {
        CompanyId = companyId;
        Number = number;
        RequisitionId = requisitionId;
        CreatedBySubject = createdBySubject;
        CreatedAt = createdAt;
        ClosesAt = closesAt;
        Notes = notes;
        Status = QuotationStatus.Open;
    }

    private Quotation() : base(default!) { } // EF

    public CompanyId CompanyId { get; private set; }

    /// <summary>Número sequencial por tenant — é como a cotação é citada no dia a dia.</summary>
    public long Number { get; private set; }
    public RequisitionId RequisitionId { get; private set; }
    public string CreatedBySubject { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Prazo para receber propostas. Depois dele a proposta entra marcada como atrasada.</summary>
    public DateTimeOffset ClosesAt { get; private set; }
    public string? Notes { get; private set; }
    public QuotationStatus Status { get; private set; }
    public string? CancelledBySubject { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancelReason { get; private set; }

    public IReadOnlyList<QuotationLine> Lines => _lines;
    public IReadOnlyList<QuotationParticipant> Participants => _participants;
    public IReadOnlyList<QuotationBid> Bids => _bids;

    /// <summary>Quantos fornecedores efetivamente responderam — a base da concorrência.</summary>
    public int ResponseCount => _participants.Count(p => p.HasResponded);

    public static Result<Quotation> Open(
        CompanyId companyId, long number, RequisitionId requisitionId, string createdBySubject,
        DateTimeOffset createdAt, DateTimeOffset closesAt, string? notes,
        IEnumerable<QuotationLineInput> lines, IEnumerable<Guid> supplierIds)
    {
        if (string.IsNullOrWhiteSpace(createdBySubject))
            return Result.Failure<Quotation>(new Error("purchases.quotation.buyer_required", "Comprador obrigatório."));
        if (closesAt <= createdAt)
            return Result.Failure<Quotation>(new Error("purchases.quotation.deadline_invalid",
                "O prazo para receber propostas deve ser no futuro."));

        var itens = lines.ToList();
        if (itens.Count == 0)
            return Result.Failure<Quotation>(new Error("purchases.quotation.lines_required",
                "Selecione ao menos um item para cotar."));
        if (itens.Any(l => l.Quantity <= 0))
            return Result.Failure<Quotation>(new Error("purchases.quotation.qty_invalid",
                "Quantidade dos itens deve ser positiva."));
        if (itens.Select(l => l.RequisitionLineId).Distinct().Count() != itens.Count)
            return Result.Failure<Quotation>(new Error("purchases.quotation.duplicate_line",
                "O mesmo item da requisição não pode entrar duas vezes na cotação."));

        // Concorrência pressupõe disputa: com um único convidado isso é compra direta, não cotação.
        var fornecedores = supplierIds.Distinct().ToList();
        if (fornecedores.Count < 2)
            return Result.Failure<Quotation>(new Error("purchases.quotation.suppliers_required",
                "Convide ao menos dois fornecedores — cotação com um só é compra direta."));

        var q = new Quotation(QuotationId.New(), companyId, number, requisitionId, createdBySubject,
            createdAt, closesAt, string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
        foreach (var l in itens)
            q._lines.Add(QuotationLine.Create(companyId, q.Id, l.RequisitionLineId, l.ItemCode, l.Quantity, l.Unit));
        foreach (var s in fornecedores)
            q._participants.Add(QuotationParticipant.Create(companyId, q.Id, s, createdAt));
        return Result.Success(q);
    }

    /// <summary>
    /// Registra (ou substitui) a proposta de um fornecedor convidado. Recotar é normal na
    /// negociação: a proposta nova apaga a anterior dele, inteira — nunca se mistura preço velho
    /// com preço novo. Item que ele não quer atender simplesmente não vem na lista.
    /// </summary>
    public Result SubmitProposal(Guid supplierId, ProposalHeader header, IEnumerable<BidInput> bids, DateTimeOffset now)
    {
        if (Status == QuotationStatus.Cancelled)
            return Result.Failure(new Error("purchases.quotation.cancelled", "Esta cotação foi cancelada."));
        if (Status is QuotationStatus.Awarded or QuotationStatus.PartiallyAwarded)
            return Result.Failure(new Error("purchases.quotation.already_awarded",
                "A cotação já teve itens adjudicados — não aceita novas propostas."));

        var participant = _participants.FirstOrDefault(p => p.SupplierId == supplierId);
        if (participant is null)
            return Result.Failure(new Error("purchases.quotation.not_invited",
                "Este fornecedor não foi convidado para a cotação."));

        var ofertas = bids.ToList();
        if (ofertas.Count == 0)
            return Result.Failure(new Error("purchases.quotation.bids_required",
                "Informe o preço de ao menos um item."));
        if (ofertas.Select(b => b.LineId).Distinct().Count() != ofertas.Count)
            return Result.Failure(new Error("purchases.quotation.duplicate_bid",
                "O mesmo item aparece duas vezes na proposta."));

        foreach (var b in ofertas)
        {
            if (_lines.All(l => l.Id != b.LineId))
                return Result.Failure(new Error("purchases.quotation.line_not_found",
                    "Item proposto não pertence a esta cotação."));
            if (b.UnitPrice <= 0)
                return Result.Failure(new Error("purchases.quotation.price_invalid",
                    "Preço unitário deve ser positivo. Para não atender um item, deixe-o de fora da proposta."));
            if (b.DeliveryDays is < 0)
                return Result.Failure(new Error("purchases.quotation.delivery_invalid",
                    "Prazo de entrega não pode ser negativo."));
        }

        _bids.RemoveAll(b => b.ParticipantId == participant.Id);
        foreach (var b in ofertas)
            _bids.Add(QuotationBid.Create(CompanyId, Id, participant.Id, b.LineId, b.UnitPrice, b.DeliveryDays, b.Notes));

        participant.RecordResponse(header, late: now > ClosesAt, now);
        Version++;
        return Result.Success();
    }

    /// <summary>
    /// Adjudica os itens informados aos fornecedores escolhidos. Cada item sai para quem o comprador
    /// decidir — mas escolher fora do menor preço <b>exige justificativa</b>, que fica gravada na
    /// linha. Quando o último item é adjudicado, a cotação vira <see cref="QuotationStatus.Awarded"/>
    /// e está pronta para gerar as OCs.
    /// </summary>
    public Result Award(IReadOnlyCollection<AwardInput> awards, string awardedBySubject, DateTimeOffset now)
    {
        if (Status == QuotationStatus.Cancelled)
            return Result.Failure(new Error("purchases.quotation.cancelled", "Esta cotação foi cancelada."));
        if (Status == QuotationStatus.Awarded)
            return Result.Failure(new Error("purchases.quotation.already_awarded",
                "Todos os itens desta cotação já foram adjudicados."));
        if (string.IsNullOrWhiteSpace(awardedBySubject))
            return Result.Failure(new Error("purchases.quotation.buyer_required", "Comprador obrigatório."));
        if (awards.Count == 0)
            return Result.Failure(new Error("purchases.quotation.awards_required",
                "Escolha o vencedor de ao menos um item."));
        if (awards.Select(a => a.LineId).Distinct().Count() != awards.Count)
            return Result.Failure(new Error("purchases.quotation.duplicate_award",
                "O mesmo item aparece duas vezes na adjudicação."));

        // Valida tudo antes de gravar qualquer coisa: adjudicação é atômica.
        var aplicar = new List<(QuotationLine Line, Guid SupplierId, decimal UnitPrice, string? Note)>(awards.Count);
        foreach (var a in awards)
        {
            var line = _lines.FirstOrDefault(l => l.Id == a.LineId);
            if (line is null)
                return Result.Failure(new Error("purchases.quotation.line_not_found",
                    "Item informado não pertence a esta cotação."));
            if (line.IsAwarded)
                return Result.Failure(new Error("purchases.quotation.line_already_awarded",
                    $"O item '{line.ItemCode}' já foi adjudicado."));

            var participant = _participants.FirstOrDefault(p => p.SupplierId == a.SupplierId);
            if (participant is null)
                return Result.Failure(new Error("purchases.quotation.not_invited",
                    "Fornecedor escolhido não participou desta cotação."));

            var bid = _bids.FirstOrDefault(b => b.ParticipantId == participant.Id && b.LineId == line.Id);
            if (bid is null)
                return Result.Failure(new Error("purchases.quotation.no_bid",
                    $"O fornecedor escolhido não cotou o item '{line.ItemCode}'."));

            // O menor preço da linha entre TODAS as propostas recebidas. Escolher outro é legítimo
            // (prazo, desempenho de entrega, condição de pagamento) — mas precisa estar escrito.
            var menor = _bids.Where(b => b.LineId == line.Id).Min(b => b.UnitPrice);
            if (bid.UnitPrice > menor && string.IsNullOrWhiteSpace(a.Note))
                return Result.Failure(new Error("purchases.quotation.award_note_required",
                    $"Item '{line.ItemCode}': o escolhido não é o menor preço — justifique a escolha."));

            aplicar.Add((line, a.SupplierId, bid.UnitPrice, a.Note));
        }

        foreach (var (line, supplierId, unitPrice, note) in aplicar)
            line.Award(supplierId, unitPrice, note, awardedBySubject, now);

        Status = _lines.All(l => l.IsAwarded) ? QuotationStatus.Awarded : QuotationStatus.PartiallyAwarded;
        Version++;
        return Result.Success();
    }

    /// <summary>Cancela a cotação (motivo obrigatório). Não cancela o que já virou OC.</summary>
    public Result Cancel(string bySubject, string? reason, DateTimeOffset now)
    {
        if (Status == QuotationStatus.Cancelled)
            return Result.Failure(new Error("purchases.quotation.cancelled", "Esta cotação já foi cancelada."));
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("purchases.quotation.cancel_reason_required",
                "Informe o motivo do cancelamento."));

        Status = QuotationStatus.Cancelled;
        CancelledBySubject = bySubject;
        CancelledAt = now;
        CancelReason = reason.Trim();
        Version++;
        return Result.Success();
    }
}
