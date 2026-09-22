using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>Uma etapa da linha do tempo do solicitante.</summary>
public record EtapaDaSc(string Chave, string Rotulo, string Situacao, DateTimeOffset? Quando, string? Quem);

/// <summary>
/// Onde a solicitação está, na língua de quem pediu: a etapa atual, com quem está, desde
/// quando, o que se espera e quando chega. Derivado da SC, do processo de cotação e do
/// pedido — nada aqui é gravado.
/// </summary>
public record Acompanhamento(
    IReadOnlyList<EtapaDaSc> Etapas, string EtapaAtual, string Frase, string? ComQuem, DateTimeOffset? Desde,
    DateOnly? Previsao, string? Motivo, bool PrecisaDoSolicitante,
    Guid? QuotationId, string? QuotationNumber, Guid? PurchaseOrderId, string? PurchaseOrderNumber, string? Fornecedor);

/// <summary>
/// A linha do tempo da solicitação em seis passos — Enviada → Comprador → Cotação →
/// Aprovação → Pedido → Entrega. O solicitante não precisa saber em que tabela a SC está:
/// a frase diz com quem ela está e o que falta. Devolvida e rejeitada dizem o motivo e,
/// na devolvida, que a bola voltou para quem pediu.
/// </summary>
public static class AcompanhamentoDaSc
{
    public static readonly (string Chave, string Rotulo)[] Passos =
    [
        ("enviada", "Enviada"), ("comprador", "Com o comprador"), ("cotacao", "Em cotação"),
        ("aprovacao", "Aprovação"), ("pedido", "Pedido fechado"), ("entrega", "Entregue"),
    ];

    public const string Feita = "feita", Atual = "atual", Pendente = "pendente", Parada = "parada";

    /// <summary>
    /// Monta o acompanhamento a partir do que já existe. <paramref name="aprovadores"/> traz os
    /// nomes do nível pendente do centro (vazio = sem cadastro, que a frase diz).
    /// </summary>
    /// <param name="ehGestorResponsavel">
    /// O Nível 2 pendente é o gestor de suprimentos do comprador, e não a diretoria
    /// (<c>AlcadaDoComprador</c>). Muda só a frase: quem decide já veio em <paramref name="aprovadores"/>.
    /// </param>
    public static Acompanhamento Montar(PurchaseRequisition pr, Quotation? q, PurchaseOrder? o,
        IReadOnlyList<string>? aprovadores = null, bool ehGestorResponsavel = false)
    {
        var etapas = new Dictionary<string, (string situacao, DateTimeOffset? quando, string? quem)>();
        foreach (var (chave, _) in Passos) etapas[chave] = (Pendente, null, null);

        string atual; string frase; string? comQuem = null; DateTimeOffset? desde = null; DateOnly? previsao = null;
        string? motivo = null; var precisaDoSolicitante = false; string? fornecedor = null;

        // ---- a SC ainda não saiu das mãos do solicitante -----------------------------
        if (pr.Status is RequisitionStatus.Draft)
        {
            atual = "enviada"; etapas["enviada"] = (Atual, null, null);
            frase = "Rascunho: revise e envie quando estiver pronta.";
            precisaDoSolicitante = true;
            return Fechar();
        }
        if (pr.Status is RequisitionStatus.Returned)
        {
            atual = "enviada"; etapas["enviada"] = (Parada, pr.DecidedAt, pr.DecidedByLabel);
            motivo = pr.DecisionReason;
            frase = $"Devolvida{(pr.DecidedByLabel is null ? "" : $" por {pr.DecidedByLabel}")} para ajuste: corrija e reenvie.";
            comQuem = pr.RequesterLabel; desde = pr.DecidedAt; precisaDoSolicitante = true;
            return Fechar();
        }
        if (pr.Status is RequisitionStatus.Rejected && q is null)
        {
            atual = "aprovacao"; etapas["enviada"] = (Feita, pr.SubmittedAt, null); etapas["aprovacao"] = (Parada, pr.DecidedAt, pr.DecidedByLabel);
            motivo = pr.DecisionReason;
            frase = $"Rejeitada{(pr.DecidedByLabel is null ? "" : $" por {pr.DecidedByLabel}")}.";
            desde = pr.DecidedAt;
            return Fechar();
        }
        if (pr.Status is RequisitionStatus.Cancelled && q is null)
        {
            atual = "enviada"; etapas["enviada"] = (Parada, pr.DecidedAt ?? pr.UpdatedAt, pr.DecidedByLabel);
            motivo = pr.DecisionReason; frase = "Cancelada."; desde = pr.DecidedAt;
            return Fechar();
        }

        etapas["enviada"] = (Feita, pr.SubmittedAt, pr.RequesterLabel);

        // ---- sem processo: está com Suprimentos --------------------------------------
        if (q is null)
        {
            if (pr.Status is RequisitionStatus.InApproval)
            {
                // acervo do fluxo anterior: a SC aprovava sem preço
                atual = "aprovacao"; etapas["comprador"] = (Feita, pr.SubmittedAt, null);
                etapas["aprovacao"] = (Atual, pr.SubmittedAt, null);
                comQuem = aprovadores is { Count: > 0 } ? string.Join(", ", aprovadores) : null;
                frase = comQuem is null ? "Aguardando aprovação do gestor." : $"Aguardando aprovação de {comQuem}.";
                desde = pr.SubmittedAt;
                return Fechar();
            }
            if (pr.AssignedToLabel is not null)
            {
                atual = "cotacao"; etapas["comprador"] = (Feita, pr.AssignedAt, pr.AssignedToLabel);
                etapas["cotacao"] = (Atual, pr.AssignedAt, pr.AssignedToLabel);
                comQuem = pr.AssignedToLabel; desde = pr.AssignedAt;
                frase = $"Com o comprador {pr.AssignedToLabel}, que vai abrir a cotação.";
                return Fechar();
            }
            atual = "comprador"; etapas["comprador"] = (Atual, pr.SubmittedAt, null);
            comQuem = "Suprimentos"; desde = pr.SubmittedAt;
            frase = "Com Suprimentos — aguardando a designação do comprador que fará a cotação.";
            return Fechar();
        }

        // ---- há processo de cotação --------------------------------------------------
        etapas["comprador"] = (Feita, pr.AssignedAt ?? q.CreatedAt, pr.AssignedToLabel ?? q.CreatedByLabel);
        var comprador = q.CreatedByLabel;
        var convidados = q.Suppliers.Count;
        var propostas = q.Proposals.Select(p => p.SupplierId).Distinct().Count();
        fornecedor = q.IsSplitAward
            ? string.Join(" + ", q.AwardList.Select(a => a.SupplierName).Distinct())
            : q.AwardList.FirstOrDefault()?.SupplierName ?? q.Proposals.FirstOrDefault(p => p.Id == q.WinnerProposalId)?.SupplierName;

        switch (q.Status)
        {
            case QuotationStatus.Open:
            case QuotationStatus.Analysis:
                atual = "cotacao"; etapas["cotacao"] = (Atual, q.CreatedAt, comprador);
                comQuem = comprador; desde = q.CreatedAt;
                frase = q.Status == QuotationStatus.Analysis
                    ? $"{comprador} está comparando {propostas} proposta(s) para escolher o fornecedor."
                    : convidados == 0
                        ? $"{comprador} abriu a cotação e vai convidar os fornecedores."
                        : $"{comprador} está cotando com {convidados} fornecedor(es) — {propostas} já responderam.";
                break;
            case QuotationStatus.AwaitingManager:
            case QuotationStatus.AwaitingDirector:
            {
                etapas["cotacao"] = (Feita, q.SelectedAt, comprador);
                atual = "aprovacao";
                var nivel2 = q.Status == QuotationStatus.AwaitingDirector;
                desde = nivel2 ? q.ManagerApprovedAt : q.SelectedAt;
                etapas["aprovacao"] = (Atual, desde, null);
                comQuem = aprovadores is { Count: > 0 } ? string.Join(", ", aprovadores) : null;
                var nivel = nivel2
                    ? (ehGestorResponsavel ? "Nível 2 (gestor de suprimentos)" : "Nível 2 (diretoria)")
                    : "Nível 1 (gestor do centro)";
                frase = comQuem is null
                    ? $"Aguardando aprovação de {nivel} — o centro {pr.CostCenter} está sem aprovador cadastrado."
                    : $"Aguardando aprovação de {comQuem} — {nivel}.";
                if (fornecedor is not null) frase += $" Fornecedor escolhido: {fornecedor}.";
                break;
            }
            case QuotationStatus.ApprovedForIssue:
            case QuotationStatus.PoIssued:
                etapas["cotacao"] = (Feita, q.SelectedAt, comprador);
                etapas["aprovacao"] = (Feita, q.DirectorApprovedAt ?? q.ManagerApprovedAt, q.DirectorApprovedByLabel ?? q.ManagerApprovedByLabel);
                if (o is null)
                {
                    atual = "pedido"; etapas["pedido"] = (Atual, q.DirectorApprovedAt, comprador);
                    comQuem = comprador; desde = q.DirectorApprovedAt;
                    frase = $"Compra aprovada. {comprador} está fechando o pedido{(fornecedor is null ? "" : $" com {fornecedor}")}.";
                }
                else
                {
                    frase = FraseDoPedido(o, comprador, out atual, out comQuem, out desde, out previsao, out motivo, etapas);
                }
                break;
            case QuotationStatus.Rejected:
                etapas["cotacao"] = (Feita, q.SelectedAt, comprador);
                atual = "aprovacao"; etapas["aprovacao"] = (Parada, q.UpdatedAt, null);
                motivo = q.DecisionReason; desde = q.UpdatedAt;
                frase = "A compra foi rejeitada na aprovação.";
                break;
            default:
                atual = "cotacao"; etapas["cotacao"] = (Parada, q.UpdatedAt, comprador);
                motivo = q.DecisionReason; desde = q.UpdatedAt;
                frase = "A cotação foi cancelada pelo comprador.";
                break;
        }
        return Fechar();

        Acompanhamento Fechar() => new(
            Passos.Select(p => new EtapaDaSc(p.Chave, p.Rotulo, etapas[p.Chave].situacao, etapas[p.Chave].quando, etapas[p.Chave].quem)).ToList(),
            atual, frase, comQuem, desde, previsao, motivo, precisaDoSolicitante,
            q?.Id, q?.Number, o?.Id, o?.Number, fornecedor ?? o?.SupplierName);
    }

    /// <summary>O pedido existe: a frase vem do que falta nele — O.C., faturamento, entrega.</summary>
    private static string FraseDoPedido(PurchaseOrder o, string comprador, out string atual, out string? comQuem,
        out DateTimeOffset? desde, out DateOnly? previsao, out string? motivo,
        Dictionary<string, (string situacao, DateTimeOffset? quando, string? quem)> etapas)
    {
        motivo = null; previsao = o.PromisedDate;
        var chegada = o.PromisedDate is { } d ? $" Chega até {d:dd/MM/yyyy}." : "";
        switch (o.Status)
        {
            case PurchaseOrderStatus.Received:
                etapas["pedido"] = (Feita, o.CreatedAt, o.SupplierName);
                etapas["entrega"] = (Feita, o.DeliveryCompletedAt ?? o.ReceivedAt, o.ReceivedByLabel);
                atual = "entrega"; comQuem = null; desde = o.DeliveryCompletedAt ?? o.ReceivedAt;
                return $"Entregue em {(desde is { } e ? e.ToString("dd/MM/yyyy") : "—")}.";
            case PurchaseOrderStatus.Cancelled:
                etapas["pedido"] = (Parada, o.CreatedAt, o.SupplierName);
                atual = "pedido"; comQuem = comprador; desde = o.CreatedAt; motivo = o.CancelReason;
                return "O pedido foi cancelado.";
            case PurchaseOrderStatus.PartiallyReceived:
                etapas["pedido"] = (Feita, o.CreatedAt, o.SupplierName);
                etapas["entrega"] = (o.DeliveryCompletedAt is null ? Atual : Parada, o.DeliveryCompletedAt ?? o.ReceivedAt, o.ReceivedByLabel);
                atual = "entrega"; comQuem = o.SupplierName; desde = o.ReceivedAt; motivo = o.CancelReason;
                return o.DeliveryCompletedAt is null
                    ? $"Parte já chegou; o restante está com {o.SupplierName}.{chegada}"
                    : "Entregue em parte — o saldo não vai chegar.";
            default:
                etapas["pedido"] = (Feita, o.CreatedAt, o.SupplierName);
                atual = "entrega"; etapas["entrega"] = (Atual, o.CreatedAt, o.SupplierName);
                comQuem = o.SupplierName; desde = o.CreatedAt;
                if (o.ErpNumber is null && o.NoErpReason is null)
                {
                    atual = "pedido"; etapas["pedido"] = (Atual, o.CreatedAt, comprador); etapas["entrega"] = (Pendente, null, null);
                    comQuem = comprador;
                    return $"Pedido {o.Number} criado com {o.SupplierName}. {comprador} está registrando a O.C. do ERP.";
                }
                return o.Invoices.Count == 0
                    ? $"Pedido fechado com {o.SupplierName}, aguardando o faturamento.{chegada}"
                    : $"Faturado por {o.SupplierName}, aguardando a entrega.{chegada}";
        }
    }

    /// <summary>
    /// O acompanhamento de cada SC da lista, com o mínimo de idas ao banco: cotação e pedido
    /// por SC, e os aprovadores do nível pendente por centro (uma consulta por centro e nível).
    /// </summary>
    public static async Task<Dictionary<Guid, (ProcessStatusView Situacao, Acompanhamento Acompanhamento)>> MontarMapAsync(
        AppDbContext db, IReadOnlyCollection<PurchaseRequisition> prs, CancellationToken ct = default)
    {
        var map = new Dictionary<Guid, (ProcessStatusView, Acompanhamento)>();
        if (prs.Count == 0) return map;
        var ids = prs.Select(r => r.Id).ToList();
        var quotations = await db.Quotations.Include(q => q.Items).Include(q => q.Suppliers)
            .Include(q => q.Proposals).Include(q => q.Awards)
            .Where(q => ids.Contains(q.SourcePrId)
                        || q.Items.Any(i => i.SourcePrId != null && ids.Contains(i.SourcePrId.Value)))
            .OrderByDescending(q => q.CreatedAt).ToListAsync(ct);
        var qIds = quotations.Select(q => q.Id).ToList();
        var orders = await db.PurchaseOrders.Include(o => o.Items).Include(o => o.Invoices)
            .Where(o => (o.SourcePrId != null && ids.Contains(o.SourcePrId!.Value))
                        || (o.QuotationId != null && qIds.Contains(o.QuotationId!.Value)))
            .OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
        var aprovadores = new Dictionary<string, List<string>>();
        foreach (var pr in prs)
        {
            var q = quotations.FirstOrDefault(x => x.CoversPr(pr.Id));
            var o = orders.FirstOrDefault(x => x.SourcePrId == pr.Id) ?? (q is null ? null : orders.FirstOrDefault(x => x.QuotationId == q.Id));
            List<string>? nomes = null;
            var nivel = q?.Status switch
            {
                QuotationStatus.AwaitingManager => ApprovalLevels.Level1,
                QuotationStatus.AwaitingDirector => ApprovalLevels.Level2,
                _ => q is null && pr.Status == RequisitionStatus.InApproval ? ApprovalLevels.Level1 : 0,
            };
            // a compra da própria área de compras tem Nível 2 próprio: o gestor responsável pelo
            // comprador. Dizer a lista do centro aqui mandaria o solicitante cobrar quem não decide
            var rota = nivel == ApprovalLevels.Level2 && q is not null
                ? await AlcadaDoComprador.RotaAsync(db, q, q.ManagerApprovedBy, ct)
                : new RotaDoNivel2(CaminhoDoNivel2.Padrao);
            if (rota.Caminho == CaminhoDoNivel2.GestorResponsavel)
                nomes = [rota.GestorNome!];
            else if (nivel > 0)
            {
                var chave = $"{pr.CostCenter.Trim().ToUpperInvariant()}:{nivel}";
                if (!aprovadores.TryGetValue(chave, out nomes))
                    aprovadores[chave] = nomes = (await ApprovalLevels.OfAsync(db, pr.CostCenter, nivel, ct)).Select(a => a.UserName).ToList();
            }
            map[pr.Id] = (ProcessStatus.Of(pr, q, o),
                Montar(pr, q, o, nomes, rota.Caminho == CaminhoDoNivel2.GestorResponsavel));
        }
        return map;
    }
}
