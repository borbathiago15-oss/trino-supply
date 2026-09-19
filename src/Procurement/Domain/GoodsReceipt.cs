using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Procurement.Domain;

public readonly record struct GoodsReceiptId(Guid Value)
{
    public static GoodsReceiptId New() => new(Guid.NewGuid());
    public static GoodsReceiptId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("GoodsReceiptId não pode ser vazio.", nameof(value))
        : new GoodsReceiptId(value);
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Não-conformidade constatada na conferência física (MMS-005). Acompanha descrição obrigatória e
/// é o gancho para a tratativa com o fornecedor (devolução/reposição).
/// </summary>
public enum ReceiptOccurrence
{
    None = 0,
    Avaria = 1,        // material chegou danificado
    Falta = 2,         // veio menos do que o pedido
    Excesso = 3,       // veio mais do que o pedido
    Divergencia = 4,   // item/especificação diferente do pedido
}

/// <summary>Uma linha conferida na doca: o que foi pedido, o que chegou e o que veio avariado.</summary>
public sealed class GoodsReceiptLine : IBelongsToTenant
{
    private GoodsReceiptLine(
        Guid id, CompanyId companyId, GoodsReceiptId receiptId, Guid orderLineId, string itemCode, string unit,
        decimal quantityOrdered, decimal quantityReceived, decimal quantityDamaged,
        ReceiptOccurrence occurrence, string? occurrenceNote)
    {
        Id = id;
        CompanyId = companyId;
        ReceiptId = receiptId;
        OrderLineId = orderLineId;
        ItemCode = itemCode;
        Unit = unit;
        QuantityOrdered = quantityOrdered;
        QuantityReceived = quantityReceived;
        QuantityDamaged = quantityDamaged;
        Occurrence = occurrence;
        OccurrenceNote = occurrenceNote;
    }

    private GoodsReceiptLine() { } // EF

    public Guid Id { get; private set; }
    public CompanyId CompanyId { get; private set; }
    public GoodsReceiptId ReceiptId { get; private set; }

    /// <summary>Linha da OC conferida — rastreabilidade recebimento→pedido.</summary>
    public Guid OrderLineId { get; private set; }
    public string ItemCode { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;

    /// <summary>Quantidade da OC (snapshot, para a conferência "pedido × entregue").</summary>
    public decimal QuantityOrdered { get; private set; }

    /// <summary>Quantidade que FISICAMENTE chegou (inclui o que veio avariado).</summary>
    public decimal QuantityReceived { get; private set; }

    /// <summary>Parte da quantidade recebida que chegou imprestável — não entra no estoque.</summary>
    public decimal QuantityDamaged { get; private set; }
    public ReceiptOccurrence Occurrence { get; private set; }
    public string? OccurrenceNote { get; private set; }

    /// <summary>O que efetivamente entra no estoque: recebido menos avariado (MMS-005).</summary>
    public decimal NetQuantity => QuantityReceived - QuantityDamaged;

    internal static GoodsReceiptLine Create(
        CompanyId companyId, GoodsReceiptId receiptId, Guid orderLineId, string itemCode, string unit,
        decimal quantityOrdered, decimal quantityReceived, decimal quantityDamaged,
        ReceiptOccurrence occurrence, string? occurrenceNote) =>
        new(Guid.NewGuid(), companyId, receiptId, orderLineId, itemCode.Trim().ToUpperInvariant(),
            unit, quantityOrdered, quantityReceived, quantityDamaged, occurrence,
            string.IsNullOrWhiteSpace(occurrenceNote) ? null : occurrenceNote.Trim());
}

/// <summary>Uma linha informada pelo conferente, já com o quanto daquela linha da OC entrou antes.</summary>
public sealed record ReceiptLineInput(
    Guid OrderLineId, string ItemCode, string Unit, decimal QuantityOrdered, decimal QuantityAlreadyReceived,
    decimal QuantityReceived, decimal QuantityDamaged, ReceiptOccurrence Occurrence, string? OccurrenceNote);

/// <summary>
/// Recebimento de mercadoria (MMS-005): a conferência física de uma entrega contra a OC, amarrada à
/// nota fiscal. Guarda, por linha, o pedido × o entregue × o avariado, e a ocorrência de
/// não-conformidade quando houver. A quantidade LÍQUIDA (recebido − avariado) é o que entra no
/// estoque — o crédito em si é orquestrado pelo host, pois Materiais é outro bounded context.
/// Entregas parciais são normais: a mesma OC aceita vários recebimentos até completar.
/// </summary>
public sealed class GoodsReceipt : AggregateRoot<GoodsReceiptId>, IBelongsToTenant
{
    private readonly List<GoodsReceiptLine> _lines = new();

    private GoodsReceipt(
        GoodsReceiptId id, CompanyId companyId, PurchaseOrderId orderId, string invoiceNumber,
        DateOnly? invoiceDate, string receivedBySubject, DateTimeOffset receivedAt, string? notes) : base(id)
    {
        CompanyId = companyId;
        PurchaseOrderId = orderId;
        InvoiceNumber = invoiceNumber;
        InvoiceDate = invoiceDate;
        ReceivedBySubject = receivedBySubject;
        ReceivedAt = receivedAt;
        Notes = notes;
    }

    private GoodsReceipt() : base(default!) { } // EF

    public CompanyId CompanyId { get; private set; }
    public PurchaseOrderId PurchaseOrderId { get; private set; }
    public string InvoiceNumber { get; private set; } = string.Empty;   // nota fiscal
    public DateOnly? InvoiceDate { get; private set; }
    public string ReceivedBySubject { get; private set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Entrada no estoque já efetivada? Falso = pendente de crédito (reprocessável).</summary>
    public bool StockPosted { get; private set; }
    public DateTimeOffset? StockPostedAt { get; private set; }
    public IReadOnlyList<GoodsReceiptLine> Lines => _lines;

    /// <summary>Houve não-conformidade em alguma linha (aciona tratativa com o fornecedor).</summary>
    public bool HasOccurrence => _lines.Any(l => l.Occurrence != ReceiptOccurrence.None);

    public static Result<GoodsReceipt> Register(
        CompanyId companyId, PurchaseOrderId orderId, string? invoiceNumber, DateOnly? invoiceDate,
        string receivedBySubject, DateTimeOffset receivedAt, string? notes,
        IEnumerable<ReceiptLineInput> lines)
    {
        if (string.IsNullOrWhiteSpace(receivedBySubject))
            return Result.Failure<GoodsReceipt>(new Error("purchases.receipt.receiver_required", "Conferente obrigatório."));
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            return Result.Failure<GoodsReceipt>(new Error("purchases.receipt.invoice_required", "Informe o número da nota fiscal."));

        var informadas = lines.ToList();
        var comQuantidade = informadas.Where(l => l.QuantityReceived > 0).ToList();
        if (comQuantidade.Count == 0)
            return Result.Failure<GoodsReceipt>(new Error("purchases.receipt.lines_required",
                "Informe a quantidade recebida de ao menos um item."));

        foreach (var l in comQuantidade)
        {
            if (l.QuantityReceived < 0 || l.QuantityDamaged < 0)
                return Result.Failure<GoodsReceipt>(new Error("purchases.receipt.qty_invalid",
                    "Quantidades não podem ser negativas."));
            if (l.QuantityDamaged > l.QuantityReceived)
                return Result.Failure<GoodsReceipt>(new Error("purchases.receipt.damaged_exceeds_received",
                    $"Item '{l.ItemCode}': avariado ({l.QuantityDamaged}) não pode passar do recebido ({l.QuantityReceived})."));

            // Excesso sobre o saldo pendente da OC só passa se declarado como ocorrência (auditável).
            var pendente = l.QuantityOrdered - l.QuantityAlreadyReceived;
            if (l.QuantityReceived > pendente && l.Occurrence != ReceiptOccurrence.Excesso)
                return Result.Failure<GoodsReceipt>(new Error("purchases.receipt.exceeds_ordered",
                    $"Item '{l.ItemCode}': recebido ({l.QuantityReceived}) passa do pendente da OC ({pendente}). " +
                    "Registre como ocorrência de Excesso para prosseguir."));

            // Avaria sem classificação não entra: é o gatilho da devolução.
            if (l.QuantityDamaged > 0 && l.Occurrence == ReceiptOccurrence.None)
                return Result.Failure<GoodsReceipt>(new Error("purchases.receipt.occurrence_required",
                    $"Item '{l.ItemCode}': há avaria — selecione o tipo de ocorrência."));
            if (l.Occurrence != ReceiptOccurrence.None && string.IsNullOrWhiteSpace(l.OccurrenceNote))
                return Result.Failure<GoodsReceipt>(new Error("purchases.receipt.occurrence_note_required",
                    $"Item '{l.ItemCode}': descreva a não-conformidade."));
        }

        var receipt = new GoodsReceipt(GoodsReceiptId.New(), companyId, orderId, invoiceNumber.Trim(),
            invoiceDate, receivedBySubject, receivedAt, string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
        foreach (var l in comQuantidade)
            receipt._lines.Add(GoodsReceiptLine.Create(companyId, receipt.Id, l.OrderLineId, l.ItemCode, l.Unit,
                l.QuantityOrdered, l.QuantityReceived, l.QuantityDamaged, l.Occurrence, l.OccurrenceNote));
        return Result.Success(receipt);
    }

    /// <summary>Confirma que a quantidade líquida deste recebimento já entrou no estoque.</summary>
    public Result MarkStockPosted(DateTimeOffset now)
    {
        if (StockPosted) return Result.Success(); // idempotente
        StockPosted = true;
        StockPostedAt = now;
        Version++;
        return Result.Success();
    }
}
