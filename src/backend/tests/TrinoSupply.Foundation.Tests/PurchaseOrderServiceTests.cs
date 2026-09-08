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

        var (fornecedor, _) = await sup.CreateAsync(Carla.Id, "Distribuidora Alfa LTDA", "Alfa", "12345678000190", null, "81 3333-1000");
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

        var (_, dup) = await w.Sup.CreateAsync(Carla.Id, "Outra Empresa", null, "12.345.678/0001-90", null, "81 3333-3000");
        var (_, invalido) = await w.Sup.CreateAsync(Carla.Id, "Empresa X", null, "123", null, "81 3333-4000");

        Assert.Equal("SUP-ERR-010", dup!.Code);   // mesmo CNPJ após normalização
        Assert.Equal("SUP-ERR-011", invalido!.Code);
    }

    /// <summary>
    /// §7 — o pré-cadastro. O comprador pede preço antes de existir cadastro: o mínimo
    /// para entrar numa cotação é razão social + telefone, que é o que ele tem na mão.
    /// O CNPJ vem depois, e é ele que separa "pode cotar" de "pode vencer".
    /// </summary>
    [Fact]
    public async Task Pre_cadastro_nasce_com_nome_e_telefone_e_so_homologa_com_cnpj()
    {
        var w = await BuildAsync();

        var (semTelefone, erroTelefone) = await w.Sup.CreateAsync(
            Carla.Id, "Ferragens do Norte LTDA", null, null, null, null);
        Assert.Null(semTelefone);
        Assert.Equal("SUP-ERR-014", erroTelefone!.Code);

        var (pre, erro) = await w.Sup.CreateAsync(
            Carla.Id, "Ferragens do Norte LTDA", null, null, null, "(81) 98888-1234");
        Assert.Null(erro);
        Assert.Null(pre!.TaxId);
        // nasce PROSPECT: cotar é livre, vencer o BID é que exige homologação (SUP-ERR-030)
        Assert.Equal(SupplierHomologation.Prospect, pre.HomologationStatus);

        // dois pré-cadastros sem CNPJ convivem — nulo não colide com nulo no índice único
        var (outro, erroOutro) = await w.Sup.CreateAsync(
            Carla.Id, "Parafusos do Agreste ME", null, null, null, "(87) 3555-4321");
        Assert.Null(erroOutro);
        Assert.Null(outro!.TaxId);

        // sem documento não há O.C., nota nem retenção — logo não há homologação
        var (naoHomologou, erroHomologacao) = await w.Sup.SetHomologationAsync(
            pre.Id, SupplierHomologation.Homologado);
        Assert.Null(naoHomologou);
        Assert.Equal("SUP-ERR-013", erroHomologacao!.Code);

        // o cadastro completo entra pela edição, e aí a homologação passa
        var (completo, erroEdicao) = await w.Sup.UpdateAsync(
            pre.Id, null, null, null, null, "11.222.333/0001-81");
        Assert.Null(erroEdicao);
        Assert.Equal("11222333000181", completo!.TaxId);

        var (homologado, semErro) = await w.Sup.SetHomologationAsync(pre.Id, SupplierHomologation.Homologado);
        Assert.Null(semErro);
        Assert.Equal(SupplierHomologation.Homologado, homologado!.HomologationStatus);

        // gravado uma vez, o CNPJ é identidade: a edição não o troca por outro
        var (mesmo, _) = await w.Sup.UpdateAsync(pre.Id, null, null, null, null, "98.765.432/0001-10");
        Assert.Equal("11222333000181", mesmo!.TaxId);
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

    /// <summary>
    /// Item inativado no catálogo depois da emissão: receber dá entrada em estoque e é
    /// recusado (IV-ERR-010). A leitura do pedido passa a dizer quais são, para a tela
    /// avisar na linha em vez de deixar o almoxarife preencher tudo e perder o formulário.
    /// </summary>
    [Fact]
    public async Task Pedido_diz_quais_itens_estao_inativos_no_catalogo()
    {
        var w = await BuildAsync();
        var (order, _) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null,
            [new PoItemInput("Detergente neutro", 10, "UN", 3.5m, w.Detergente.Id)], null);

        Assert.Empty(await w.Pos.InactiveCatalogCodesAsync(order!));

        await w.Catalog.UpdateAsync(w.Detergente.Id, null, null, null, null, active: false);

        var codigos = await w.Pos.InactiveCatalogCodesAsync((await w.Pos.GetAsync(order!.Id))!);
        Assert.Equal(["LMP-001"], codigos);

        // e a lista bate com o que o recebimento recusa de fato
        var (nada, erro) = await w.Pos.RegisterDeliveryAsync(Otavio, order.Id, w.Local.Id,
            [new PurchaseOrderService.ReceiptLine(order.Items.Single().Id, 10)],
            closeRemaining: false, closeReason: null);
        Assert.Null(nada);
        Assert.Equal("IV-ERR-010", erro!.Code);
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
        var mrs = new TrinoSupply.Foundation.Api.Materials.MaterialRequisitionService(w.Db, w.Catalog, clock);
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

        // campo em branco não é mais recusa seca: passa a cobrar o motivo (PO-BR-011)
        var (_, semNumero) = await w.Pos.RegisterErpOrderAsync(Carla, order!.Id, "  ", null);
        Assert.Equal("PO-ERR-054", semNumero!.Code);

        var (comOc, error) = await w.Pos.RegisterErpOrderAsync(Carla, order.Id, "663", new DateOnly(2026, 8, 20));
        Assert.Null(error);
        Assert.Equal("663", comOc!.ErpNumber);
        Assert.Equal(new DateOnly(2026, 8, 20), comOc.ErpIssuedOn);
    }

    [Fact]
    public async Task Numero_de_oc_do_erp_nao_se_repete_em_dois_pedidos()
    {
        var w = await BuildAsync();
        var (primeiro, _) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null,
            [new PoItemInput("Detergente neutro", 10, "UN", 3.5m, w.Detergente.Id)], null);
        var (segundo, _) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null,
            [new PoItemInput("Detergente neutro", 5, "UN", 3.5m, w.Detergente.Id)], null);

        var (_, ok) = await w.Pos.RegisterErpOrderAsync(Carla, primeiro!.Id, "OC-4501", null);
        Assert.Null(ok);

        // o mesmo número em outro pedido é recusado — a OC do SENIOR é única (RFQ-ERR-041)
        var (_, repetida) = await w.Pos.RegisterErpOrderAsync(Carla, segundo!.Id, " OC-4501 ", null);
        Assert.Equal("PO-ERR-050", repetida!.Code);
        Assert.Contains("já está registrada", repetida.Message);

        // corrigir o número do próprio pedido continua valendo
        var (mesmo, semErro) = await w.Pos.RegisterErpOrderAsync(Carla, primeiro.Id, "OC-4501", new DateOnly(2026, 8, 20));
        Assert.Null(semErro);
        Assert.Equal(new DateOnly(2026, 8, 20), mesmo!.ErpIssuedOn);

        var (_, longo) = await w.Pos.RegisterErpOrderAsync(Carla, segundo.Id, new string('9', 31), null);
        Assert.Equal("PO-ERR-050", longo!.Code);
    }

    // ---- fechamento sem O.C. do ERP (PO-BR-011) -----------------------------
    [Fact]
    public async Task Sem_oc_do_erp_o_pedido_so_fecha_com_a_observacao()
    {
        var w = await BuildAsync();
        var (order, _) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null,
            [new PoItemInput("Detergente neutro", 10, "UN", 3.5m, w.Detergente.Id)], null);

        // a regra é a O.C. do ERP: sem ela e sem a observação, não fecha
        var (_, semMotivo) = await w.Pos.RegisterErpOrderAsync(Carla, order!.Id, "  ", null);
        Assert.Equal("PO-ERR-054", semMotivo!.Code);

        // observação curta demais não conta como justificativa
        var (_, curto) = await w.Pos.RegisterErpOrderAsync(Carla, order.Id, null, null, "urgente");
        Assert.Equal("PO-ERR-054", curto!.Code);

        var (semOc, erro) = await w.Pos.RegisterErpOrderAsync(Carla, order.Id, null, new DateOnly(2026, 9, 1),
            "Compra emergencial de balcão, sem tempo de abrir O.C. no SENIOR.");

        Assert.Null(erro);
        Assert.Null(semOc!.ErpNumber);                       // nenhuma O.C. inventada
        Assert.StartsWith("PO-", semOc.Number);              // o pedido segue com a própria numeração
        Assert.Contains("emergencial", semOc.NoErpReason);
        Assert.Equal(new DateOnly(2026, 9, 1), semOc.ErpIssuedOn);
    }

    [Fact]
    public async Task Faturamento_anda_com_a_observacao_e_a_oc_que_chega_depois_a_dispensa()
    {
        var w = await BuildAsync();
        var (order, _) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null,
            [new PoItemInput("Detergente neutro", 10, "UN", 3.5m, w.Detergente.Id)], null);

        await w.Pos.RegisterErpOrderAsync(Carla, order!.Id, null, null,
            "Fornecedor entregou antes de a O.C. sair do SENIOR.");
        var (nf, erroNf) = await w.Pos.AddInvoiceAsync(Carla, order.Id, "1001", null, 35m);
        Assert.Null(erroNf);
        Assert.NotNull(nf);

        // a O.C. chega depois: deixa de ser exceção e a observação sai de cena
        var (comOc, _) = await w.Pos.RegisterErpOrderAsync(Carla, order.Id, "OC-9911", null);
        Assert.Equal("OC-9911", comOc!.ErpNumber);
        Assert.Null(comOc.NoErpReason);
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
    public async Task Devolucao_no_recebimento_exige_motivo_e_nao_entra_no_estoque_nem_no_recebido()
    {
        var w = await BuildAsync();
        var (order, _) = await w.Pos.CreateAsync(Carla, w.Fornecedor.Id, null,
            [new PoItemInput("Detergente neutro", 10, "UN", 3.5m, w.Detergente.Id)], null);
        var itemId = order!.Items.Single().Id;

        // devolver sem motivo não passa (PO-ERR-058)
        var (_, semMotivo) = await w.Pos.RegisterDeliveryAsync(Otavio, order.Id, w.Local.Id,
            [new PurchaseOrderService.ReceiptLine(itemId, 6, Rejected: 2)], false, null);
        Assert.Equal("PO-ERR-058", semMotivo!.Code);

        // 6 aceitos + 2 devolvidos: recebido = 6, devolvido = 2, saldo continua pendente (2)
        var (entrega, e1) = await w.Pos.RegisterDeliveryAsync(Otavio, order.Id, w.Local.Id,
            [new PurchaseOrderService.ReceiptLine(itemId, 6, Rejected: 2)], false, null,
            rejectReason: "qualidade fora do padrão");
        Assert.Null(e1);
        var item = entrega!.Items.Single();
        Assert.Equal(6m, item.ReceivedQuantity);
        Assert.Equal(2m, item.RejectedQuantity);
        Assert.Equal("qualidade fora do padrão", item.RejectionReason);
        Assert.True(entrega.HasPendingDelivery);

        // o estoque só recebeu o aceito
        var saldo = await w.Db.StockBalances.SingleAsync(b => b.CatalogItemId == w.Detergente.Id);
        Assert.Equal(6m, saldo.TotalQty);

        // aceito + devolvido não podem passar do que falta (faltam 4)
        var (_, excesso) = await w.Pos.RegisterDeliveryAsync(Otavio, order.Id, w.Local.Id,
            [new PurchaseOrderService.ReceiptLine(itemId, 3, Rejected: 2)], false, null, rejectReason: "avaria");
        Assert.Equal("PO-ERR-055", excesso!.Code);
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
