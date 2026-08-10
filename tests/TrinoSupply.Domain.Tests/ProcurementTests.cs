using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.Procurement.Domain;
using Xunit;

namespace TrinoSupply.Domain.Tests;

public class PurchaseRequisitionTests
{
    private static readonly CompanyId Company = CompanyId.New();
    private static readonly (string, decimal, string)[] Lines = [("PARAFUSO", 10m, "un")];

    private static PurchaseRequisition Submitted(string requester = "comprador")
    {
        var req = PurchaseRequisition.Create(Company, requester, Lines, DateTimeOffset.UtcNow).Value;
        req.Submit();
        return req;
    }

    [Fact]
    public void Create_sem_linhas_falha()
    {
        var result = PurchaseRequisition.Create(Company, "comprador", [], DateTimeOffset.UtcNow);
        Assert.True(result.IsFailure);
        Assert.Equal("purchases.lines_required", result.Error.Code);
    }

    [Fact]
    public void Create_com_quantidade_nao_positiva_falha()
    {
        var result = PurchaseRequisition.Create(Company, "comprador", [("X", 0m, "un")], DateTimeOffset.UtcNow);
        Assert.True(result.IsFailure);
        Assert.Equal("purchases.qty_invalid", result.Error.Code);
    }

    [Fact]
    public void Nova_requisicao_comeca_em_draft()
    {
        var req = PurchaseRequisition.Create(Company, "comprador", Lines, DateTimeOffset.UtcNow).Value;
        Assert.Equal(RequisitionStatus.Draft, req.Status);
    }

    [Fact]
    public void Approve_sem_submeter_falha()
    {
        var req = PurchaseRequisition.Create(Company, "comprador", Lines, DateTimeOffset.UtcNow).Value;
        var result = req.Approve("aprovador", DateTimeOffset.UtcNow);
        Assert.True(result.IsFailure);
        Assert.Equal("purchases.not_submitted", result.Error.Code);
    }

    [Fact]
    public void SoD_requisitante_nao_aprova_a_propria_requisicao()
    {
        var req = Submitted("comprador");
        var result = req.Approve("comprador", DateTimeOffset.UtcNow); // mesmo subject

        Assert.True(result.IsFailure);
        Assert.Equal("purchases.sod_violation", result.Error.Code);
        Assert.Equal(RequisitionStatus.Submitted, req.Status); // não mudou
    }

    [Fact]
    public void Aprovador_distinto_aprova()
    {
        var req = Submitted("comprador");
        var result = req.Approve("aprovador", DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(RequisitionStatus.Approved, req.Status);
        Assert.Equal("aprovador", req.DecidedBySubject);
    }

    [Fact]
    public void Nao_decide_duas_vezes()
    {
        var req = Submitted("comprador");
        req.Approve("aprovador", DateTimeOffset.UtcNow);
        var second = req.Approve("outro", DateTimeOffset.UtcNow);

        Assert.True(second.IsFailure);
        Assert.Equal("purchases.not_submitted", second.Error.Code);
    }

    [Fact]
    public void Reject_tambem_respeita_SoD()
    {
        var req = Submitted("comprador");
        var result = req.Reject("comprador", "não", DateTimeOffset.UtcNow);
        Assert.True(result.IsFailure);
        Assert.Equal("purchases.sod_violation", result.Error.Code);
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
