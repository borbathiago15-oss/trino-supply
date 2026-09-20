using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// A linha do tempo do solicitante é derivada: cada combinação de SC, processo e pedido
/// tem uma etapa atual, uma frase e um "com quem". Aqui se testa a regra pura.
/// </summary>
public class AcompanhamentoDaScTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    private static PurchaseRequisition Sc(RequisitionStatus status) => new()
    {
        Number = "PR-2026-000001", Status = status, CostCenter = "BAH-001", RequesterLabel = "Ana",
        SubmittedAt = T0, CreatedAt = T0, UpdatedAt = T0,
    };

    private static string Situacao(Acompanhamento a, string chave) => a.Etapas.Single(e => e.Chave == chave).Situacao;

    [Fact]
    public void Rascunho_e_devolvida_estao_com_o_solicitante()
    {
        var rascunho = AcompanhamentoDaSc.Montar(Sc(RequisitionStatus.Draft), null, null);
        Assert.Equal("enviada", rascunho.EtapaAtual);
        Assert.True(rascunho.PrecisaDoSolicitante);
        Assert.Contains("Rascunho", rascunho.Frase);

        var sc = Sc(RequisitionStatus.Returned);
        sc.DecidedByLabel = "Gustavo"; sc.DecisionReason = "faltou o orçamento"; sc.DecidedAt = T0.AddDays(1);
        var devolvida = AcompanhamentoDaSc.Montar(sc, null, null);
        Assert.True(devolvida.PrecisaDoSolicitante);
        Assert.Equal("faltou o orçamento", devolvida.Motivo);
        Assert.Contains("Devolvida por Gustavo", devolvida.Frase);
        Assert.Equal(AcompanhamentoDaSc.Parada, Situacao(devolvida, "enviada"));
        Assert.Equal("Ana", devolvida.ComQuem);
    }

    [Fact]
    public void Enviada_sem_comprador_esta_com_Suprimentos_e_com_comprador_designado_esta_com_ele()
    {
        var sc = Sc(RequisitionStatus.Submitted);
        var fila = AcompanhamentoDaSc.Montar(sc, null, null);
        Assert.Equal("comprador", fila.EtapaAtual);
        Assert.Equal("Suprimentos", fila.ComQuem);
        Assert.Contains("aguardando a designação", fila.Frase);
        Assert.Equal(T0, fila.Desde);
        Assert.Equal(AcompanhamentoDaSc.Feita, Situacao(fila, "enviada"));

        sc.AssignedToLabel = "Carla"; sc.AssignedAt = T0.AddDays(1);
        var designada = AcompanhamentoDaSc.Montar(sc, null, null);
        Assert.Equal("cotacao", designada.EtapaAtual);
        Assert.Equal("Carla", designada.ComQuem);
        Assert.Equal(T0.AddDays(1), designada.Desde);
        Assert.False(designada.PrecisaDoSolicitante);
    }

    [Fact]
    public void Em_cotacao_a_frase_conta_convidados_e_respostas_e_na_alcada_diz_de_quem_espera()
    {
        var sc = Sc(RequisitionStatus.Submitted);
        var q = new Quotation
        {
            Number = "RFQ-2026-000009", Status = QuotationStatus.Open, SourcePrId = sc.Id, CreatedByLabel = "Carla",
            CreatedAt = T0.AddDays(2), UpdatedAt = T0.AddDays(2),
            Suppliers = [new QuotationSupplier { SupplierId = Guid.NewGuid() }, new QuotationSupplier { SupplierId = Guid.NewGuid() }, new QuotationSupplier { SupplierId = Guid.NewGuid() }],
            Proposals = [new Proposal { SupplierId = Guid.NewGuid(), SupplierName = "Alfa" }],
        };
        var cotando = AcompanhamentoDaSc.Montar(sc, q, null);
        Assert.Equal("cotacao", cotando.EtapaAtual);
        Assert.Contains("3 fornecedor(es)", cotando.Frase);
        Assert.Contains("1 já responderam", cotando.Frase);
        Assert.Equal("RFQ-2026-000009", cotando.QuotationNumber);

        // Nível 1: espera desde a escolha, de quem está na lista do centro
        q.Status = QuotationStatus.AwaitingManager; q.SelectedAt = T0.AddDays(4);
        q.WinnerProposalId = q.Proposals[0].Id;
        var nivel1 = AcompanhamentoDaSc.Montar(sc, q, null, ["Gustavo", "Helena"]);
        Assert.Equal("aprovacao", nivel1.EtapaAtual);
        Assert.Equal("Gustavo, Helena", nivel1.ComQuem);
        Assert.Equal(T0.AddDays(4), nivel1.Desde);
        Assert.Contains("Nível 1", nivel1.Frase);
        Assert.Contains("Fornecedor escolhido: Alfa", nivel1.Frase);
        Assert.Equal(AcompanhamentoDaSc.Feita, Situacao(nivel1, "cotacao"));

        // Nível 2 conta desde o Nível 1; centro sem aprovador diz isso, em vez de "aguardando ninguém"
        q.Status = QuotationStatus.AwaitingDirector; q.ManagerApprovedAt = T0.AddDays(5);
        var nivel2 = AcompanhamentoDaSc.Montar(sc, q, null, []);
        Assert.Equal(T0.AddDays(5), nivel2.Desde);
        Assert.Null(nivel2.ComQuem);
        Assert.Contains("sem aprovador cadastrado", nivel2.Frase);
    }

    [Fact]
    public void Aprovada_o_pedido_diz_o_que_falta_e_quando_chega()
    {
        var sc = Sc(RequisitionStatus.Approved);
        var q = new Quotation
        {
            Status = QuotationStatus.ApprovedForIssue, SourcePrId = sc.Id, CreatedByLabel = "Carla",
            SelectedAt = T0.AddDays(4), ManagerApprovedAt = T0.AddDays(5), DirectorApprovedAt = T0.AddDays(6),
            DirectorApprovedByLabel = "Diana", CreatedAt = T0.AddDays(2), UpdatedAt = T0.AddDays(6),
        };
        var semPedido = AcompanhamentoDaSc.Montar(sc, q, null);
        Assert.Equal("pedido", semPedido.EtapaAtual);
        Assert.Equal("Carla", semPedido.ComQuem);
        Assert.Equal(AcompanhamentoDaSc.Feita, Situacao(semPedido, "aprovacao"));

        var o = new PurchaseOrder { Number = "PO-2026-000003", SupplierName = "Alfa", Status = PurchaseOrderStatus.Issued, CreatedAt = T0.AddDays(6), QuotationId = q.Id };
        var semOc = AcompanhamentoDaSc.Montar(sc, q, o);
        Assert.Equal("pedido", semOc.EtapaAtual);
        Assert.Contains("registrando a O.C.", semOc.Frase);
        Assert.Equal("PO-2026-000003", semOc.PurchaseOrderNumber);

        o.ErpNumber = "OC-1"; o.PromisedDate = new DateOnly(2026, 9, 28);
        var faturar = AcompanhamentoDaSc.Montar(sc, q, o);
        Assert.Equal("entrega", faturar.EtapaAtual);
        Assert.Equal("Alfa", faturar.ComQuem);
        Assert.Contains("aguardando o faturamento", faturar.Frase);
        Assert.Equal(new DateOnly(2026, 9, 28), faturar.Previsao);
        Assert.Contains("Chega até 28/09/2026", faturar.Frase);

        o.Invoices.Add(new PurchaseOrderInvoice { Number = "1" });
        Assert.Contains("aguardando a entrega", AcompanhamentoDaSc.Montar(sc, q, o).Frase);

        o.Status = PurchaseOrderStatus.Received; o.DeliveryCompletedAt = T0.AddDays(20);
        var entregue = AcompanhamentoDaSc.Montar(sc, q, o);
        Assert.Equal(AcompanhamentoDaSc.Feita, Situacao(entregue, "entrega"));
        Assert.Contains("Entregue em 21/09/2026", entregue.Frase);
        Assert.All(entregue.Etapas, e => Assert.Equal(AcompanhamentoDaSc.Feita, e.Situacao));
    }

    [Fact]
    public void Rejeicao_e_cancelamento_param_a_linha_com_o_motivo()
    {
        var sc = Sc(RequisitionStatus.Rejected);
        sc.DecisionReason = "fora do orçamento do ano"; sc.DecidedByLabel = "Diana"; sc.DecidedAt = T0.AddDays(3);
        var rejeitada = AcompanhamentoDaSc.Montar(sc, null, null);
        Assert.Equal(AcompanhamentoDaSc.Parada, Situacao(rejeitada, "aprovacao"));
        Assert.Equal("fora do orçamento do ano", rejeitada.Motivo);
        Assert.False(rejeitada.PrecisaDoSolicitante);

        var q = new Quotation { Status = QuotationStatus.Cancelled, SourcePrId = sc.Id, CreatedByLabel = "Carla", DecisionReason = "demanda suspensa", CreatedAt = T0, UpdatedAt = T0.AddDays(2) };
        var cancelada = AcompanhamentoDaSc.Montar(Sc(RequisitionStatus.Submitted), q, null);
        Assert.Equal(AcompanhamentoDaSc.Parada, Situacao(cancelada, "cotacao"));
        Assert.Equal("demanda suspensa", cancelada.Motivo);
    }
}
