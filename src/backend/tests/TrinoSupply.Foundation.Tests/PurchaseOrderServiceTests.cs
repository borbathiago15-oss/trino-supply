using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Inventory;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

public class PurchaseOrderServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeNumbers : IPrNumberGenerator
    {
        private int _next;
        public Task<string> NextAsync(CancellationToken ct = default) =>
            Task.FromResult($"PR-2026-{++_next:000000}");
    }

    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana Solicitante", Roles.Requester);
    private static readonly Actor Bruno = new(Guid.NewGuid(), "Bruno Aprovador", Roles.Approver);
    private static readonly Actor Carla = new(Guid.NewGuid(), "Carla Compradora", Roles.PurchasingOfficer);
    private static readonly Actor Otavio = new(Guid.NewGuid(), "Otávio Almoxarife", Roles.WarehouseOperator);

    private sealed record World(
        PurchaseOrderService Pos, SupplierService Sup, RequisitionService Prs,
        InventoryService Inv, CatalogService Catalog, AppDbContext Db,
        Supplier Fornecedor, CatalogItem Detergente, StorageLocation Local);

    private static async Task<World> BuildAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        var catalog = new CatalogService(db, clock);
        var inv = new InventoryService(db, clock);
        var sup = new SupplierService(db, clock);
        var pos = new PurchaseOrderService(db, inv, clock);
        var prs = new RequisitionService(db, new FakeNumbers(), catalog, clock);

        var (fornecedor, _) = await sup.CreateAsync(Carla.Id, "Distribuidora Alfa LTDA", "Alfa", "12345678000190", null, null);
        var (detergente, _) = await catalog.CreateAsync(Carla.Id, "LMP-001", "Detergente neutro 500ml", "MATERIAL DE LIMPEZA", "UN", 3.5m);
        var (local, _) = await inv.CreateLocationAsync(Otavio.Id, "ALM-01", "Almoxarifado Central");
        return new World(pos, sup, prs, inv, catalog, db, fornecedor!, detergente!, local!);
    }

    private static async Task<PurchaseRequisition> ApprovedPrAsync(World w)
    {
        var (pr, _) = await w.Prs.CreateAsync(Ana, "Reposição", "CC-01", "NORMAL", null,
            [new ItemInput("", 10, null, null, null, w.Detergente.Id)]);
        await w.Prs.SubmitAsync(Ana, pr!.Id);
        var (approved, error) = await w.Prs.ApproveAsync(Bruno, pr.Id, null);
        Assert.Null(error);
        return approved!;
    }

    // ---- fornecedores -------------------------------------------------------
    [Fact]
    public async Task Fornecedor_com_tax_id_duplicado_ou_invalido_e_recusado()
    {
        var w = await BuildAsync();

        var (_, dup) = await w.Sup.CreateAsync(Carla.Id, "Outra Empresa", null, "12.345.678/0001-90", null, null);
        var (_, invalido) = await w.Sup.CreateAsync(Carla.Id, "Empresa X", null, "123", null, null);

        Assert.Equal("SUP-ERR-010", dup!.Code);   // mesmo CNPJ após normalização
        Assert.Equal("SUP-ERR-011", invalido!.Code);
    }

    [Fact]
    public async Task Fornecedor_inativo_nao_recebe_pedido()
    {
        var w = await BuildAsync();
        await w.Sup.UpdateAsync(w.Fornecedor.Id, null, null, null, active: false);

        var (_, error) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null,
            [new PoItemInput("Detergente", 10, "UN", 3.5m, null)], null);

        Assert.Equal("PO-ERR-020", error!.Code);
    }

    // ---- emissão ------------------------------------------------------------
    [Fact]
    public async Task Pedido_manual_gera_numero_e_total()
    {
        var w = await BuildAsync();

        var (order, error) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, "Urgente",
            [new PoItemInput("Detergente neutro", 10, "UN", 3.5m, w.Detergente.Id)], null);

        Assert.Null(error);
        Assert.StartsWith("PO-2026-", order!.Number);
        Assert.Equal(PurchaseOrderStatus.Issued, order.Status);
        Assert.Equal(35m, order.TotalValue);
        Assert.Equal("LMP-001", order.Items.Single().CatalogCode);
    }

    [Fact]
    public async Task Conversao_direta_de_requisicao_em_pedido_esta_desativada_RFQ_BR_010()
    {
        var w = await BuildAsync();
        var pr = await ApprovedPrAsync(w);

        var (_, error) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null, [], pr.Id);

        Assert.Equal("PO-ERR-023", error!.Code); // PR aprovada só vira OC pelo processo de cotação
    }

    [Fact]
    public async Task Pedido_sem_itens_ou_com_quantidade_invalida_e_recusado()
    {
        var w = await BuildAsync();

        var (_, vazio) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null, [], null);
        var (_, qtd) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null,
            [new PoItemInput("Item", 0, null, null, null)], null);

        Assert.Equal("PO-ERR-030", vazio!.Code);
        Assert.Equal("PO-ERR-010", qtd!.Code);
    }

    // ---- recebimento --------------------------------------------------------
    [Fact]
    public async Task Recebimento_gera_entrada_de_estoque_para_itens_de_catalogo()
    {
        var w = await BuildAsync();
        var (order, _) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null,
            [new PoItemInput("Detergente neutro", 10, "UN", 3.5m, w.Detergente.Id),
             new PoItemInput("Serviço de frete", 1, "UN", 50m, null)], null);

        var (received, error) = await w.Pos.ReceiveAsync(Carla, order!.Id, w.Local.Id);

        Assert.Null(error);
        Assert.Equal(PurchaseOrderStatus.Received, received!.Status);
        var mov = await w.Db.StockMovements.SingleAsync();   // só o item de catálogo movimenta
        Assert.Equal(MovementType.Entry, mov.Type);
        Assert.Equal(MovementOrigin.Receiving, mov.Origin);
        Assert.Equal(order.Number, mov.OriginReference);
        Assert.Equal(10, (await w.Db.StockBalances.SingleAsync()).TotalQty);
    }

    [Fact]
    public async Task Receber_ou_cancelar_fora_de_EMITIDO_e_recusado()
    {
        var w = await BuildAsync();
        var (order, _) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null,
            [new PoItemInput("Detergente", 5, "UN", 3.5m, w.Detergente.Id)], null);
        await w.Pos.ReceiveAsync(Carla, order!.Id, w.Local.Id);

        var (_, again) = await w.Pos.ReceiveAsync(Carla, order.Id, w.Local.Id);
        var (_, cancel) = await w.Pos.CancelAsync(Carla, order.Id, "Tarde demais");

        Assert.Equal("PO-ERR-040", again!.Code);
        Assert.Equal("PO-ERR-040", cancel!.Code);
    }

    [Fact]
    public async Task Cancelamento_exige_motivo()
    {
        var w = await BuildAsync();
        var (order, _) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null,
            [new PoItemInput("Item", 1, null, null, null)], null);

        var (_, semMotivo) = await w.Pos.CancelAsync(Carla, order!.Id, "  ");
        Assert.Equal("PO-ERR-041", semMotivo!.Code);

        var (cancelled, ok) = await w.Pos.CancelAsync(Carla, order.Id, "Fornecedor sem prazo");
        Assert.Null(ok);
        Assert.Equal(PurchaseOrderStatus.Cancelled, cancelled!.Status);
    }

    // ---- demandas -----------------------------------------------------------
    [Fact]
    public async Task Demandas_listam_pr_aprovada_e_somem_quando_a_cotacao_e_aberta()
    {
        var w = await BuildAsync();
        var pr = await ApprovedPrAsync(w);

        var (before, _) = await w.Pos.DemandsAsync();
        Assert.Single(before);
        Assert.Equal(pr.Id, before.Single().Id);

        var qsvc = new QuotationService(w.Db, new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero)));
        var (_, qErr) = await qsvc.CreateFromPrAsync(Carla, pr.Id, QuotationKind.Purchase, null, null);
        Assert.Null(qErr);

        var (after, _) = await w.Pos.DemandsAsync();
        Assert.Empty(after);
    }

    [Fact]
    public async Task Faltante_do_almoxarifado_entra_como_solicitacao_e_nao_como_item_solto()
    {
        var w = await BuildAsync();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        var mrs = new TrinoSupply.Foundation.Api.Materials.MaterialRequisitionService(
            w.Db, w.Catalog, w.Inv, clock);
        var prs = new RequisitionService(w.Db, new FakeNumbers(), w.Catalog, clock);

        var (mr, _) = await mrs.CreateAsync(Ana, "CC-01", null,
            [new TrinoSupply.Foundation.Api.Materials.MaterialItemInput(w.Detergente.Id, 5)]);
        await mrs.ApproveAsync(Bruno, mr!.Id, null, null);
        var (atendida, error) = await mrs.FulfillAsync(Otavio, mr.Id,
            [new(mr.Items.Single().Id, 0)], prs);            // nada em estoque
        Assert.Null(error);

        // o faltante virou SC própria, e a lista de demandas não repete o item
        var (demandPrs, mrItems) = await w.Pos.DemandsAsync();
        Assert.Empty(mrItems);
        Assert.Contains(demandPrs, r => r.Number == atendida!.PurchaseRequisitionNumber);
    }

    [Fact]
    public void Permissoes_comprador_gestor_admin_gerenciam_auditor_consulta()
    {
        Assert.True(PurchaseOrderService.CanManage(Roles.PurchasingOfficer));
        Assert.True(PurchaseOrderService.CanManage(Roles.SupplyManager));
        Assert.False(PurchaseOrderService.CanManage(Roles.Requester));
        Assert.False(PurchaseOrderService.CanManage(Roles.Auditor));
        Assert.True(PurchaseOrderService.CanView(Roles.Auditor));
        Assert.True(SupplierService.CanMaintain(Roles.PurchasingOfficer));
        Assert.False(SupplierService.CanMaintain(Roles.WarehouseOperator));
    }

    // ---- OC do ERP, faturamento e entrega (revisão de telas 2026-08-26) ------
    [Fact]
    public async Task Oc_do_erp_exige_numero_e_guarda_a_data()
    {
        var w = await BuildAsync();
        var (order, _) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null,
            [new PoItemInput("Detergente neutro", 10, "UN", 3.5m, w.Detergente.Id)], null);

        var (_, semNumero) = await w.Pos.RegisterErpOrderAsync(Carla, order!.Id, "  ", null);
        Assert.Equal("PO-ERR-050", semNumero!.Code);

        var (comOc, error) = await w.Pos.RegisterErpOrderAsync(Carla, order.Id, "663", new DateOnly(2026, 8, 20));
        Assert.Null(error);
        Assert.Equal("663", comOc!.ErpNumber);
        Assert.Equal(new DateOnly(2026, 8, 20), comOc.ErpIssuedOn);
    }

    [Fact]
    public async Task Faturamento_exige_oc_registrada_e_aceita_mais_de_uma_nota()
    {
        var w = await BuildAsync();
        var (order, _) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null,
            [new PoItemInput("Detergente neutro", 10, "UN", 3.5m, w.Detergente.Id)], null);

        var (_, semOc) = await w.Pos.AddInvoiceAsync(Carla, order!.Id, "1001", null, null);
        Assert.Equal("PO-ERR-052", semOc!.Code);

        await w.Pos.RegisterErpOrderAsync(Carla, order.Id, "663", null);
        var (nf1, e1) = await w.Pos.AddInvoiceAsync(Carla, order.Id, "1001", new DateOnly(2026, 8, 21), 20m);
        var (nf2, e2) = await w.Pos.AddInvoiceAsync(Carla, order.Id, "1002", new DateOnly(2026, 8, 22), 15m);
        Assert.Null(e1); Assert.Null(e2);
        Assert.Equal(2, await w.Db.PurchaseOrderInvoices.CountAsync(i => i.OrderId == order.Id));
        Assert.NotEqual(nf1!.Id, nf2!.Id);

        var atualizado = await w.Db.PurchaseOrders.SingleAsync(o => o.Id == order.Id);
        Assert.Equal(PurchaseOrderStatus.Invoiced, atualizado.Status);   // faturado, aguardando entrega
    }

    [Fact]
    public async Task Entrega_parcial_mantem_o_saldo_pendente_e_a_total_encerra()
    {
        var w = await BuildAsync();
        var (order, _) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null,
            [new PoItemInput("Detergente neutro", 10, "UN", 3.5m, w.Detergente.Id)], null);
        var itemId = order!.Items.Single().Id;

        var (parcial, e1) = await w.Pos.RegisterDeliveryAsync(Otavio, order.Id, w.Local.Id,
            [new PurchaseOrderService.ReceiptLine(itemId, 4)], closeRemaining: false, closeReason: null);
        Assert.Null(e1);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, parcial!.Status);
        Assert.Equal(4m, parcial.Items.Single().ReceivedQuantity);
        Assert.True(parcial.HasPendingDelivery);

        var (excesso, e2) = await w.Pos.RegisterDeliveryAsync(Otavio, order.Id, w.Local.Id,
            [new PurchaseOrderService.ReceiptLine(itemId, 7)], false, null);
        Assert.Equal("PO-ERR-055", e2!.Code);          // só faltavam 6
        Assert.Null(excesso);

        var (total, e3) = await w.Pos.RegisterDeliveryAsync(Otavio, order.Id, w.Local.Id,
            [new PurchaseOrderService.ReceiptLine(itemId, 6)], false, null);
        Assert.Null(e3);
        Assert.Equal(PurchaseOrderStatus.Received, total!.Status);
        Assert.NotNull(total.DeliveryCompletedAt);

        // o estoque recebeu as duas entregas
        var saldo = await w.Db.StockBalances.SingleAsync(b => b.CatalogItemId == w.Detergente.Id);
        Assert.Equal(10m, saldo.TotalQty);
    }

    [Fact]
    public async Task Encerrar_o_saldo_nao_entregue_exige_motivo_e_marca_parcial()
    {
        var w = await BuildAsync();
        var (order, _) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null,
            [new PoItemInput("Detergente neutro", 10, "UN", 3.5m, w.Detergente.Id)], null);
        var itemId = order!.Items.Single().Id;
        await w.Pos.RegisterDeliveryAsync(Otavio, order.Id, w.Local.Id,
            [new PurchaseOrderService.ReceiptLine(itemId, 4)], false, null);

        var (_, semMotivo) = await w.Pos.RegisterDeliveryAsync(Otavio, order.Id, w.Local.Id,
            [], closeRemaining: true, closeReason: null);
        Assert.Equal("PO-ERR-057", semMotivo!.Code);

        var (encerrado, error) = await w.Pos.RegisterDeliveryAsync(Otavio, order.Id, w.Local.Id,
            [], true, "fornecedor não entregará o restante");
        Assert.Null(error);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, encerrado!.Status);
        Assert.Equal("fornecedor não entregará o restante", encerrado.CancelReason);
        Assert.NotNull(encerrado.DeliveryCompletedAt);
    }

    [Fact]
    public void Status_do_processo_cobre_as_oito_situacoes_do_fluxo()
    {
        var pr = new PurchaseRequisition { Status = RequisitionStatus.Submitted };
        Assert.Equal("PENDENTE", ProcessStatus.Of(pr, null, null).Key);
        Assert.Equal("EM_COTACAO", ProcessStatus.Of(pr, new Quotation { Status = QuotationStatus.Open }, null).Key);
        Assert.Equal("AGUARDANDO_APROVACAO",
            ProcessStatus.Of(pr, new Quotation { Status = QuotationStatus.AwaitingDirector }, null).Key);
        Assert.Equal("PEDIDO_APROVADO",
            ProcessStatus.Of(pr, new Quotation { Status = QuotationStatus.ApprovedForIssue }, null).Key);
        Assert.Equal("PEDIDO_REJEITADO",
            ProcessStatus.Of(pr, new Quotation { Status = QuotationStatus.Rejected }, null).Key);
        Assert.Equal("OC_FATURAMENTO",
            ProcessStatus.Of(pr, null, new PurchaseOrder { Status = PurchaseOrderStatus.Invoiced }).Key);
        Assert.Equal("PEDIDO_ENTREGUE",
            ProcessStatus.Of(pr, null, new PurchaseOrder { Status = PurchaseOrderStatus.Received }).Key);
        Assert.Equal("CANCELADO_PARCIAL",
            ProcessStatus.Of(pr, null, new PurchaseOrder { Status = PurchaseOrderStatus.PartiallyReceived }).Key);
    }
}
