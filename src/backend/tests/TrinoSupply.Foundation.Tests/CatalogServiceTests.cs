using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

public class CatalogServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeNumbers : IPrNumberGenerator
    {
        private int _next;
        public Task<string> NextAsync(CancellationToken ct = default) => Task.FromResult($"PR-2026-{++_next:000000}");
    }

    private static readonly Actor Gestor = new(Guid.NewGuid(), "Gestor", Roles.SupplyManager);
    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana", Roles.Requester);

    private static (CatalogService catalog, RequisitionService reqs, AppDbContext db) Build()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        var catalog = new CatalogService(db, clock);
        return (catalog, new RequisitionService(db, new FakeNumbers(), catalog, clock), db);
    }

    [Fact]
    public async Task Criacao_normaliza_codigo_e_familia_e_bloqueia_duplicado()
    {
        var (catalog, _, _) = Build();

        var (item, ok) = await catalog.CreateAsync(Gestor.Id, "lmp-001", "Detergente neutro 500ml", "material de limpeza", "un", 3.50m);
        var (_, dup) = await catalog.CreateAsync(Gestor.Id, "LMP-001", "Outro", "MATERIAL DE LIMPEZA", null, null);

        Assert.Null(ok);
        Assert.Equal("LMP-001", item!.Code);
        Assert.Equal("MATERIAL DE LIMPEZA", item.Family);
        Assert.Equal("IC-ERR-010", dup!.Code);
    }

    // ---- grade de tamanhos e seletor de quem pede ----------------------------
    [Fact]
    public async Task Grade_cadastra_um_produto_por_tamanho_com_o_mesmo_codigo_base()
    {
        var (catalog, _, _) = Build();

        var (itens, erro) = await catalog.CriarGradeAsync(Gestor.Id, "12003", "Bota de segurança", "EPI", "PAR", 89.90m,
            ["40, 38, 39"], productType: ProductTypes.Epi);

        Assert.Null(erro);
        Assert.Equal(["12003-38", "12003-39", "12003-40"], itens.Select(i => i.Code).ToList());
        Assert.All(itens, i => Assert.Equal("12003", i.BaseCode));
        Assert.All(itens, i => Assert.Equal("EPI", i.Family));
        Assert.All(itens, i => Assert.Equal(89.90m, i.ReferencePrice));
        Assert.Equal("Bota de segurança — Tam. 38", itens[0].Description);
        Assert.Equal(["38", "39", "40"], itens.Select(i => i.Size).ToList());
    }

    [Fact]
    public async Task Grade_sem_tamanho_e_com_codigo_repetido_e_recusada_inteira()
    {
        var (catalog, _, db) = Build();
        await catalog.CreateAsync(Gestor.Id, "12003-M", "Camisa antiga", "FARDAMENTO", "UN", null);

        var (_, semTamanho) = await catalog.CriarGradeAsync(Gestor.Id, "9000", "Camisa", "FARDAMENTO", "UN", null, []);
        Assert.Equal("IC-ERR-017", semTamanho!.Code);

        var (_, repetido) = await catalog.CriarGradeAsync(Gestor.Id, "12003", "Camisa", "FARDAMENTO", "UN", null, ["P, M, G"]);
        Assert.Equal("IC-ERR-010", repetido!.Code);
        Assert.Contains("12003-M", repetido.Message);
        // meia grade cadastrada seria pior que nenhuma: nada entrou
        Assert.Equal(1, await db.CatalogItems.CountAsync());
    }

    [Fact]
    public async Task Seletor_junta_os_tamanhos_num_produto_so_e_a_busca_traz_a_grade_inteira()
    {
        var (catalog, _, _) = Build();
        await catalog.CriarGradeAsync(Gestor.Id, "12003", "Bota de segurança", "EPI", "PAR", 89.90m, ["38, 39, 40"]);
        await catalog.CreateAsync(Gestor.Id, "LMP-001", "Detergente neutro", "MATERIAL DE LIMPEZA", "UN", 3.50m);

        var tudo = await catalog.ParaEscolhaAsync(null, null);
        Assert.Equal(2, tudo.Count);                                  // dois produtos, não quatro itens
        var bota = tudo.Single(p => p.BaseCode == "12003");
        Assert.Equal("Bota de segurança", bota.Description);          // a descrição sem o tamanho
        Assert.Equal(["38", "39", "40"], bota.Sizes.Select(v => v.Size).ToList());
        Assert.True(bota.TemGrade);

        var detergente = tudo.Single(p => p.BaseCode is null);
        Assert.False(detergente.TemGrade);
        Assert.Null(Assert.Single(detergente.Sizes).Size);

        // procurar um tamanho traz o produto inteiro: achar o 39 e esconder o 40 obrigaria a buscar de novo
        var porTamanho = await catalog.ParaEscolhaAsync(null, "12003-39");
        Assert.Equal(["38", "39", "40"], Assert.Single(porTamanho).Sizes.Select(v => v.Size).ToList());
        // e o filtro por família continua valendo
        Assert.Equal("EPI", Assert.Single(await catalog.ParaEscolhaAsync("EPI", null)).Family);
    }

    [Fact]
    public async Task Seletor_marca_o_tamanho_sem_CA_e_ignora_o_inativo()
    {
        var (catalog, _, _) = Build();
        var (itens, _) = await catalog.CriarGradeAsync(Gestor.Id, "12003", "Bota", "EPI", "PAR", null, ["38, 39, 40"],
            productType: ProductTypes.Epi);
        // o C.A. é do par produto+fornecedor: informar num tamanho não libera os outros
        await catalog.UpdateAsync(itens[0].Id, null, null, null, null, active: null, suppliers:
            [new ItemSupplierInput("Alfa EPIs", null, null, null, null, null, null, "CA-12345")]);
        await catalog.UpdateAsync(itens[2].Id, null, null, null, null, active: false);

        var bota = Assert.Single(await catalog.ParaEscolhaAsync("EPI", null));
        Assert.Equal(["38", "39"], bota.Sizes.Select(v => v.Size).ToList());   // o 40 inativo saiu
        Assert.False(bota.Sizes[0].CompliancePending);                         // o 38 tem C.A.
        Assert.True(bota.Sizes[1].CompliancePending);                          // o 39 não
        Assert.False(bota.CompliancePending);                                  // o produto ainda pode ser pedido
    }

    [Fact]
    public async Task Listagem_por_familia_retorna_somente_ativos_para_solicitantes()
    {
        var (catalog, _, _) = Build();
        var (detergente, _) = await catalog.CreateAsync(Gestor.Id, "LMP-001", "Detergente neutro", "MATERIAL DE LIMPEZA", "UN", 3.50m);
        await catalog.CreateAsync(Gestor.Id, "LMP-002", "Água sanitária 1L", "MATERIAL DE LIMPEZA", "UN", 5.90m);
        await catalog.CreateAsync(Gestor.Id, "ESC-001", "Papel A4 (resma)", "MATERIAL DE ESCRITORIO", "RES", 28.00m);
        await catalog.UpdateAsync(detergente!.Id, null, null, null, null, active: false);

        var limpeza = await catalog.ListAsync("MATERIAL DE LIMPEZA", null, includeInactive: false);
        var familias = await catalog.FamiliesAsync(onlyActive: true);

        Assert.Single(limpeza);
        Assert.Equal("LMP-002", limpeza[0].Code);
        Assert.Equal(["MATERIAL DE ESCRITORIO", "MATERIAL DE LIMPEZA"], familias);
    }

    [Fact]
    public async Task Requisicao_por_catalogo_usa_snapshot_de_descricao_unidade_e_preco()
    {
        var (catalog, reqs, _) = Build();
        var (papel, _) = await catalog.CreateAsync(Gestor.Id, "ESC-001", "Papel A4 (resma)", "MATERIAL DE ESCRITORIO", "RES", 28.00m);
        var (caneta, _) = await catalog.CreateAsync(Gestor.Id, "ESC-002", "Caneta esferográfica azul", "MATERIAL DE ESCRITORIO", "UN", 2.20m);

        var (pr, error) = await reqs.CreateAsync(Ana, "Reposição do escritório", "CC-ADM-001", null, null,
        [
            new ItemInput("", 10, null, null, null, papel!.Id),
            new ItemInput("", 50, null, null, null, caneta!.Id),
        ], "CATALOGO");

        Assert.Null(error);
        Assert.Equal("CATALOGO", pr!.Kind);
        var papelItem = pr.Items.Single(i => i.CatalogCode == "ESC-001");
        Assert.Equal("Papel A4 (resma)", papelItem.Description);
        Assert.Equal("RES", papelItem.UnitOfMeasure);
        Assert.Equal(28.00m, papelItem.EstimatedUnitPrice);
        Assert.Equal(10 * 28.00m + 50 * 2.20m, pr.TotalEstimatedValue);
    }

    [Fact]
    public async Task Item_inativo_nao_pode_ser_solicitado()
    {
        var (catalog, reqs, _) = Build();
        var (item, _) = await catalog.CreateAsync(Gestor.Id, "LMP-001", "Detergente neutro", "MATERIAL DE LIMPEZA", "UN", 3.50m);
        await catalog.UpdateAsync(item!.Id, null, null, null, null, active: false);

        var (_, error) = await reqs.CreateAsync(Ana, "Limpeza", "CC-ADM-001", null, null,
            [new ItemInput("", 5, null, null, null, item.Id)], "CATALOGO");

        Assert.Equal("PR-ERR-022", error!.Code);
    }

    [Fact]
    public async Task Requisicao_por_catalogo_recusa_item_avulso()
    {
        var (_, reqs, _) = Build();

        var (_, error) = await reqs.CreateAsync(Ana, "Mista inválida", "CC-ADM-001", null, null,
            [new ItemInput("Item digitado à mão", 1, "UN", 10, null)], "CATALOGO");

        Assert.Equal("PR-ERR-030", error!.Code);
    }

    [Fact]
    public async Task Requisicao_avulsa_continua_funcionando_como_antes()
    {
        var (_, reqs, _) = Build();

        var (pr, error) = await reqs.CreateAsync(Ana, "Compra pontual", "CC-ADM-001", null, null,
            [new ItemInput("Serviço de calibração de balança", 1, "SV", 850, null)]);

        Assert.Null(error);
        Assert.Equal("AVULSA", pr!.Kind);
        Assert.Null(pr.Items.Single().CatalogItemId);
    }

    // ---- o custo de compra do produto de almoxarifado (IC-ERR-018) -----------------------

    [Fact]
    public async Task Produto_de_familia_de_almoxarifado_nao_nasce_sem_custo_de_compra()
    {
        var (catalog, _, _) = Build();
        await catalog.CreateFamilyAsync(Gestor.Id, "FARDAMENTO", null, materialRequestable: true);
        await catalog.CreateFamilyAsync(Gestor.Id, "SERVICOS", null, materialRequestable: false);

        var (_, semCusto) = await catalog.CreateAsync(Gestor.Id, "FAR-001", "Camisa polo", "FARDAMENTO", "UN", null);
        Assert.Equal("IC-ERR-018", semCusto!.Code);

        var (comCusto, ok) = await catalog.CreateAsync(Gestor.Id, "FAR-001", "Camisa polo", "FARDAMENTO", "UN", 45m);
        Assert.Null(ok);
        Assert.Equal(45m, comCusto!.ReferencePrice);

        // família que não é de almoxarifado não exige: o serviço não vai para a solicitação de material
        var (_, servico) = await catalog.CreateAsync(Gestor.Id, "SRV-001", "Manutenção predial", "SERVICOS", "UN", null);
        Assert.Null(servico);

        // a grade inteira é recusada, não meia grade
        var (grade, gradeErro) = await catalog.CriarGradeAsync(Gestor.Id, "12003", "Bota", "FARDAMENTO", "PAR", null, ["38", "39"]);
        Assert.Equal("IC-ERR-018", gradeErro!.Code);
        Assert.Empty(grade);
    }

    [Fact]
    public async Task O_ajuste_do_produto_vence_a_familia_tambem_para_o_custo()
    {
        var (catalog, _, _) = Build();
        await catalog.CreateFamilyAsync(Gestor.Id, "FARDAMENTO", null, materialRequestable: true);
        await catalog.CreateFamilyAsync(Gestor.Id, "SERVICOS", null, materialRequestable: false);

        // "nunca entra" numa família de almoxarifado: não vai para a solicitação, não exige custo
        var (_, nunca) = await catalog.CreateAsync(Gestor.Id, "FAR-900", "Cabide de loja", "FARDAMENTO", "UN", null,
            materialAdjust: MaterialDoAlmoxarifado.NuncaEntra);
        Assert.Null(nunca);

        // "sempre entra" numa família que não é: vai para a solicitação, exige
        var (_, sempre) = await catalog.CreateAsync(Gestor.Id, "SRV-900", "Crachá", "SERVICOS", "UN", null,
            materialAdjust: MaterialDoAlmoxarifado.SempreEntra);
        Assert.Equal("IC-ERR-018", sempre!.Code);
    }

    [Fact]
    public async Task Familia_fora_do_cadastro_nao_exige_custo_e_a_edicao_cobra_quando_o_produto_passa_a_entrar()
    {
        var (catalog, _, _) = Build();
        // cadastro de famílias vazio: a tela mostra tudo, mas ninguém marcou nada — não há o que cobrar
        var (solto, erro) = await catalog.CreateAsync(Gestor.Id, "X-001", "Produto solto", "SEM CADASTRO", "UN", null);
        Assert.Null(erro);

        await catalog.CreateFamilyAsync(Gestor.Id, "SEM CADASTRO", null, materialRequestable: true);
        // a edição de outra coisa cobra o custo que a família passou a exigir — e diz qual é o campo
        var (_, aoEditar) = await catalog.UpdateAsync(solto!.Id, "Produto solto corrigido", null, null, null, null);
        Assert.Equal("IC-ERR-018", aoEditar!.Code);
        var (corrigido, ok) = await catalog.UpdateAsync(solto.Id, "Produto solto corrigido", null, null, 7.5m, null);
        Assert.Null(ok);
        Assert.Equal(7.5m, corrigido!.ReferencePrice);
    }

    [Fact]
    public async Task O_resumo_conta_e_a_lista_recorta_os_produtos_de_almoxarifado_sem_custo()
    {
        var (catalog, _, db) = Build();
        await catalog.CreateFamilyAsync(Gestor.Id, "FARDAMENTO", null, materialRequestable: true);
        await catalog.CreateFamilyAsync(Gestor.Id, "SERVICOS", null, materialRequestable: false);
        // o acervo anterior à regra entra pelo banco, sem custo
        db.CatalogItems.AddRange(
            new CatalogItem { Code = "FAR-001", Description = "Camisa", Family = "FARDAMENTO" },
            new CatalogItem { Code = "FAR-002", Description = "Calça", Family = "FARDAMENTO", ReferencePrice = 80m },
            new CatalogItem { Code = "FAR-003", Description = "Camisa antiga", Family = "FARDAMENTO", Active = false },
            new CatalogItem { Code = "SRV-001", Description = "Limpeza", Family = "SERVICOS" },
            new CatalogItem { Code = "SRV-002", Description = "Crachá", Family = "SERVICOS", MaterialRequestable = true });
        await db.SaveChangesAsync();

        var resumo = await catalog.SummaryAsync();
        Assert.Equal(2, resumo.SemCusto);   // FAR-001 e SRV-002; o inativo e o serviço não contam

        var lista = await catalog.ListAsync(null, null, includeInactive: false, semCusto: true);
        Assert.Equal(["FAR-001", "SRV-002"], lista.Select(i => i.Code).Order().ToArray());
    }
}
