using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// A ficha do contrato: documentos, compras e história num lugar só. O que estes testes
/// protegem é a memória do contrato — ele é regravado inteiro a cada edição, e sem o registro
/// o preço de antes da renovação simplesmente sumia —, e a régua do saldo, que precisa ser a
/// mesma da lista de contratos.
/// </summary>
public class FichaDoContratoTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly DateTimeOffset Agora = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoje = new(2026, 9, 25);
    private static readonly Actor Marina = new(Guid.NewGuid(), "Marina Gestora", Roles.SupplyManager);

    private sealed record World(AppDbContext Db, SupplierService Sup, FixedTimeProvider Clock, Supplier Alfa);

    private static async Task<World> BuildAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var clock = new FixedTimeProvider(Agora);
        var alfa = new Supplier { LegalName = "Alfa EPIs Ltda", TaxId = "12345678000195", CreatedAt = Agora };
        db.Suppliers.Add(alfa);
        await db.SaveChangesAsync();
        return new World(db, new SupplierService(db, clock), clock, alfa);
    }

    private static SupplierService.ContractItemInput Item(string descricao, decimal preco) =>
        new(null, descricao, null, "UN", preco, null, null, null, null);

    [Fact]
    public async Task Cadastrar_alterar_e_encerrar_ficam_na_linha_do_tempo_com_o_que_mudou()
    {
        var w = await BuildAsync();

        await w.Sup.SaveContractAsync(w.Alfa.Id, "CT-10", Hoje, Hoje.AddMonths(12), null,
            [Item("Luva nitrílica", 1.80m), Item("Óculos incolor", 14.90m)], 50_000m, actor: Marina);

        w.Clock.Now = Agora.AddDays(30);
        await w.Sup.SaveContractAsync(w.Alfa.Id, "CT-10", Hoje, Hoje.AddMonths(24), null,
            [Item("Luva nitrílica", 1.95m), Item("Protetor auricular", 2.40m)], 50_000m, actor: Marina);

        w.Clock.Now = Agora.AddDays(60);
        await w.Sup.SaveContractAsync(w.Alfa.Id, null, null, null, null, [], actor: Marina);

        var ficha = (await w.Sup.FichaAsync(w.Alfa.Id))!;
        Assert.True(ficha.HistoricoCompleto);
        Assert.Equal(["ENCERRADO", "ALTERADO", "CRIADO"], ficha.LinhaDoTempo.Select(e => e.Tipo));

        var criado = ficha.LinhaDoTempo[2].Texto;
        Assert.Contains("CT-10", criado);
        Assert.Contains("2 produto(s)", criado);
        Assert.Contains("50.000,00", criado);

        // o que a renovação mudou, dito item a item — e não só "o contrato foi alterado"
        var alterado = ficha.LinhaDoTempo[1].Texto;
        Assert.Contains("vigência 25/09/2026 a 25/09/2027 → 25/09/2026 a 25/09/2028", alterado);
        Assert.Contains("preço de Luva nitrílica R$ 1,80 → R$ 1,95", alterado);
        Assert.Contains("incluído Protetor auricular", alterado);
        Assert.Contains("retirado Óculos incolor", alterado);
        Assert.All(ficha.LinhaDoTempo, e => Assert.Equal("Marina Gestora", e.Quem));
    }

    [Fact]
    public async Task Salvar_sem_mudar_nada_nao_e_um_acontecimento()
    {
        var w = await BuildAsync();
        await w.Sup.SaveContractAsync(w.Alfa.Id, "CT-10", Hoje, Hoje.AddMonths(12), null, [Item("Luva nitrílica", 1.80m)], actor: Marina);
        await w.Sup.SaveContractAsync(w.Alfa.Id, "CT-10", Hoje, Hoje.AddMonths(12), null, [Item("Luva nitrílica", 1.80m)], actor: Marina);

        var ficha = (await w.Sup.FichaAsync(w.Alfa.Id))!;
        Assert.Single(ficha.LinhaDoTempo);
    }

    [Fact]
    public async Task Contrato_de_antes_do_registro_diz_que_a_historia_esta_incompleta()
    {
        var w = await BuildAsync();
        // gravado direto, como estava no banco antes de a ficha existir
        w.Alfa.ContractNumber = "CT-ANTIGO";
        w.Db.SupplierContractItems.Add(new SupplierContractItem { SupplierId = w.Alfa.Id, Description = "Luva", UnitPrice = 2m });
        await w.Db.SaveChangesAsync();

        var ficha = (await w.Sup.FichaAsync(w.Alfa.Id))!;
        Assert.False(ficha.HistoricoCompleto);
        Assert.Empty(ficha.LinhaDoTempo);
    }

    [Fact]
    public async Task Compras_mostram_todas_e_dizem_quais_contam_no_saldo_pela_mesma_regua_da_lista()
    {
        var w = await BuildAsync();
        await w.Sup.SaveContractAsync(w.Alfa.Id, "CT-10", Hoje, Hoje.AddMonths(12), null, [Item("Luva", 2m)], 10_000m, actor: Marina);
        w.Db.PurchaseOrders.AddRange(
            new PurchaseOrder { Number = "PO-1", SupplierId = w.Alfa.Id, SupplierName = "Alfa", TotalValue = 900m, CreatedAt = Agora.AddDays(-40) },
            new PurchaseOrder { Number = "PO-2", SupplierId = w.Alfa.Id, SupplierName = "Alfa", TotalValue = 1_200m, CreatedAt = Agora.AddDays(5) },
            new PurchaseOrder { Number = "PO-3", SupplierId = w.Alfa.Id, SupplierName = "Alfa", TotalValue = 700m, CreatedAt = Agora.AddDays(6),
                Status = PurchaseOrderStatus.Cancelled });
        await w.Db.SaveChangesAsync();

        var ficha = (await w.Sup.FichaAsync(w.Alfa.Id))!;
        Assert.Equal(["PO-3", "PO-2", "PO-1"], ficha.Pedidos.Select(p => p.Number));
        // antes da vigência e cancelado aparecem, mas não abatem o saldo
        Assert.Equal([false, true, false], ficha.Pedidos.Select(p => p.NaVigencia));
        Assert.Equal(1_200m, ficha.Fornecedor.ContractConsumed);

        var daLista = (await w.Sup.ListAsync(false)).Single(s => s.Id == w.Alfa.Id);
        Assert.Equal(daLista.ContractConsumed, ficha.Fornecedor.ContractConsumed);
    }

    [Fact]
    public async Task Contrato_assinado_entra_e_sai_da_historia_e_nao_restringe_a_homologacao()
    {
        var w = await BuildAsync();
        w.Alfa.HomologationStatus = SupplierHomologation.Homologado;
        await w.Db.SaveChangesAsync();

        // o contrato assinado "vence" com a vigência: isso não torna o fornecedor irregular
        var (doc, erro) = await w.Sup.AddDocumentAsync(w.Alfa.Id, "CONTRATO", null, Hoje.AddDays(-1),
            Guid.NewGuid(), "contrato-ct10.pdf", "Marina Gestora");
        Assert.Null(erro);
        var alfa = await w.Db.Suppliers.Include(s => s.Documents).SingleAsync(s => s.Id == w.Alfa.Id);
        Assert.Equal(SupplierHomologation.Homologado, alfa.EffectiveHomologation(Hoje));

        // a certidão vencida continua restringindo, como sempre
        await w.Sup.AddDocumentAsync(w.Alfa.Id, "CNDT", null, Hoje.AddDays(-1), Guid.NewGuid(), "cndt.pdf", "Marina Gestora");
        alfa = await w.Db.Suppliers.Include(s => s.Documents).SingleAsync(s => s.Id == w.Alfa.Id);
        Assert.Equal(SupplierHomologation.Restrito, alfa.EffectiveHomologation(Hoje));

        w.Clock.Now = Agora.AddHours(1);
        await w.Sup.RemoveDocumentAsync(w.Alfa.Id, doc!.Id, actor: Marina);
        var ficha = (await w.Sup.FichaAsync(w.Alfa.Id))!;
        // só o papel do contrato vai para a história; a certidão tem o lugar dela no cadastro
        Assert.Equal(["DOCUMENTO_REMOVIDO", "DOCUMENTO_ANEXADO"], ficha.LinhaDoTempo.Select(e => e.Tipo));
        Assert.Contains("contrato-ct10.pdf", ficha.LinhaDoTempo[0].Texto);
        Assert.Equal("Marina Gestora", ficha.LinhaDoTempo[0].Quem);
    }

    [Fact]
    public async Task Reajuste_entra_na_linha_do_tempo_com_o_custo_evitado()
    {
        var w = await BuildAsync();
        await w.Sup.SaveContractAsync(w.Alfa.Id, "CT-10", Hoje, Hoje.AddMonths(12), null, [Item("Luva", 2m)], actor: Marina);
        w.Db.PurchaseOrders.Add(new PurchaseOrder
        {
            Number = "PO-1", SupplierId = w.Alfa.Id, SupplierName = "Alfa", TotalValue = 10_000m, CreatedAt = Agora.AddDays(-10),
        });
        await w.Db.SaveChangesAsync();
        w.Clock.Now = Agora.AddDays(1);
        await w.Sup.RegisterContractAdjustmentAsync(Marina, w.Alfa.Id, 8m, 3m, "índice IPCA", false);

        var ficha = (await w.Sup.FichaAsync(w.Alfa.Id))!;
        var reajuste = ficha.LinhaDoTempo[0];
        Assert.Equal("REAJUSTE", reajuste.Tipo);
        Assert.Contains("pleiteado de 8%, fechado em 3%", reajuste.Texto);
        Assert.Contains("R$ 500,00", reajuste.Texto);
        Assert.Equal(500m, ficha.CustoEvitado);
    }

    [Fact]
    public async Task Fornecedor_que_nao_existe_nao_tem_ficha()
    {
        var w = await BuildAsync();
        Assert.Null(await w.Sup.FichaAsync(Guid.NewGuid()));
    }
}
