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
    public async Task Converter_requisicao_aprovada_copia_itens_e_impede_segundo_pedido()
    {
        var w = await BuildAsync();
        var pr = await ApprovedPrAsync(w);

        var (order, error) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null, [], pr.Id);
        Assert.Null(error);
        Assert.Equal(pr.Number, order!.SourcePrNumber);
        Assert.Single(order.Items);
        Assert.Equal(w.Detergente.Id, order.Items.Single().CatalogItemId);

        var (_, second) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null, [], pr.Id);
        Assert.Equal("PO-ERR-022", second!.Code);
    }

    [Fact]
    public async Task Requisicao_nao_aprovada_nao_vira_pedido()
    {
        var w = await BuildAsync();
        var (pr, _) = await w.Prs.CreateAsync(Ana, "Justificativa", "CC-01", "NORMAL", null,
            [new ItemInput("Cabo", 1, "UN", 10, null)]);

        var (_, error) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null, [], pr!.Id);

        Assert.Equal("PO-ERR-021", error!.Code);
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
    public async Task Demandas_listam_pr_aprovada_sem_pedido_e_somem_apos_conversao()
    {
        var w = await BuildAsync();
        var pr = await ApprovedPrAsync(w);

        var (before, _) = await w.Pos.DemandsAsync();
        Assert.Single(before);
        Assert.Equal(pr.Id, before.Single().Id);

        await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null, [], pr.Id);
        var (after, _) = await w.Pos.DemandsAsync();
        Assert.Empty(after);
    }

    [Fact]
    public async Task Demandas_incluem_itens_de_solicitacao_em_rota_de_compra()
    {
        var w = await BuildAsync();
        var mrs = new TrinoSupply.Foundation.Api.Materials.MaterialRequisitionService(
            w.Db, w.Catalog, w.Inv, new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero)));
        var (mr, _) = await mrs.CreateAsync(Ana, "CC-01", null,
            [new TrinoSupply.Foundation.Api.Materials.MaterialItemInput(w.Detergente.Id, 5)]);
        await mrs.FulfillAsync(Otavio, mr!.Id, w.Local.Id);   // sem saldo → rota de compra

        var (_, mrItems) = await w.Pos.DemandsAsync();

        var demand = Assert.Single(mrItems);
        Assert.Equal(mr.Number, demand.mr.Number);
        Assert.Equal(5, demand.item.Quantity);
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
}
