using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// Os recortes da lista de processos: período, centro de custo e quem abriu. Todos são do
/// servidor — peneirar no navegador sobre uma lista já truncada diria "nada encontrado"
/// para processo que existe (PO-BR-012).
/// </summary>
public class FiltrosDaListaDeCotacoesTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Agora = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Carla = Guid.NewGuid();
    private static readonly Guid Caio = Guid.NewGuid();

    private static QuotationService Build(out AppDbContext db)
    {
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return new QuotationService(db, new FixedTimeProvider(Agora));
    }

    private static Quotation Processo(
        string numero, string centro, Guid autor, string rotulo, DateTimeOffset aberto) => new()
    {
        Number = numero, CostCenter = centro, CreatedBy = autor, CreatedByLabel = rotulo,
        CreatedAt = aberto, Kind = QuotationKind.Purchase, Status = QuotationStatus.Open,
    };

    private static async Task<QuotationService> AcervoAsync()
    {
        var rfq = Build(out var db);
        db.Quotations.AddRange(
            Processo("RFQ-1", "CC-01", Carla, "Carla Compradora", Agora.AddDays(-30)),
            Processo("RFQ-2", "CC-02", Carla, "Carla Compradora", Agora.AddDays(-5)),
            Processo("RFQ-3", "CC-01", Caio, "Caio Comprador", Agora.AddDays(-1)));
        await db.SaveChangesAsync();
        return rfq;
    }

    [Fact]
    public async Task Sem_recorte_vem_tudo()
    {
        var (itens, total) = await (await AcervoAsync()).ListAsync();
        Assert.Equal(3, total);
        Assert.Equal(3, itens.Count);
    }

    [Fact]
    public async Task O_centro_de_custo_recorta_e_o_total_acompanha()
    {
        // o total é quantos existem no recorte, não quantos couberam na página
        var (itens, total) = await (await AcervoAsync()).ListAsync(centroCusto: "CC-01");
        Assert.Equal(2, total);
        Assert.Equal(["RFQ-3", "RFQ-1"], itens.Select(q => q.Number));
    }

    [Fact]
    public async Task Quem_abriu_recorta_pelo_id_e_nao_pelo_nome()
    {
        // é o que permite agrupar de verdade: dois "Carla" digitados diferente
        // seriam duas pessoas se o filtro fosse por texto
        var (itens, _) = await (await AcervoAsync()).ListAsync(abertoPor: Caio);
        Assert.Equal(["RFQ-3"], itens.Select(q => q.Number));
    }

    [Fact]
    public async Task O_periodo_inclui_o_dia_inteiro_do_fim()
    {
        // RFQ-3 abriu às 12h do dia 21; um filtro até o dia 21 que parasse à meia-noite
        // deixaria de fora o processo do próprio dia que a pessoa pediu
        var rfq = await AcervoAsync();
        var dia = DateOnly.FromDateTime(Agora.AddDays(-1).UtcDateTime);
        var (itens, _) = await rfq.ListAsync(de: dia, ate: dia);
        Assert.Equal(["RFQ-3"], itens.Select(q => q.Number));
    }

    [Fact]
    public async Task Os_recortes_se_somam()
    {
        var rfq = await AcervoAsync();
        var (itens, total) = await rfq.ListAsync(centroCusto: "CC-01", abertoPor: Carla);
        Assert.Equal(1, total);
        Assert.Equal(["RFQ-1"], itens.Select(q => q.Number));
    }

    [Fact]
    public async Task As_opcoes_saem_do_que_existe_e_sem_repetir()
    {
        // oferecer o cadastro inteiro encheria a caixa de centro que nunca abriu
        // cotação, e quem escolhesse receberia lista vazia sem entender por quê
        var (centros, autores) = await (await AcervoAsync()).OpcoesDaListaAsync();
        Assert.Equal(["CC-01", "CC-02"], centros);
        Assert.Equal(["Caio Comprador", "Carla Compradora"], autores.Select(a => a.Label));
        Assert.Equal(2, autores.Count);   // Carla abriu dois processos e aparece uma vez
    }
}
