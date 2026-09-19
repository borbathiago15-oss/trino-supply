namespace TrinoSupply.Procurement.Domain;

/// <summary>Onde um item da solicitação está no ciclo — a granularidade da Torre é o ITEM.</summary>
public enum ItemStage
{
    Draft = 1,              // solicitação ainda em rascunho
    Awaiting = 2,           // em aprovação (nível 1 ou 2)
    Approved = 3,           // aprovado, ainda sem cotação nem OC
    Quoting = 4,            // em concorrência aberta
    Ordered = 5,            // OC emitida, nada recebido
    PartiallyReceived = 6,  // parte chegou
    Received = 7,           // tudo chegou
    FulfilledFromStock = 8, // atendido pelo Almox, sem compra
    Rejected = 9,
}

/// <summary>Farol de SLA. <see cref="None"/> = item encerrado, não há prazo a vigiar.</summary>
public enum SlaLight { None = 0, Green = 1, Yellow = 2, Red = 3 }

/// <summary>Os fatos de um item que a Torre observa para classificar etapa e farol.</summary>
public sealed record ItemTimeline(
    RequisitionStatus RequisitionStatus,
    bool HasOrder,
    bool InOpenQuotation,
    decimal QuantityRequested,
    decimal QuantityReceivedNet,
    DateOnly? NeededBy,
    DateOnly? PromisedDate);

/// <summary>
/// Regras da Torre de Controlo (Fase 04). Cálculo puro: dado o que aconteceu com o item, diz em
/// que etapa ele está, se ainda está em aberto, quanto falta chegar e a cor do farol. A Torre
/// não tem tabela própria — é uma leitura viva dos registros de solicitação, cotação, OC e doca.
/// </summary>
public static class ControlTower
{
    /// <summary>Com menos de tantos dias até o prazo, o farol fica amarelo.</summary>
    public const int YellowWindowDays = 3;

    public static ItemStage Stage(ItemTimeline t) => t.RequisitionStatus switch
    {
        RequisitionStatus.Draft => ItemStage.Draft,
        RequisitionStatus.Submitted or RequisitionStatus.ApprovedLevel1 => ItemStage.Awaiting,
        RequisitionStatus.Rejected => ItemStage.Rejected,
        RequisitionStatus.FulfilledFromStock => ItemStage.FulfilledFromStock,
        _ when !t.HasOrder => t.InOpenQuotation ? ItemStage.Quoting : ItemStage.Approved,
        _ when t.QuantityReceivedNet >= t.QuantityRequested => ItemStage.Received,
        _ when t.QuantityReceivedNet > 0 => ItemStage.PartiallyReceived,
        _ => ItemStage.Ordered,
    };

    public static bool IsOpen(ItemStage stage) =>
        stage is not (ItemStage.Received or ItemStage.FulfilledFromStock or ItemStage.Rejected);

    /// <summary>O que ainda não chegou. Item sem OC deve a quantidade inteira.</summary>
    public static decimal Pending(ItemTimeline t)
    {
        var stage = Stage(t);
        if (!IsOpen(stage)) return 0m;
        return Math.Max(t.QuantityRequested - t.QuantityReceivedNet, 0m);
    }

    /// <summary>
    /// O prazo que vale é o <b>prometido na OC</b>; sem OC, a data de necessidade da solicitação.
    /// Sem prazo nenhum não há como atrasar — fica verde, e a tela mostra que não há data.
    /// </summary>
    public static SlaLight Light(ItemTimeline t, DateOnly today)
    {
        if (!IsOpen(Stage(t))) return SlaLight.None;

        var deadline = t.PromisedDate ?? t.NeededBy;
        if (deadline is null) return SlaLight.Green;
        if (deadline.Value < today) return SlaLight.Red;
        if (deadline.Value <= today.AddDays(YellowWindowDays)) return SlaLight.Yellow;
        return SlaLight.Green;
    }
}
