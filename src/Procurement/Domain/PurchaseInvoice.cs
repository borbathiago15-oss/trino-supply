using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Procurement.Domain;

public readonly record struct PurchaseInvoiceId(Guid Value)
{
    public static PurchaseInvoiceId New() => new(Guid.NewGuid());
    public static PurchaseInvoiceId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("PurchaseInvoiceId não pode ser vazio.", nameof(value))
        : new PurchaseInvoiceId(value);
    public override string ToString() => Value.ToString();
}

/// <summary>Situação da conciliação de três pontas da nota.</summary>
public enum InvoiceMatchStatus
{
    Pending = 1,     // importada, ainda não conciliada
    Matched = 2,     // bate com OC e doca — liberada ao financeiro
    Divergent = 3,   // divergência acima da tolerância — envio travado
}

/// <summary>Um item da NF-e como veio no XML (&lt;det&gt;&lt;prod&gt;), sem reinterpretação.</summary>
public sealed class PurchaseInvoiceLine : IBelongsToTenant
{
    private PurchaseInvoiceLine(
        Guid id, CompanyId companyId, PurchaseInvoiceId invoiceId, int itemNumber, string productCode,
        string description, string? ncm, string? cfop, string unit, decimal quantity,
        decimal unitPrice, decimal totalValue)
    {
        Id = id;
        CompanyId = companyId;
        InvoiceId = invoiceId;
        ItemNumber = itemNumber;
        ProductCode = productCode;
        Description = description;
        Ncm = ncm;
        Cfop = cfop;
        Unit = unit;
        Quantity = quantity;
        UnitPrice = unitPrice;
        TotalValue = totalValue;
    }

    private PurchaseInvoiceLine() { } // EF

    public Guid Id { get; private set; }
    public CompanyId CompanyId { get; private set; }
    public PurchaseInvoiceId InvoiceId { get; private set; }

    /// <summary>Sequência do item na nota (atributo <c>nItem</c>) — é como o fiscal cita a linha.</summary>
    public int ItemNumber { get; private set; }

    /// <summary>Código do produto <b>do fornecedor</b> (<c>cProd</c>), não o nosso.</summary>
    public string ProductCode { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string? Ncm { get; private set; }
    public string? Cfop { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal TotalValue { get; private set; }

    internal static PurchaseInvoiceLine Create(
        CompanyId companyId, PurchaseInvoiceId invoiceId, NfeItem item) =>
        new(Guid.NewGuid(), companyId, invoiceId, item.ItemNumber,
            item.ProductCode.Trim().ToUpperInvariant(), item.Description.Trim(),
            Blank(item.Ncm), Blank(item.Cfop), item.Unit.Trim().ToLowerInvariant(),
            item.Quantity, item.UnitPrice, item.TotalValue);

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>Item extraído do XML, antes de virar entidade.</summary>
public sealed record NfeItem(
    int ItemNumber, string ProductCode, string Description, string? Ncm, string? Cfop,
    string Unit, decimal Quantity, decimal UnitPrice, decimal TotalValue);

/// <summary>
/// A NF-e inteira como o parser a leu. É o contrato entre a infraestrutura (que sabe XML) e o
/// domínio (que sabe o que é uma nota válida).
/// </summary>
public sealed record NfeDocument(
    string AccessKey, long Number, string Series, DateTimeOffset IssuedAt, string Model,
    string EmitterTaxId, string EmitterName, decimal TotalValue, IReadOnlyList<NfeItem> Items);

/// <summary>
/// Nota fiscal de entrada (Fase 05) vinculada a uma OC. Nasce da ingestão do XML — nada aqui é
/// digitado — e carrega o veredito da conciliação de três pontas.
/// <para>
/// Divergência acima da tolerância <b>trava o envio ao financeiro</b>. A liberação manual existe,
/// porque o mundo real tem casos legítimos (acordo comercial, frete embutido), mas só com
/// justificativa registrada e por quem tem a permissão de compras — o que se quer evitar é o
/// pagamento silencioso, não a decisão humana.
/// </para>
/// </summary>
public sealed class PurchaseInvoice : AggregateRoot<PurchaseInvoiceId>, IBelongsToTenant
{
    private readonly List<PurchaseInvoiceLine> _lines = new();

    private PurchaseInvoice(
        PurchaseInvoiceId id, CompanyId companyId, PurchaseOrderId orderId, string accessKey,
        long number, string series, DateTimeOffset issuedAt, string emitterTaxId, string emitterName,
        decimal totalValue, string importedBySubject, DateTimeOffset importedAt) : base(id)
    {
        CompanyId = companyId;
        PurchaseOrderId = orderId;
        AccessKey = accessKey;
        Number = number;
        Series = series;
        IssuedAt = issuedAt;
        EmitterTaxId = emitterTaxId;
        EmitterName = emitterName;
        TotalValue = totalValue;
        ImportedBySubject = importedBySubject;
        ImportedAt = importedAt;
        Status = InvoiceMatchStatus.Pending;
    }

    private PurchaseInvoice() : base(default!) { } // EF

    public CompanyId CompanyId { get; private set; }
    public PurchaseOrderId PurchaseOrderId { get; private set; }

    /// <summary>Chave de acesso de 44 dígitos — a identidade fiscal da nota, única por tenant.</summary>
    public string AccessKey { get; private set; } = string.Empty;
    public long Number { get; private set; }
    public string Series { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; private set; }
    public string EmitterTaxId { get; private set; } = string.Empty;
    public string EmitterName { get; private set; } = string.Empty;
    public decimal TotalValue { get; private set; }
    public string ImportedBySubject { get; private set; } = string.Empty;
    public DateTimeOffset ImportedAt { get; private set; }

    public InvoiceMatchStatus Status { get; private set; }
    public DateTimeOffset? MatchedAt { get; private set; }

    /// <summary>Resumo legível das divergências — o que aparece no alerta e no histórico.</summary>
    public string? MatchSummary { get; private set; }

    /// <summary>Liberada para o Contas a Pagar (automático no match, ou manual justificado).</summary>
    public bool ReleasedToFinance { get; private set; }
    public string? ReleasedBySubject { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }
    public string? ReleaseNote { get; private set; }

    public IReadOnlyList<PurchaseInvoiceLine> Lines => _lines;

    public static Result<PurchaseInvoice> Import(
        CompanyId companyId, PurchaseOrderId orderId, NfeDocument doc,
        string? expectedSupplierTaxId, string importedBySubject, DateTimeOffset importedAt)
    {
        if (string.IsNullOrWhiteSpace(importedBySubject))
            return Result.Failure<PurchaseInvoice>(new Error("purchases.invoice.importer_required", "Usuário obrigatório."));

        var chave = NfeAccessKey.Parse(doc.AccessKey);
        if (chave.IsFailure) return Result.Failure<PurchaseInvoice>(chave.Error);

        if (doc.Model != NfeAccessKey.ModeloNfe)
            return Result.Failure<PurchaseInvoice>(new Error("purchases.invoice.model_invalid",
                $"Só nota fiscal eletrônica modelo 55 entra como nota de compra (recebido modelo {doc.Model})."));

        if (doc.Items.Count == 0)
            return Result.Failure<PurchaseInvoice>(new Error("purchases.invoice.no_items",
                "A nota fiscal não tem itens."));
        if (doc.Items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0))
            return Result.Failure<PurchaseInvoice>(new Error("purchases.invoice.item_invalid",
                "Item da nota com quantidade não positiva ou preço negativo."));

        // O emitente do XML tem de ser o fornecedor da OC — nota de outro fornecedor anexada ao
        // pedido errado é o começo de um pagamento indevido.
        var emitente = NfeAccessKey.OnlyDigits(doc.EmitterTaxId);
        var esperado = NfeAccessKey.OnlyDigits(expectedSupplierTaxId);
        if (esperado.Length == 14 && emitente.Length == 14 && emitente != esperado)
            return Result.Failure<PurchaseInvoice>(new Error("purchases.invoice.supplier_mismatch",
                "O emitente da nota não é o fornecedor desta OC."));

        var invoice = new PurchaseInvoice(
            PurchaseInvoiceId.New(), companyId, orderId, chave.Value, doc.Number, doc.Series.Trim(),
            doc.IssuedAt, emitente, doc.EmitterName.Trim(), doc.TotalValue, importedBySubject, importedAt);
        foreach (var item in doc.Items)
            invoice._lines.Add(PurchaseInvoiceLine.Create(companyId, invoice.Id, item));
        return Result.Success(invoice);
    }

    /// <summary>
    /// Grava o veredito da conciliação. Nota conciliada libera sozinha para o financeiro; nota
    /// divergente fica travada até alguém justificar.
    /// </summary>
    public Result ApplyMatch(MatchResult result, DateTimeOffset now)
    {
        Status = result.Matched ? InvoiceMatchStatus.Matched : InvoiceMatchStatus.Divergent;
        MatchedAt = now;
        MatchSummary = result.Summary;

        if (result.Matched && !ReleasedToFinance)
        {
            ReleasedToFinance = true;
            ReleasedAt = now;
            ReleasedBySubject = "sistema";   // conciliação automática, sem intervenção humana
        }
        else if (!result.Matched)
        {
            // Reconciliar depois de uma correção pode reprovar o que já fora liberado: a trava volta.
            ReleasedToFinance = false;
            ReleasedBySubject = null;
            ReleasedAt = null;
            ReleaseNote = null;
        }

        Version++;
        return Result.Success();
    }

    /// <summary>Libera uma nota divergente com justificativa — a exceção fica auditável.</summary>
    public Result ReleaseWithJustification(string bySubject, string? note, DateTimeOffset now)
    {
        if (Status == InvoiceMatchStatus.Pending)
            return Result.Failure(new Error("purchases.invoice.not_matched",
                "A nota ainda não foi conciliada."));
        if (ReleasedToFinance)
            return Result.Failure(new Error("purchases.invoice.already_released",
                "Esta nota já está liberada para o financeiro."));
        if (string.IsNullOrWhiteSpace(note))
            return Result.Failure(new Error("purchases.invoice.release_note_required",
                "Liberar nota divergente exige justificativa."));

        ReleasedToFinance = true;
        ReleasedBySubject = bySubject;
        ReleasedAt = now;
        ReleaseNote = note.Trim();
        Version++;
        return Result.Success();
    }
}
