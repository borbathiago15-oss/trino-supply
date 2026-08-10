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

    [Fact]
    public void Issue_copia_as_linhas()
    {
        var order = PurchaseOrder.Issue(
            Company, RequisitionId.New(), SupplierId.New(), "comprador", DateTimeOffset.UtcNow,
            [("PARAFUSO", 100m, "un"), ("PORCA", 50m, "un")]).Value;

        Assert.Equal(PurchaseOrderStatus.Issued, order.Status);
        Assert.Equal(2, order.Lines.Count);
        Assert.Contains(order.Lines, l => l.ItemCode == "PARAFUSO" && l.Quantity == 100m);
    }

    [Fact]
    public void Issue_sem_linhas_falha()
    {
        var result = PurchaseOrder.Issue(
            Company, RequisitionId.New(), SupplierId.New(), "comprador", DateTimeOffset.UtcNow, []);
        Assert.True(result.IsFailure);
        Assert.Equal("purchases.order.no_lines", result.Error.Code);
    }
}
