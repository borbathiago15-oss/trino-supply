using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Analytics;

/// <summary>
/// A planilha do relatório de material: o que a diretoria pede quando quer "todas as
/// solicitações" — a lista completa não cabe num PDF de parede, e na planilha ela se filtra e
/// se soma. Sete abas, uma por bloco da tela, mais a de <b>itens</b>: uma linha por item, com
/// o custo congelado, porque é nela que se responde "quanto de luva o centro X pediu".
/// </summary>
public static class RelatorioDeMaterialPlanilha
{
    public static byte[] Gerar(RelatorioDeMaterial r)
    {
        var lista = r.Requisitions ?? [];
        var solicitacoes = new PlanilhaXlsx.Aba("Solicitações",
            ["Número", "Criada em", "Centro de custo", "Nome do centro", "Solicitante", "Situação", "Aprovada em", "Aprovada por",
             "Atendida em", "Atendida por", "SC do faltante", "Itens", "Qtd pedida", "Qtd entregue",
             "Valor pedido", "Valor liberado", "Valor entregue", "Itens sem custo", "Prazo", "Dias na fila", "Prazo da família (dias)"],
            lista.Select(s => (IReadOnlyList<object?>)[
                s.Number, s.CreatedAt, s.CostCenter, s.CostCenterName, s.Requester, s.StatusLabel, s.ApprovedAt, s.ApprovedBy,
                s.FulfilledAt, s.FulfilledBy, s.PurchaseRequisitionNumber, s.Items, s.RequestedQty, s.DeliveredQty,
                s.RequestedValue, s.ApprovedValue, s.DeliveredValue, s.ItemsWithoutPrice,
                s.SlaStatus switch { "ESTOURADO" => "estourado", "ATENCAO" => "a vencer", "OK" => "no prazo", _ => null },
                s.SlaDays, s.SlaMaxDays]));

        var itens = new PlanilhaXlsx.Aba("Itens",
            ["Solicitação", "Criada em", "Centro de custo", "Solicitante", "Situação da solicitação",
             "Código", "Produto", "Família", "Unidade", "Qtd pedida", "Qtd liberada", "Qtd entregue",
             "Custo unitário", "Valor pedido", "Valor liberado", "Valor entregue", "Situação do item"],
            lista.SelectMany(s => s.ItemList.Select(i => (IReadOnlyList<object?>)[
                s.Number, s.CreatedAt, s.CostCenter, s.Requester, s.StatusLabel,
                i.Code, i.Description, i.Family, i.Unit, i.Qty, i.ApprovedQty, i.DeliveredQty,
                i.UnitPrice, i.RequestedValue, i.ApprovedValue, i.DeliveredValue, i.StatusLabel])));

        PlanilhaXlsx.Aba Ranking(string nome, string rotulo, IReadOnlyList<LinhaDeMaterial> linhas) => new(nome,
            [rotulo, "Solicitações", "Qtd pedida", "Qtd entregue", "Valor pedido", "Valor entregue"],
            linhas.Select(l => (IReadOnlyList<object?>)[l.Label, l.Count, l.Qty, l.Delivered, l.RequestedValue, l.DeliveredValue]));

        var meses = new PlanilhaXlsx.Aba("Mês a mês",
            ["Mês", "Solicitadas", "Atendidas", "Recusadas", "Valor pedido", "Valor entregue"],
            r.Months.Select(m => (IReadOnlyList<object?>)[m.Month, m.Requested, m.Fulfilled, m.Rejected, m.RequestedValue, m.DeliveredValue]));

        var prazos = new PlanilhaXlsx.Aba("Prazo por família",
            ["Família", "Prazo (dias)", "Atendimentos medidos", "No prazo", "Média (dias)"],
            r.SlaByFamily.Select(p => (IReadOnlyList<object?>)[p.Family, p.MaxDays, p.Measured, p.Met, p.AvgDays]));

        return PlanilhaXlsx.Gerar([
            solicitacoes, itens,
            Ranking("Por centro", "Centro de custo", r.ByCostCenter),
            Ranking("Por família", "Família", r.ByFamily),
            Ranking("Por produto", "Produto", r.ByProduct),
            Ranking("Por solicitante", "Solicitante", r.ByRequester),
            meses, prazos,
        ]);
    }
}
