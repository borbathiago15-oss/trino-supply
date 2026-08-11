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
