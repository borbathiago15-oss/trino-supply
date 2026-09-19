using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.Procurement.Domain;
using Xunit;

namespace TrinoSupply.Domain.Tests;

public class PurchaseRequisitionTests
{
    private static readonly CompanyId Company = CompanyId.New();
    private static readonly PayingCompanyId Pay = PayingCompanyId.New();
    private static readonly CostCenterId Cc = CostCenterId.New();
    private static readonly (string, decimal, string)[] Lines = [("PARAFUSO", 10m, "un")];
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static PurchaseRequisition New(string requester = "solicitante", string a1 = "aprov1", string a2 = "aprov2",
        string just = "Reposição de estoque") =>
        PurchaseRequisition.Create(Company, requester, Pay, Cc, RequisitionPriority.Normal, just, a1, a2, Lines, Now).Value;

    private static PurchaseRequisition Submitted(string requester = "solicitante")
    {
        var req = New(requester);
        req.Submit();
        return req;
    }

    [Fact]
    public void Create_sem_linhas_falha()
    {
        var r = PurchaseRequisition.Create(Company, "sol", Pay, Cc, RequisitionPriority.Normal, "j", "a1", "a2", [], Now);
        Assert.True(r.IsFailure);
        Assert.Equal("purchases.lines_required", r.Error.Code);
    }

    [Fact]
    public void Create_sem_justificativa_falha()
    {
        var r = PurchaseRequisition.Create(Company, "sol", Pay, Cc, RequisitionPriority.Normal, "  ", "a1", "a2", Lines, Now);
        Assert.True(r.IsFailure);
        Assert.Equal("purchases.justification_required", r.Error.Code);
    }

    [Fact]
    public void Create_com_quantidade_nao_positiva_falha()
    {
        var r = PurchaseRequisition.Create(Company, "sol", Pay, Cc, RequisitionPriority.Normal, "j", "a1", "a2", [("X", 0m, "un")], Now);
        Assert.True(r.IsFailure);
        Assert.Equal("purchases.qty_invalid", r.Error.Code);
    }

    [Fact]
    public void SoD_requisitante_nao_pode_ser_aprovador()
    {
        var r = PurchaseRequisition.Create(Company, "sol", Pay, Cc, RequisitionPriority.Normal, "j", "sol", "a2", Lines, Now);
        Assert.True(r.IsFailure);
        Assert.Equal("purchases.sod_violation", r.Error.Code);
    }

    [Fact]
    public void Aprovadores_devem_ser_distintos()
    {
        var r = PurchaseRequisition.Create(Company, "sol", Pay, Cc, RequisitionPriority.Normal, "j", "a1", "a1", Lines, Now);
        Assert.True(r.IsFailure);
        Assert.Equal("purchases.approvers_distinct", r.Error.Code);
    }

    [Fact]
    public void Nova_requisicao_comeca_em_draft() => Assert.Equal(RequisitionStatus.Draft, New().Status);

    [Fact]
    public void Fluxo_dois_niveis_ate_aprovado()
    {
        var req = Submitted();
        Assert.True(req.ApproveLevel1("aprov1", Now).IsSuccess);
        Assert.Equal(RequisitionStatus.ApprovedLevel1, req.Status);
        Assert.True(req.ApproveLevel2("aprov2", Now).IsSuccess);
        Assert.Equal(RequisitionStatus.Approved, req.Status);
        Assert.Equal("aprov1", req.Level1DecidedBySubject);
        Assert.Equal("aprov2", req.Level2DecidedBySubject);
    }

    [Fact]
    public void Nivel1_por_aprovador_errado_falha()
    {
        var result = Submitted().ApproveLevel1("intruso", Now);
        Assert.True(result.IsFailure);
        Assert.Equal("purchases.wrong_approver", result.Error.Code);
    }

    [Fact]
    public void Nivel2_antes_do_nivel1_falha()
    {
        var result = Submitted().ApproveLevel2("aprov2", Now);
        Assert.True(result.IsFailure);
        Assert.Equal("purchases.not_level1", result.Error.Code);
    }

    [Fact]
    public void ApproveLevel1_sem_submeter_falha()
    {
        var result = New().ApproveLevel1("aprov1", Now);
        Assert.True(result.IsFailure);
        Assert.Equal("purchases.not_submitted", result.Error.Code);
    }

    [Fact]
    public void Reject_exige_justificativa()
    {
        var result = Submitted().Reject("aprov1", "  ", Now);
        Assert.True(result.IsFailure);
        Assert.Equal("purchases.reject_note_required", result.Error.Code);
    }

    [Fact]
    public void Reject_por_aprovador_da_etapa()
    {
        var req = Submitted();
        var result = req.Reject("aprov1", "fora do orçamento", Now);
        Assert.True(result.IsSuccess);
        Assert.Equal(RequisitionStatus.Rejected, req.Status);
        Assert.Equal("fora do orçamento", req.DecisionNote);
    }
}

public class PurchaseOrderTests
{
    private static readonly CompanyId Company = CompanyId.New();
    private static readonly OrderTotalsInput ZeroTotals = new(0m, 0m, 0m, 0m, null);

    private static OrderLineInput Line(string code, decimal qty, decimal price, decimal irrf = 0m, decimal iss = 0m) =>
        new(code, code, qty, "un", price, irrf, iss, null);

    [Fact]
    public void Issue_copia_as_linhas_com_precos_e_numero()
    {
        var order = PurchaseOrder.Issue(
            Company, 664, RequisitionId.New(), PayingCompanyId.New(), SupplierId.New(),
            "A Vista", "Depósito Bancário", ZeroTotals, "comprador", DateTimeOffset.UtcNow,
            [Line("PARAFUSO", 100m, 2.50m), Line("PORCA", 50m, 1.00m)]).Value;

        Assert.Equal(PurchaseOrderStatus.Issued, order.Status);
        Assert.Equal(664, order.Number);
        Assert.Equal(2, order.Lines.Count);
        Assert.Contains(order.Lines, l => l.ItemCode == "PARAFUSO" && l.Quantity == 100m && l.UnitPrice == 2.50m);
    }

    [Fact]
    public void Issue_calcula_valor_de_servico_e_liquido()
    {
        var totals = new OrderTotalsInput(IpiValue: 10m, IcmsValue: 0m, DiscountValue: 5m, OtherExpenses: 3m, FreightTerms: null);
        var order = PurchaseOrder.Issue(
            Company, 1, RequisitionId.New(), PayingCompanyId.New(), SupplierId.New(),
            "A Vista", "Depósito Bancário", totals, "comprador", DateTimeOffset.UtcNow,
            [Line("A", 10m, 2m), Line("B", 5m, 4m)]).Value;

        // Produtos = 10*2 + 5*4 = 40; Líquido = 40 + 10 - 5 + 3 = 48
        Assert.Equal(40m, order.ProductsValue);
        Assert.Equal(48m, order.NetValue);
    }

    [Fact]
    public void Issue_calcula_irrf_e_iss_por_linha()
    {
        var order = PurchaseOrder.Issue(
            Company, 1, RequisitionId.New(), PayingCompanyId.New(), SupplierId.New(),
            "A Vista", "Depósito Bancário", ZeroTotals, "comprador", DateTimeOffset.UtcNow,
            [Line("SERV", 1m, 1000m, irrf: 1.5m, iss: 5m)]).Value;

        var line = Assert.Single(order.Lines);
        Assert.Equal(1000m, line.ServiceValue);
        Assert.Equal(15m, line.IrrfValue);   // 1.5% de 1000
        Assert.Equal(50m, line.IssValue);    // 5% de 1000
    }

    [Fact]
    public void Issue_sem_linhas_falha()
    {
        var result = PurchaseOrder.Issue(
            Company, 1, RequisitionId.New(), PayingCompanyId.New(), SupplierId.New(),
            "A Vista", "Depósito Bancário", ZeroTotals, "comprador", DateTimeOffset.UtcNow, []);
        Assert.True(result.IsFailure);
        Assert.Equal("purchases.order.no_lines", result.Error.Code);
    }

    [Fact]
    public void Issue_com_numero_invalido_falha()
    {
        var result = PurchaseOrder.Issue(
            Company, 0, RequisitionId.New(), PayingCompanyId.New(), SupplierId.New(),
            "A Vista", "Depósito Bancário", ZeroTotals, "comprador", DateTimeOffset.UtcNow,
            [Line("A", 1m, 1m)]);
        Assert.True(result.IsFailure);
        Assert.Equal("purchases.order.number_required", result.Error.Code);
    }

    private static PurchaseOrder Emitida() => PurchaseOrder.Issue(
        Company, 1, RequisitionId.New(), PayingCompanyId.New(), SupplierId.New(),
        "A Vista", "Depósito Bancário", ZeroTotals, "comprador", DateTimeOffset.UtcNow,
        [Line("A", 1m, 10m)]).Value;

    [Fact]
    public void Cancel_marca_como_cancelada_e_registra_motivo()
    {
        var order = Emitida();
        var now = DateTimeOffset.UtcNow;
        var result = order.Cancel("gestor", "Fornecedor não atende mais", now);

        Assert.True(result.IsSuccess);
        Assert.Equal(PurchaseOrderStatus.Cancelled, order.Status);
        Assert.Equal("gestor", order.CancelledBySubject);
        Assert.Equal("Fornecedor não atende mais", order.CancelReason);
        Assert.Equal(now, order.CancelledAt);
    }

    [Fact]
    public void Cancel_exige_motivo()
    {
        var result = Emitida().Cancel("gestor", "  ", DateTimeOffset.UtcNow);
        Assert.True(result.IsFailure);
        Assert.Equal("purchases.order.cancel_reason_required", result.Error.Code);
    }

    [Fact]
    public void Cancel_de_OC_ja_cancelada_falha()
    {
        var order = Emitida();
        order.Cancel("gestor", "motivo", DateTimeOffset.UtcNow);
        var second = order.Cancel("gestor", "de novo", DateTimeOffset.UtcNow);

        Assert.True(second.IsFailure);
        Assert.Equal("purchases.order.not_issued", second.Error.Code);
    }
}

public class PayingCompanyTests
{
    private static readonly CompanyId Company = CompanyId.New();

    [Fact]
    public void Create_exige_cnpj()
    {
        var result = PayingCompany.Create(Company, "EP1", "Brilho Ltda", "", null, null, null, null, null, null, null, null);
        Assert.True(result.IsFailure);
        Assert.Equal("purchases.paying_company.taxid_required", result.Error.Code);
    }

    [Fact]
    public void Create_normaliza_codigo_e_uf()
    {
        var pc = PayingCompany.Create(
            Company, "ep-1", "Brilho Ltda", "05.345.258/0004-96", "16.411.708-3",
            "R Manoel Cesar", "Distrito Industrial", "Alhandra", "pb", "58320-000", "81 3243-8500", "compras@x.com").Value;

        Assert.Equal("EP-1", pc.Code);
        Assert.Equal("PB", pc.State);
        Assert.Equal(PayingCompanyStatus.Active, pc.Status);
    }
}

public class RequisitionFulfillmentTests
{
    private static readonly CompanyId Company = CompanyId.New();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static PurchaseRequisition Approved()
    {
        var req = PurchaseRequisition.Create(Company, "sol", PayingCompanyId.New(), CostCenterId.New(),
            RequisitionPriority.Normal, "j", "a1", "a2", [("BOTA", 2m, "un")], Now).Value;
        req.Submit();
        req.ApproveLevel1("a1", Now);
        req.ApproveLevel2("a2", Now);
        return req;
    }

    [Fact]
    public void Pedido_aprovado_pode_ser_atendido_pelo_estoque()
    {
        var req = Approved();
        Assert.True(req.MarkFulfilledFromStock(Now).IsSuccess);
        Assert.Equal(RequisitionStatus.FulfilledFromStock, req.Status);
    }

    [Fact]
    public void Atender_pelo_estoque_sem_aprovacao_falha()
    {
        var req = PurchaseRequisition.Create(Company, "sol", PayingCompanyId.New(), CostCenterId.New(),
            RequisitionPriority.Normal, "j", "a1", "a2", [("BOTA", 2m, "un")], Now).Value;
        req.Submit();
        var r = req.MarkFulfilledFromStock(Now);
        Assert.True(r.IsFailure);
        Assert.Equal("purchases.not_approved", r.Error.Code);
    }

    [Fact]
    public void Atendido_pelo_estoque_nao_pode_ser_atendido_de_novo()
    {
        var req = Approved();
        req.MarkFulfilledFromStock(Now);
        Assert.True(req.MarkFulfilledFromStock(Now).IsFailure);
    }
}

/// <summary>
/// v3 — compra dividida: uma requisição aprovada pode render VÁRIAS OCs, cada uma cobrindo um
/// subconjunto de itens (um fornecedor por família, por exemplo). A linha já pedida não entra em
/// outra OC, e cancelar a OC devolve os itens dela para a fila de compra.
/// </summary>
public class RequisitionSplitOrderTests
{
    private static readonly CompanyId Company = CompanyId.New();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    /// <summary>Requisição aprovada com 2 EPIs e 2 itens de limpeza (cenário multi-família).</summary>
    private static PurchaseRequisition Aprovada()
    {
        var req = PurchaseRequisition.Create(Company, "sol", PayingCompanyId.New(), CostCenterId.New(),
            RequisitionPriority.Normal, "multi-família", "a1", "a2",
            [("BOTINA", 2m, "par"), ("LUVA", 5m, "par"), ("DETERGENTE", 10m, "un"), ("ALVEJANTE", 4m, "un")],
            Now).Value;
        req.Submit();
        req.ApproveLevel1("a1", Now);
        req.ApproveLevel2("a2", Now);
        return req;
    }

    private static Guid[] Ids(PurchaseRequisition req, params string[] itemCodes) =>
        req.Lines.Where(l => itemCodes.Contains(l.ItemCode)).Select(l => l.Id).ToArray();

    [Fact]
    public void Primeira_OC_parcial_deixa_requisicao_atendida_parcialmente()
    {
        var req = Aprovada();
        var oc1 = Guid.NewGuid();

        Assert.True(req.MarkLinesOrdered(Ids(req, "BOTINA", "LUVA"), oc1).IsSuccess);

        Assert.Equal(RequisitionStatus.PartiallyOrdered, req.Status);
        Assert.Equal(2, req.Lines.Count(l => l.PurchaseOrderId == oc1));
        Assert.Equal(2, req.Lines.Count(l => l.IsPending));
    }

    [Fact]
    public void Segunda_OC_com_o_restante_fecha_a_requisicao()
    {
        var req = Aprovada();
        var (oc1, oc2) = (Guid.NewGuid(), Guid.NewGuid());

        req.MarkLinesOrdered(Ids(req, "BOTINA", "LUVA"), oc1);
        Assert.True(req.MarkLinesOrdered(Ids(req, "DETERGENTE", "ALVEJANTE"), oc2).IsSuccess);

        Assert.Equal(RequisitionStatus.Ordered, req.Status);
        // Rastreabilidade: cada item aponta para a OC que o comprou.
        Assert.Equal(oc1, req.Lines.Single(l => l.ItemCode == "BOTINA").PurchaseOrderId);
        Assert.Equal(oc2, req.Lines.Single(l => l.ItemCode == "ALVEJANTE").PurchaseOrderId);
    }

    [Fact]
    public void Item_ja_pedido_nao_entra_em_outra_OC()
    {
        var req = Aprovada();
        req.MarkLinesOrdered(Ids(req, "BOTINA"), Guid.NewGuid());

        var r = req.MarkLinesOrdered(Ids(req, "BOTINA"), Guid.NewGuid());

        Assert.True(r.IsFailure);
        Assert.Equal("purchases.order.line_already_ordered", r.Error.Code);
    }

    [Fact]
    public void Cancelar_a_OC_devolve_os_itens_para_a_fila_de_compra()
    {
        var req = Aprovada();
        var (oc1, oc2) = (Guid.NewGuid(), Guid.NewGuid());
        req.MarkLinesOrdered(Ids(req, "BOTINA", "LUVA"), oc1);
        req.MarkLinesOrdered(Ids(req, "DETERGENTE", "ALVEJANTE"), oc2);
        Assert.Equal(RequisitionStatus.Ordered, req.Status);

        req.ReleaseOrderLines(oc2);

        Assert.Equal(RequisitionStatus.PartiallyOrdered, req.Status);
        Assert.Equal(2, req.Lines.Count(l => l.IsPending));

        // Cancelando também a primeira, a requisição volta a Aprovada (nada pedido).
        req.ReleaseOrderLines(oc1);
        Assert.Equal(RequisitionStatus.Approved, req.Status);
        Assert.All(req.Lines, l => Assert.True(l.IsPending));
    }

    [Fact]
    public void Requisicao_nao_aprovada_nao_gera_OC()
    {
        var req = PurchaseRequisition.Create(Company, "sol", PayingCompanyId.New(), CostCenterId.New(),
            RequisitionPriority.Normal, "j", "a1", "a2", [("BOTINA", 1m, "par")], Now).Value;
        req.Submit();

        var r = req.MarkLinesOrdered([req.Lines[0].Id], Guid.NewGuid());

        Assert.True(r.IsFailure);
        Assert.Equal("purchases.order.not_approved", r.Error.Code);
    }

    [Fact]
    public void Linha_de_outra_requisicao_e_recusada()
    {
        var req = Aprovada();
        var r = req.MarkLinesOrdered([Guid.NewGuid()], Guid.NewGuid());

        Assert.True(r.IsFailure);
        Assert.Equal("purchases.order.line_not_found", r.Error.Code);
    }
}

/// <summary>
/// MMS-005 — conferência física na doca: o que entra no estoque é o LÍQUIDO (recebido − avariado),
/// e toda não-conformidade exige classificação + descrição (gatilho da tratativa com o fornecedor).
/// </summary>
public class GoodsReceiptTests
{
    private static readonly CompanyId Company = CompanyId.New();
    private static readonly PurchaseOrderId Order = PurchaseOrderId.New();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static ReceiptLineInput Linha(
        decimal pedida, decimal recebida, decimal avariada,
        ReceiptOccurrence ocorrencia = ReceiptOccurrence.None, string? nota = null, decimal jaRecebida = 0m) =>
        new(Guid.NewGuid(), "BOTINA", "par", pedida, jaRecebida, recebida, avariada, ocorrencia, nota);

    private static Result<GoodsReceipt> Registrar(params ReceiptLineInput[] linhas) =>
        GoodsReceipt.Register(Company, Order, "NF-1234", new DateOnly(2026, 9, 19), "conferente", Now, null, linhas);

    [Fact]
    public void Entrada_no_estoque_e_o_liquido_recebido_menos_avariado()
    {
        // Cenário de aceite: 50 pedidas, 50 chegaram, 2 avariadas → 48 entram no estoque.
        var r = Registrar(Linha(50m, 50m, 2m, ReceiptOccurrence.Avaria, "2 caixas amassadas no transporte"));

        Assert.True(r.IsSuccess);
        var linha = Assert.Single(r.Value.Lines);
        Assert.Equal(48m, linha.NetQuantity);
        Assert.True(r.Value.HasOccurrence);
    }

    [Fact]
    public void Avaria_sem_classificacao_e_recusada()
    {
        var r = Registrar(Linha(50m, 50m, 2m));

        Assert.True(r.IsFailure);
        Assert.Equal("purchases.receipt.occurrence_required", r.Error.Code);
    }

    [Fact]
    public void Ocorrencia_sem_descricao_e_recusada()
    {
        var r = Registrar(Linha(50m, 50m, 2m, ReceiptOccurrence.Avaria, nota: "   "));

        Assert.True(r.IsFailure);
        Assert.Equal("purchases.receipt.occurrence_note_required", r.Error.Code);
    }

    [Fact]
    public void Avariado_nao_pode_passar_do_recebido()
    {
        var r = Registrar(Linha(50m, 10m, 11m, ReceiptOccurrence.Avaria, "erro de digitação"));

        Assert.True(r.IsFailure);
        Assert.Equal("purchases.receipt.damaged_exceeds_received", r.Error.Code);
    }

    [Fact]
    public void Receber_mais_que_o_pedido_exige_ocorrencia_de_excesso()
    {
        var semOcorrencia = Registrar(Linha(50m, 60m, 0m));
        Assert.True(semOcorrencia.IsFailure);
        Assert.Equal("purchases.receipt.exceeds_ordered", semOcorrencia.Error.Code);

        var declarado = Registrar(Linha(50m, 60m, 0m, ReceiptOccurrence.Excesso, "fornecedor enviou a mais"));
        Assert.True(declarado.IsSuccess);
    }

    [Fact]
    public void Entrega_parcial_considera_o_que_ja_entrou_antes()
    {
        // 50 pedidas, 30 já recebidas: cabem 20. Pedir 25 estoura o pendente.
        var estoura = Registrar(Linha(50m, 25m, 0m, jaRecebida: 30m));
        Assert.True(estoura.IsFailure);
        Assert.Equal("purchases.receipt.exceeds_ordered", estoura.Error.Code);

        var cabe = Registrar(Linha(50m, 20m, 0m, jaRecebida: 30m));
        Assert.True(cabe.IsSuccess);
    }

    [Fact]
    public void Nota_fiscal_e_quantidade_sao_obrigatorias()
    {
        var semNota = GoodsReceipt.Register(Company, Order, "  ", null, "conferente", Now, null, [Linha(10m, 10m, 0m)]);
        Assert.True(semNota.IsFailure);
        Assert.Equal("purchases.receipt.invoice_required", semNota.Error.Code);

        var semQuantidade = Registrar(Linha(10m, 0m, 0m));
        Assert.True(semQuantidade.IsFailure);
        Assert.Equal("purchases.receipt.lines_required", semQuantidade.Error.Code);
    }

    [Fact]
    public void Marcar_entrada_no_estoque_e_idempotente()
    {
        var receipt = Registrar(Linha(10m, 10m, 0m)).Value;
        Assert.False(receipt.StockPosted);

        Assert.True(receipt.MarkStockPosted(Now).IsSuccess);
        Assert.True(receipt.StockPosted);
        Assert.Equal(Now, receipt.StockPostedAt);

        Assert.True(receipt.MarkStockPosted(Now.AddHours(1)).IsSuccess);
        Assert.Equal(Now, receipt.StockPostedAt);   // não sobrescreve o primeiro registro
    }
}

/// <summary>
/// Fase 03 — OTIF do fornecedor. Mede o que foi combinado: pontualidade só conta onde a OC trouxe
/// prazo; integralidade compara o LÍQUIDO aproveitável com o pedido (avaria derruba o In-Full).
/// </summary>
public class SupplierScorecardTests
{
    private static readonly Guid Fornecedor = Guid.NewGuid();
    private static readonly DateOnly Prazo = new(2026, 9, 10);

    private static DeliveryFact Entrega(
        decimal pedida, decimal recebida, decimal avariada, DateOnly entregueEm,
        bool ocorrencia = false, string periodo = "2026-09") =>
        new(Fornecedor, periodo, pedida, recebida, avariada, ocorrencia, Prazo, entregueEm);

    /// <summary>OC que saiu sem data prometida — não há prazo contra o que medir.</summary>
    private static DeliveryFact EntregaSemPrazo(
        decimal pedida, decimal recebida, DateOnly entregueEm, string periodo = "2026-09") =>
        new(Fornecedor, periodo, pedida, recebida, 0m, false, null, entregueEm);

    private static SupplierScore Score(params DeliveryFact[] fatos) =>
        SupplierScorecard.Overall(Fornecedor, "12m", fatos);

    [Fact]
    public void Entrega_no_prazo_e_completa_e_OTIF_cheio()
    {
        var s = Score(Entrega(100m, 100m, 0m, entregueEm: Prazo));

        Assert.Equal(100m, s.OnTimeRate);
        Assert.Equal(100m, s.InFullRate);
        Assert.Equal(100m, s.OtifIndex);
        Assert.Equal(0m, s.DamageRate);
        Assert.Equal(SupplierTier.Ouro, s.Tier);
    }

    [Fact]
    public void Um_dia_de_atraso_com_10_por_cento_avariado_penaliza_os_dois_indices()
    {
        // Critério de aceite da Fase 03: atraso de 1 dia + 10% de avaria.
        var s = Score(Entrega(100m, 100m, 10m, entregueEm: Prazo.AddDays(1)));

        Assert.Equal(0m, s.OnTimeRate);    // chegou depois do prazo
        Assert.Equal(0m, s.InFullRate);    // líquido 90 < 100 pedidas
        Assert.Equal(0m, s.OtifIndex);
        Assert.Equal(10m, s.DamageRate);   // 10 de 100 recebidas
        Assert.Equal(SupplierTier.Critico, s.Tier);
    }

    [Fact]
    public void Entrega_no_ultimo_dia_do_prazo_ainda_e_pontual()
    {
        var s = Score(Entrega(10m, 10m, 0m, entregueEm: Prazo));
        Assert.Equal(100m, s.OnTimeRate);
    }

    [Fact]
    public void Linha_sem_prazo_na_OC_nao_infla_a_pontualidade()
    {
        // Sem data prometida não há o que medir: fica de fora do On-Time e do OTIF, mas conta no In-Full.
        var s = Score(
            EntregaSemPrazo(10m, 10m, entregueEm: Prazo),
            Entrega(10m, 10m, 0m, entregueEm: Prazo.AddDays(5)));   // esta tem prazo e atrasou

        Assert.Equal(2, s.LinesEvaluated);
        Assert.Equal(1, s.LinesWithDeadline);
        Assert.Equal(1, s.LinesWithoutDeadline);
        Assert.Equal(0m, s.OnTimeRate);     // a única linha mensurável atrasou
        Assert.Equal(100m, s.InFullRate);   // ambas vieram completas
    }

    [Fact]
    public void Sem_nenhuma_linha_com_prazo_nao_ha_classificacao()
    {
        var s = Score(EntregaSemPrazo(10m, 10m, entregueEm: Prazo));

        Assert.Equal(SupplierTier.SemDados, s.Tier);
        Assert.Equal(0m, s.OtifIndex);
    }

    [Fact]
    public void Faixas_de_desempenho_seguem_o_OTIF()
    {
        DeliveryFact ok = Entrega(1m, 1m, 0m, entregueEm: Prazo);
        DeliveryFact ruim = Entrega(1m, 1m, 0m, entregueEm: Prazo.AddDays(2));

        // 19 de 20 no prazo = 95% → Ouro
        Assert.Equal(SupplierTier.Ouro, Score([.. Enumerable.Repeat(ok, 19), ruim]).Tier);
        // 9 de 10 = 90% → Prata
        Assert.Equal(SupplierTier.Prata, Score([.. Enumerable.Repeat(ok, 9), ruim]).Tier);
        // 3 de 4 = 75% → Bronze
        Assert.Equal(SupplierTier.Bronze, Score([.. Enumerable.Repeat(ok, 3), ruim]).Tier);
        // 1 de 2 = 50% → Crítico
        Assert.Equal(SupplierTier.Critico, Score(ok, ruim).Tier);
    }

    [Fact]
    public void Score_por_periodo_separa_os_meses()
    {
        var porPeriodo = SupplierScorecard.ByPeriod([
            Entrega(10m, 10m, 0m, entregueEm: Prazo, periodo: "2026-09"),
            Entrega(10m, 10m, 0m, entregueEm: Prazo.AddDays(40), periodo: "2026-10"),
        ]);

        Assert.Equal(2, porPeriodo.Count);
        Assert.Equal("2026-10", porPeriodo[0].Period);   // mais recente primeiro
        Assert.Equal(100m, porPeriodo.Single(p => p.Period == "2026-09").OnTimeRate);
        Assert.Equal(0m, porPeriodo.Single(p => p.Period == "2026-10").OnTimeRate);
    }

    [Fact]
    public void Ocorrencias_sao_contadas_para_o_indice_de_qualidade()
    {
        var s = Score(
            Entrega(10m, 10m, 2m, entregueEm: Prazo, ocorrencia: true),
            Entrega(10m, 10m, 0m, entregueEm: Prazo));

        Assert.Equal(1, s.Occurrences);
        Assert.Equal(10m, s.DamageRate);   // 2 avariadas de 20 recebidas
    }
}
