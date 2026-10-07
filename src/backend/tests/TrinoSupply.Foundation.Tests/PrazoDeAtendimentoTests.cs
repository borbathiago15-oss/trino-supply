using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Materials;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O prazo de atendimento do almoxarifado, por família.
///
/// <para>
/// O que estes testes protegem: o <b>padrão</b> valer para quem não configurou nada (a tabela
/// nasce vazia), a <b>herança</b> não congelar o número do padrão, o <b>zero desligar</b> sem
/// zerar a solicitação inteira, e a solicitação responder pelo prazo <b>mais curto</b> dos itens
/// dela — valer o mais longo esconderia o item de dois dias parado dez.
/// </para>
/// </summary>
public class PrazoDeAtendimentoTests
{
    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    private static readonly DateTimeOffset Agora = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Actor Admin = new(Guid.NewGuid(), "Admin", Roles.SystemAdministrator);
    private static readonly Actor Almoxarife = new(Guid.NewGuid(), "Zé", Roles.WarehouseOperator);

    private static PrazoDeAtendimentoService Montar(out AppDbContext db)
    {
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return new PrazoDeAtendimentoService(db, new RelogioFixo(Agora));
    }

    private static async Task FamiliasAsync(AppDbContext db, params (string Nome, bool DeAlmoxarifado)[] familias)
    {
        foreach (var (nome, material) in familias)
            db.ProductFamilies.Add(new ProductFamily { Name = nome, MaterialRequestable = material });
        await db.SaveChangesAsync();
    }

    // ---- o cadastro -----------------------------------------------------------------

    [Fact]
    public async Task Sem_nada_configurado_toda_familia_cai_no_padrao()
    {
        // a tabela nasce vazia, e vazio quer dizer "ninguém configurou" — não "sem prazo"
        var svc = Montar(out var db);
        await FamiliasAsync(db, ("EPI", true), ("MATERIAL DE LIMPEZA", true));

        var atuais = await svc.AtuaisAsync();
        Assert.Equal([null, "EPI", "MATERIAL DE LIMPEZA"], atuais.Select(s => s.Family));
        Assert.All(atuais, s => Assert.Equal(PrazoDeAtendimentoService.PadraoDeDias, s.MaxDays));
        Assert.Empty(await svc.PropriasAsync());
    }

    [Fact]
    public async Task So_entram_as_familias_que_vao_ao_almoxarifado()
    {
        // prazo de atendimento de uma família que nunca chega ao almoxarifado é linha que o
        // administrador precisa ler para descobrir que não serve
        var svc = Montar(out var db);
        await FamiliasAsync(db, ("EPI", true), ("SERVICOS DE ENGENHARIA", false));

        Assert.Equal([null, "EPI"], (await svc.AtuaisAsync()).Select(s => s.Family));
    }

    [Fact]
    public async Task A_familia_que_define_o_seu_prazo_para_de_herdar_e_o_resto_segue_o_padrao()
    {
        var svc = Montar(out var db);
        await FamiliasAsync(db, ("EPI", true), ("MATERIAL DE LIMPEZA", true));

        var (salvos, erro) = await svc.SalvarAsync(Admin,
            new Dictionary<string, int> { [""] = 4, ["EPI"] = 1 });
        Assert.Null(erro);

        var mapa = salvos!.ToDictionary(s => s.Family ?? "", s => s.MaxDays);
        Assert.Equal(4, mapa[""]);
        Assert.Equal(1, mapa["EPI"]);
        Assert.Equal(4, mapa["MATERIAL DE LIMPEZA"]);   // herdada
        Assert.Equal(["EPI"], await svc.PropriasAsync());
    }

    [Fact]
    public async Task Mudar_o_padrao_move_junto_quem_herdou_e_deixa_a_excecao_onde_esta()
    {
        var svc = Montar(out var db);
        await FamiliasAsync(db, ("EPI", true), ("MATERIAL DE LIMPEZA", true));
        await svc.SalvarAsync(Admin, new Dictionary<string, int> { [""] = 4, ["EPI"] = 1 });

        await svc.SalvarAsync(Admin, new Dictionary<string, int> { [""] = 7 });

        var mapa = (await svc.AtuaisAsync()).ToDictionary(s => s.Family ?? "", s => s.MaxDays);
        Assert.Equal(7, mapa["MATERIAL DE LIMPEZA"]);   // seguiu o padrão
        Assert.Equal(1, mapa["EPI"]);                   // a exceção é dela, e fica
    }

    [Fact]
    public async Task Voltar_a_herdar_apaga_a_excecao_em_vez_de_copiar_o_numero()
    {
        // copiar o número do padrão congelaria a cópia, e a família deixaria de acompanhar
        // a próxima mudança do padrão — foi o que a herança existe para evitar
        var svc = Montar(out var db);
        await FamiliasAsync(db, ("EPI", true));
        await svc.SalvarAsync(Admin, new Dictionary<string, int> { [""] = 4, ["EPI"] = 1 });

        await svc.SalvarAsync(Admin, new Dictionary<string, int>(), herdar: ["EPI"]);
        Assert.Empty(await svc.PropriasAsync());

        await svc.SalvarAsync(Admin, new Dictionary<string, int> { [""] = 9 });
        Assert.Equal(9, (await svc.AtuaisAsync()).Single(s => s.Family == "EPI").MaxDays);
    }

    [Fact]
    public async Task O_padrao_nunca_se_apaga_porque_e_ele_que_sobra()
    {
        var svc = Montar(out var db);
        await FamiliasAsync(db, ("EPI", true));
        await svc.SalvarAsync(Admin, new Dictionary<string, int> { [""] = 4 });

        // mandar o padrão para herdar não faz sentido e não pode derrubá-lo
        await svc.SalvarAsync(Admin, new Dictionary<string, int>(), herdar: ["", "  "]);
        Assert.Equal(4, (await svc.AtuaisAsync()).Single(s => s.Family is null).MaxDays);
    }

    [Fact]
    public async Task A_familia_chega_normalizada_como_o_cadastro_a_guarda()
    {
        var svc = Montar(out var db);
        await FamiliasAsync(db, ("EPI", true));

        var (_, erro) = await svc.SalvarAsync(Admin, new Dictionary<string, int> { [" epi "] = 3 });
        Assert.Null(erro);
        Assert.Equal(["EPI"], await svc.PropriasAsync());
        Assert.Equal(3, (await svc.AtuaisAsync()).Single(s => s.Family == "EPI").MaxDays);
    }

    [Fact]
    public async Task Papel_que_nao_define_a_regua_recebe_recusa_e_nao_grava()
    {
        var svc = Montar(out var db);
        await FamiliasAsync(db, ("EPI", true));

        var (salvos, erro) = await svc.SalvarAsync(Almoxarife, new Dictionary<string, int> { ["EPI"] = 1 });
        Assert.Null(salvos);
        Assert.Equal("MSLA-ERR-900", erro!.Code);
        Assert.Empty(await svc.PropriasAsync());
    }

    [Fact]
    public async Task Prazo_fora_da_faixa_e_familia_desconhecida_sao_recusados()
    {
        var svc = Montar(out var db);
        await FamiliasAsync(db, ("EPI", true));

        Assert.Equal("MSLA-ERR-010",
            (await svc.SalvarAsync(Admin, new Dictionary<string, int> { ["EPI"] = 400 })).error!.Code);
        Assert.Equal("MSLA-ERR-010",
            (await svc.SalvarAsync(Admin, new Dictionary<string, int> { ["EPI"] = -1 })).error!.Code);
        Assert.Equal("MSLA-ERR-011",
            (await svc.SalvarAsync(Admin, new Dictionary<string, int> { ["NAO EXISTE"] = 3 })).error!.Code);

        // a forma vem antes da referência: "0 a 365" é mais útil que "família desconhecida"
        // quando os dois estão errados
        Assert.Equal("MSLA-ERR-010",
            (await svc.SalvarAsync(Admin, new Dictionary<string, int> { ["NAO EXISTE"] = 400 })).error!.Code);
    }

    // ---- o teto da solicitação ------------------------------------------------------

    private static readonly Dictionary<string, int> Prazos = new()
    {
        [""] = 5, ["EPI"] = 2, ["MATERIAL DE LIMPEZA"] = 10, ["FERRAGENS"] = 0,
    };

    [Fact]
    public void A_solicitacao_responde_pelo_prazo_mais_curto_dos_itens()
    {
        // valer o mais longo deixaria o item de dois dias parado dez dentro de uma
        // solicitação "no prazo" — a cobrança desapareceria onde ela serve
        var teto = PrazoDeAtendimentoService.TetoDaSolicitacao(["MATERIAL DE LIMPEZA", "EPI"], Prazos);
        Assert.Equal((2, "EPI"), teto);
    }

    [Fact]
    public void Familia_em_zero_sai_da_conta_em_vez_de_ser_a_mais_curta()
    {
        // zero quer dizer "aqui não cobramos tempo"; tratá-lo como prazo de zero dia faria
        // toda solicitação que a tocasse nascer estourada
        Assert.Equal((2, "EPI"), PrazoDeAtendimentoService.TetoDaSolicitacao(["FERRAGENS", "EPI"], Prazos));
        // e se todas estão em zero, não há prazo nenhum
        Assert.Null(PrazoDeAtendimentoService.TetoDaSolicitacao(["FERRAGENS"], Prazos));
    }

    [Fact]
    public void Familia_fora_do_cadastro_cai_no_padrao_e_nao_fica_sem_prazo()
    {
        // ela sai do cadastro e a solicitação dela continua existindo
        Assert.Equal((5, (string?)null), PrazoDeAtendimentoService.TetoDaSolicitacao([null, ""], Prazos));
        Assert.Equal((5, "SUMIU"), PrazoDeAtendimentoService.TetoDaSolicitacao(["SUMIU"], Prazos));
    }

    // ---- o veredito -----------------------------------------------------------------

    [Fact]
    public void O_relogio_comeca_na_liberacao_do_Nivel_1()
    {
        // contar da criação cobraria do almoxarife o tempo que a solicitação passou
        // esperando a aprovação do centro, que não é dele
        var aprovada = Agora.AddDays(-3);
        var s = PrazoDeAtendimentoService.Avaliar(aprovada, null, Agora, ["EPI"], Prazos);
        Assert.Equal(3, s.Days);
        Assert.Equal(2, s.MaxDays);
        Assert.Equal("ESTOURADO", s.Status);
        Assert.True(s.Breached);
        Assert.Equal("EPI", s.Family);
    }

    [Fact]
    public void Sem_aprovacao_nao_ha_relogio_porque_a_vez_e_do_Nivel_1()
    {
        var s = PrazoDeAtendimentoService.Avaliar(null, null, Agora, ["EPI"], Prazos);
        Assert.Equal(2, s.MaxDays);     // o prazo existe e a tela pode mostrá-lo
        Assert.Null(s.Days);            // mas não há espera a medir
        Assert.Null(s.Status);
        Assert.False(s.Breached);
    }

    [Fact]
    public void A_atencao_chega_antes_do_estouro_pela_mesma_fracao_do_prazo_da_etapa()
    {
        // avisar no dia do vencimento é avisar tarde para agir
        var prazos = new Dictionary<string, int> { [""] = 10 };
        string Em(int dias) => PrazoDeAtendimentoService
            .Avaliar(Agora.AddDays(-dias), null, Agora, [null], prazos).Status!;

        Assert.Equal("OK", Em(7));
        Assert.Equal("ATENCAO", Em(8));        // 80% do prazo
        Assert.Equal("ATENCAO", Em(10));       // no próprio dia do prazo ainda dá tempo
        Assert.Equal("ESTOURADO", Em(11));     // passou
    }

    [Fact]
    public void Solicitacao_atendida_e_medida_ate_o_atendimento_e_nao_ate_agora()
    {
        // ali a pergunta deixou de ser "está atrasada?" e passou a ser "foi atendida no prazo?"
        var aprovada = Agora.AddDays(-30);
        var atendida = aprovada.AddDays(1);
        var s = PrazoDeAtendimentoService.Avaliar(aprovada, atendida, Agora, ["EPI"], Prazos);
        Assert.Equal(1, s.Days);
        Assert.Equal("OK", s.Status);
    }

    // ---- a lista inteira numa consulta ---------------------------------------------

    [Fact]
    public async Task A_situacao_sai_da_familia_do_catalogo_porque_o_item_nao_a_guarda()
    {
        var svc = Montar(out var db);
        await FamiliasAsync(db, ("EPI", true), ("MATERIAL DE LIMPEZA", true));
        await svc.SalvarAsync(Admin, new Dictionary<string, int> { [""] = 5, ["EPI"] = 2 });

        var luva = new CatalogItem { Code = "EPI-01", Description = "Luva", Family = "EPI", UnitOfMeasure = "PAR" };
        var pano = new CatalogItem { Code = "LIM-01", Description = "Pano", Family = "MATERIAL DE LIMPEZA", UnitOfMeasure = "UN" };
        db.CatalogItems.AddRange(luva, pano);

        var mista = new MaterialRequisition
        {
            Number = "MR-1", CostCenter = "BAH-001", RequesterId = Guid.NewGuid(), RequesterLabel = "Ana",
            ApprovedAt = Agora.AddDays(-3),
            Items =
            [
                new MaterialRequisitionItem { CatalogItemId = luva.Id, CatalogCode = "EPI-01", Description = "Luva", Quantity = 1 },
                new MaterialRequisitionItem { CatalogItemId = pano.Id, CatalogCode = "LIM-01", Description = "Pano", Quantity = 1 },
            ],
        };
        var soLimpeza = new MaterialRequisition
        {
            Number = "MR-2", CostCenter = "BAH-001", RequesterId = Guid.NewGuid(), RequesterLabel = "Ana",
            ApprovedAt = Agora.AddDays(-3),
            Items = [new MaterialRequisitionItem { CatalogItemId = pano.Id, CatalogCode = "LIM-01", Description = "Pano", Quantity = 1 }],
        };
        db.MaterialRequisitions.AddRange(mista, soLimpeza);
        await db.SaveChangesAsync();

        var situacoes = await svc.SituacoesAsync([mista, soLimpeza]);

        // a mista responde pelo EPI, que é o mais curto, e por isso já estourou
        Assert.Equal("ESTOURADO", situacoes[mista.Id].Status);
        Assert.Equal("EPI", situacoes[mista.Id].Family);
        // a de limpeza herdou o padrão de 5 e está dentro dele
        Assert.Equal(5, situacoes[soLimpeza.Id].MaxDays);
        Assert.Equal("OK", situacoes[soLimpeza.Id].Status);
    }

    [Fact]
    public async Task Lista_vazia_nao_consulta_nada()
    {
        var svc = Montar(out _);
        Assert.Empty(await svc.SituacoesAsync([]));
    }
}
