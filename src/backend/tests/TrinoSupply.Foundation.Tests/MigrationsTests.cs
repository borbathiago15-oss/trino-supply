using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Suporte;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// As demais suítes rodam em EF InMemory, onde migration nenhuma é exercitada. Este teste sobe um
/// Postgres 16 de verdade (Testcontainers) e faz o que o <c>Program.cs</c> faz no startup:
/// <c>MigrateAsync</c> do zero. É o que descobre, antes do deploy, uma migration que não aplica
/// ou um modelo que já se afastou da última migration.
/// </summary>
public sealed class MigrationsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16").Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    [Fact]
    public async Task Todas_as_migrations_aplicam_num_Postgres_real_e_o_modelo_nao_tem_desvio()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .Options;
        await using var db = new AppDbContext(options);

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(
            db.Database.GetMigrations().Count(),
            (await db.Database.GetAppliedMigrationsAsync()).Count());

        // O mesmo que `dotnet ef migrations has-pending-model-changes`, que o CLAUDE.md pede
        // antes de todo PR que toca o modelo — aqui ninguém precisa lembrar de rodar.
        Assert.False(db.Database.HasPendingModelChanges(),
            "o modelo mudou depois da última migration: gere uma com `dotnet ef migrations add`");
    }

    /// <summary>
    /// O provedor em memória aceita LINQ que o Npgsql não traduz — foi assim que a aprovação
    /// da diretoria virou um 500 em produção (filtro sobre um record projetado, no histórico
    /// de preço) com a suíte inteira verde. As consultas que só rodam em caminho de gravação
    /// passam aqui pelo Postgres de verdade, com o banco vazio: o que se testa é a tradução.
    /// </summary>
    [Fact]
    public async Task Consultas_dos_servicos_traduzem_no_Postgres_real()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        var produto = Guid.NewGuid();
        var historico = new HistoricoDePrecoService(db);
        Assert.Empty(await historico.ResumoAsync([produto, Guid.NewGuid()]));
        Assert.Empty(await historico.SerieAsync(produto));

        var rfq = new QuotationService(db, TimeProvider.System);
        Assert.Empty(await rfq.PendingApprovalsAsync(Roles.Director, Guid.NewGuid()));
        Assert.Empty(await rfq.PendingApprovalsAsync(Roles.Approver, Guid.NewGuid()));
        Assert.Empty(await rfq.MinhasDecisoesAsync(Guid.NewGuid()));

        // duas consultas juntadas em memória: o Concat delas no LINQ não teria tradução
        Assert.Empty(await LocaisDeEntrega.ListarAsync(db));

        // roda na aprovação do Nível 1, que é gravação: `Contains` com array sobre a entidade
        var vazia = new Quotation { Number = "RFQ-0", CostCenter = "CC-01" };
        Assert.Equal(CaminhoDoNivel2.Padrao,
            (await AlcadaDoComprador.RotaAsync(db, vazia, Guid.NewGuid())).Caminho);

        // o chamado de suporte: os atendentes saem de um filtro sobre o texto dos módulos, e a
        // abertura grava chamado, primeira mensagem e avisos na mesma transação
        var chamados = new ChamadoService(db, TimeProvider.System, new AvisoDoUsuarioService(db, TimeProvider.System));
        var quem = new QuemChama(Guid.NewGuid(), "Solicitante", false);
        var (chamado, erro) = await chamados.AbrirAsync(quem,
            new("DUVIDA", "Não acho o botão", "Onde fica o botão de aprovar a SC?", "/aprovacoes", "Central de Aprovação", null));
        Assert.Null(erro);
        Assert.Empty(await chamados.AtendentesAsync());
        Assert.Single(await chamados.ListarAsync(quem, fila: false, situacao: null));
        Assert.Equal((0, (int?)null), await chamados.ResumoAsync(quem));
        Assert.Single((await chamados.AbrirParaLerAsync(quem, chamado!.Id))!.Messages);

        // roda na exclusão do produto: as sete contagens de "onde ele já circulou"
        Assert.Empty(await new TrinoSupply.Foundation.Api.Catalog.CatalogService(db, TimeProvider.System)
            .UsosAsync(Guid.NewGuid()));
    }
}
