using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Materials;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O recorte do Painel de Atendimentos.
///
/// <para>
/// O painel não tinha filtro nenhum e abria as últimas 500 solicitações: "Em andamento: 37" era
/// um número que ninguém conseguia recortar. O que estes testes protegem é a honestidade do
/// recorte — a janela de criação incluir as duas pontas, o solicitante ser o id e não o nome, e
/// o filtro valer sobre o <b>cadastro inteiro</b>, não só dentro das últimas quinhentas.
/// </para>
/// </summary>
public class RecorteDoPainelTests
{
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Bruno = Guid.NewGuid();
    /// <summary>A homônima: outra pessoa, o mesmo "Ana Silva" na tela.</summary>
    private static readonly Guid OutraAna = Guid.NewGuid();

    private static AppDbContext Banco() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static MaterialRequisition Mr(
        string numero, string centro, Guid quem, DateTimeOffset criada) => new()
        {
            Number = numero, CostCenter = centro, RequesterId = quem,
            RequesterLabel = quem == Bruno ? "Bruno Lima" : "Ana Silva",
            CreatedAt = criada, UpdatedAt = criada,
        };

    private static async Task<AppDbContext> ComDadosAsync()
    {
        var db = Banco();
        db.MaterialRequisitions.AddRange(
            Mr("MR-1", "BAH-001", Ana, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero)),
            Mr("MR-2", "BAH-001", Bruno, new DateTimeOffset(2026, 9, 15, 17, 30, 0, TimeSpan.Zero)),
            Mr("MR-3", "PER-002", Ana, new DateTimeOffset(2026, 9, 30, 23, 45, 0, TimeSpan.Zero)),
            Mr("MR-4", "PER-002", Bruno, new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero)),
            // a homônima entra no mesmo centro e na mesma janela: só o id as separa
            Mr("MR-5", "BAH-001", OutraAna, new DateTimeOffset(2026, 9, 2, 8, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<string[]> RecortarAsync(
        AppDbContext db, string? centro = null, Guid? quem = null,
        DateOnly? de = null, DateOnly? ate = null)
    {
        var itens = await RecorteDoPainel
            .Filtrar(db.MaterialRequisitions, centro, quem, de, ate)
            .OrderBy(r => r.Number).Select(r => r.Number).ToListAsync();
        return [.. itens];
    }

    [Fact]
    public async Task Sem_recorte_nenhum_o_painel_continua_sendo_o_de_antes()
    {
        // o filtro nasceu numa tela em uso: abrir sem nada marcado precisa devolver o mesmo
        // painel que o almoxarife já conhecia
        await using var db = await ComDadosAsync();
        Assert.Equal(["MR-1", "MR-2", "MR-3", "MR-4", "MR-5"], await RecortarAsync(db));
    }

    [Fact]
    public async Task O_centro_recorta_sem_depender_da_caixa()
    {
        await using var db = await ComDadosAsync();
        Assert.Equal(["MR-1", "MR-2", "MR-5"], await RecortarAsync(db, centro: "BAH-001"));
        // o valor vem da quebra do próprio painel, mas a rota aceita o que o navegador manda
        Assert.Equal(["MR-1", "MR-2", "MR-5"], await RecortarAsync(db, centro: " bah-001 "));
    }

    [Fact]
    public async Task O_solicitante_recorta_pelo_id_e_nao_pelo_nome()
    {
        // MR-1, MR-3 e MR-5 aparecem na tela como "Ana Silva", mas MR-5 é de outra pessoa:
        // pelo nome o painel somaria as duas numa só, e cobraria de Ana a solicitação alheia
        await using var db = await ComDadosAsync();
        Assert.Equal(["MR-1", "MR-3"], await RecortarAsync(db, quem: Ana));
        Assert.Equal(["MR-5"], await RecortarAsync(db, quem: OutraAna));
    }

    [Fact]
    public async Task A_janela_de_criacao_inclui_as_duas_pontas_inteiras()
    {
        await using var db = await ComDadosAsync();

        // o dia de início conta desde a meia-noite dele: MR-1 entrou às 8h
        Assert.Equal(["MR-1", "MR-2", "MR-3", "MR-4", "MR-5"],
            await RecortarAsync(db, de: new DateOnly(2026, 9, 1)));

        // e o dia final conta até o fim dele: MR-3 entrou 23h45 do dia 30, e comparar com a
        // meia-noite do próprio dia 30 esconderia justamente o dia que o usuário pediu
        Assert.Equal(["MR-1", "MR-2", "MR-3", "MR-5"],
            await RecortarAsync(db, de: new DateOnly(2026, 9, 1), ate: new DateOnly(2026, 9, 30)));
    }

    [Fact]
    public async Task Os_recortes_se_somam_em_vez_de_um_substituir_o_outro()
    {
        await using var db = await ComDadosAsync();
        Assert.Equal(["MR-1"], await RecortarAsync(db, centro: "BAH-001", quem: Ana));
        Assert.Empty(await RecortarAsync(db, centro: "PER-002", quem: Ana,
            de: new DateOnly(2026, 10, 2)));
    }

    [Fact]
    public async Task As_opcoes_do_filtro_saem_do_cadastro_inteiro_e_nao_do_recorte()
    {
        // se saíssem do recorte, filtrar o BAH-001 faria o seletor passar a oferecer só o
        // BAH-001: trocar de centro exigiria limpar o filtro e começar de novo
        await using var db = await ComDadosAsync();
        var recortado = RecorteDoPainel.Filtrar(db.MaterialRequisitions, "BAH-001", null, null, null);
        Assert.Equal(["MR-1", "MR-2", "MR-5"],
            await recortado.OrderBy(r => r.Number).Select(r => r.Number).ToListAsync());

        var opcoes = await RecorteDoPainel.OpcoesAsync(db.MaterialRequisitions);
        Assert.Equal(["BAH-001", "PER-002"], opcoes.Centros);
    }

    [Fact]
    public async Task O_seletor_de_solicitante_lista_os_homonimos_separados()
    {
        // é o id que recorta, então a homônima precisa ser uma segunda linha no seletor —
        // juntá-las pelo nome tornaria uma delas inalcançável pelo filtro
        await using var db = await ComDadosAsync();
        var opcoes = await RecorteDoPainel.OpcoesAsync(db.MaterialRequisitions);

        Assert.Equal(3, opcoes.Solicitantes.Length);
        Assert.Equal(2, opcoes.Solicitantes.Count(s => s.Label == "Ana Silva"));
        Assert.Equal([Ana, OutraAna], [.. opcoes.Solicitantes.Where(s => s.Label == "Ana Silva")
            .Select(s => s.Id).OrderBy(id => id == Ana ? 0 : 1)]);
        // ordenado pelo rótulo: é o seletor que a pessoa lê
        Assert.Equal("Bruno Lima", opcoes.Solicitantes[^1].Label);
    }

    [Fact]
    public async Task O_recorte_vale_sobre_o_cadastro_inteiro_e_nao_dentro_do_teto()
    {
        // é por isso que `Filtrar` entra antes do `Take`: a solicitação antiga daquele centro
        // sumiria do painel filtrado sem ninguém entender por quê
        await using var db = Banco();
        var antiga = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);
        db.MaterialRequisitions.Add(Mr("MR-ANTIGA", "BAH-001", Ana, antiga));
        for (var i = 0; i < RecorteDoPainel.Teto + 20; i++)
            db.MaterialRequisitions.Add(Mr($"MR-NOVA-{i:000}", "PER-002", Bruno, antiga.AddDays(100 + i)));
        await db.SaveChangesAsync();

        var recortado = await RecorteDoPainel.Filtrar(db.MaterialRequisitions, "BAH-001", null, null, null)
            .OrderByDescending(r => r.CreatedAt).Take(RecorteDoPainel.Teto)
            .Select(r => r.Number).ToListAsync();
        Assert.Equal(["MR-ANTIGA"], recortado);
    }
}
