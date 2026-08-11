using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Materials.Domain;

public readonly record struct ConsumptionId(Guid Value)
{
    public static ConsumptionId New() => new(Guid.NewGuid());
    public static ConsumptionId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("ConsumptionId não pode ser vazio.", nameof(value))
        : new ConsumptionId(value);
    public override string ToString() => Value.ToString();
}

/// <summary>Motivos sugeridos da baixa de consumo (spec Almoxarifado). Aceita texto livre além destes.</summary>
public static class ConsumptionReasons
{
    public static readonly IReadOnlyList<string> All = new[] { "Nova contratação", "Substituição" };
}

/// <summary>Item entregue na baixa (produto + quantidade). A baixa gera uma saída no ledger por linha.</summary>
public sealed class ConsumptionLine : IBelongsToTenant
{
    private ConsumptionLine(Guid id, CompanyId companyId, ConsumptionId consumptionId, string itemCode, decimal quantity)
    {
        Id = id;
        CompanyId = companyId;
        ConsumptionId = consumptionId;
        ItemCode = itemCode;
        Quantity = quantity;
    }

    private ConsumptionLine() { } // EF

    public Guid Id { get; private set; }
    public CompanyId CompanyId { get; private set; }
    public ConsumptionId ConsumptionId { get; private set; }
    public string ItemCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }

    public static ConsumptionLine Create(CompanyId companyId, ConsumptionId consumptionId, string itemCode, decimal quantity) =>
        new(Guid.NewGuid(), companyId, consumptionId, itemCode.Trim().ToUpperInvariant(), quantity);
}

/// <summary>
/// Baixa de consumo (spec Sistema de Almoxarifado — entrega direta ao colaborador). O responsável pelo
/// estoque registra a entrega informando <b>empresa</b>, <b>centro de custo</b>, <b>colaborador</b> e o
/// <b>motivo</b> (ex.: nova contratação, substituição), com os produtos/quantidades. Cada linha vira
/// uma saída no ledger (com garantia de saldo). Base para "consumo por colaborador/centro".
/// </summary>
public sealed class Consumption : AggregateRoot<ConsumptionId>, IBelongsToTenant
{
    private Consumption(ConsumptionId id, CompanyId companyId, string companyCode, string costCenterCode,
        CollaboratorId collaboratorId, string reason, string issuedBySubject, DateTimeOffset issuedAt) : base(id)
    {
        CompanyId = companyId;
        CompanyCode = companyCode;
        CostCenterCode = costCenterCode;
        CollaboratorId = collaboratorId;
        Reason = reason;
        IssuedBySubject = issuedBySubject;
        IssuedAt = issuedAt;
    }

    private Consumption() : base(default!) { } // EF

    private readonly List<ConsumptionLine> _lines = new();

    public CompanyId CompanyId { get; private set; }
    public string CompanyCode { get; private set; } = string.Empty;       // empresa (código da pagadora)
    public string CostCenterCode { get; private set; } = string.Empty;    // centro de custo
    public CollaboratorId CollaboratorId { get; private set; }
    public string Reason { get; private set; } = string.Empty;            // motivo (ex.: nova contratação)
    public string IssuedBySubject { get; private set; } = string.Empty;   // quem deu a baixa
    public DateTimeOffset IssuedAt { get; private set; }
    public IReadOnlyList<ConsumptionLine> Lines => _lines;

    public static Result<Consumption> Create(
        CompanyId companyId, string companyCode, string costCenterCode, CollaboratorId collaboratorId,
        string reason, string issuedBySubject, IEnumerable<(string ItemCode, decimal Quantity)> lines,
        DateTimeOffset issuedAt)
    {
        if (string.IsNullOrWhiteSpace(companyCode))
            return Result.Failure<Consumption>(new Error("materials.consumption.company_required", "Informe a empresa do custo."));
        if (string.IsNullOrWhiteSpace(costCenterCode))
            return Result.Failure<Consumption>(new Error("materials.consumption.cost_center_required", "Informe o centro de custo."));
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure<Consumption>(new Error("materials.consumption.reason_required", "Informe o motivo da entrega."));

        var list = lines?.ToList() ?? new();
        if (list.Count == 0)
            return Result.Failure<Consumption>(new Error("materials.consumption.lines_required", "Informe ao menos um produto."));
        if (list.Any(l => l.Quantity <= 0))
            return Result.Failure<Consumption>(new Error("materials.consumption.qty_invalid", "Quantidade deve ser positiva."));
        if (list.Any(l => string.IsNullOrWhiteSpace(l.ItemCode)))
            return Result.Failure<Consumption>(new Error("materials.consumption.item_required", "Produto é obrigatório em todas as linhas."));

        var c = new Consumption(ConsumptionId.New(), companyId, companyCode.Trim().ToUpperInvariant(),
            costCenterCode.Trim().ToUpperInvariant(), collaboratorId, reason.Trim(), issuedBySubject, issuedAt);
        foreach (var l in list)
            c._lines.Add(ConsumptionLine.Create(companyId, c.Id, l.ItemCode, l.Quantity));
        return Result.Success(c);
    }
}
