using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// Os cadastros de pagamento existem para acabar com o texto livre: o comprador
/// digitava "Boleto", "boleto bancario" e "BOLETO" em processos diferentes, e nenhum
/// relatório conseguia somar os três. O que estes testes protegem é justamente isso —
/// o nome único, o padrão único, e o cadastro usado que não desaparece do histórico.
/// </summary>
public class PagamentoServiceTests
{
    private static (AppDbContext Db, PagamentoService Svc) Build()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (db, new PagamentoService(db, TimeProvider.System));
    }

    [Fact]
    public async Task Forma_repetida_e_recusada_mesmo_com_outra_caixa()
    {
        // "Boleto" e "boleto" são a mesma forma para quem lê o relatório; se o cadastro
        // aceitasse as duas, o texto livre teria voltado por dentro do cadastro
        var (_, svc) = Build();
        var (forma, erro) = await svc.CriarFormaAsync("Boleto Bancário");
        Assert.Null(erro);
        Assert.NotNull(forma);

        var (repetida, conflito) = await svc.CriarFormaAsync("  boleto bancário  ");
        Assert.Null(repetida);
        Assert.Equal("PAY-ERR-011", conflito!.Code);
    }

    [Fact]
    public async Task Forma_sem_nome_util_nao_entra()
    {
        var (_, svc) = Build();
        foreach (var nome in new[] { null, "", "   ", "P" })
        {
            var (forma, erro) = await svc.CriarFormaAsync(nome);
            Assert.Null(forma);
            Assert.Equal("PAY-ERR-010", erro!.Code);
        }
    }

    [Fact]
    public async Task Forma_desativada_sai_da_lista_de_uso_mas_continua_no_cadastro()
    {
        // desativar não é apagar: a proposta antiga que escolheu esta forma tem de
        // continuar legível, e é por isso que o serviço não expõe exclusão
        var (_, svc) = Build();
        var (forma, _) = await svc.CriarFormaAsync("Cheque");
        await svc.AtualizarFormaAsync(forma!.Id, null, ativa: false);

        Assert.Empty(await svc.FormasAsync(incluirInativas: false));
        Assert.Single(await svc.FormasAsync(incluirInativas: true));
    }

    [Fact]
    public async Task Condicao_precisa_de_ao_menos_uma_parcela_e_prazo_nao_negativo()
    {
        var (_, svc) = Build();
        var (semParcela, e1) = await svc.CriarCondicaoAsync("Parcelado", 0, 30, false);
        Assert.Null(semParcela);
        Assert.Equal("PAY-ERR-021", e1!.Code);

        var (prazoNegativo, e2) = await svc.CriarCondicaoAsync("Parcelado", 2, -1, false);
        Assert.Null(prazoNegativo);
        Assert.Equal("PAY-ERR-022", e2!.Code);
    }

    [Fact]
    public async Task So_uma_condicao_e_a_padrao_por_vez()
    {
        // a padrão é o que a tela de proposta sugere sozinha: duas marcadas fariam a
        // sugestão depender da ordem da consulta, que é o mesmo que não ter sugestão
        var (_, svc) = Build();
        var (aVista, _) = await svc.CriarCondicaoAsync("À Vista", 1, 0, padrao: true);
        var (trinta, _) = await svc.CriarCondicaoAsync("Parcelado 30/60/90", 3, 30, padrao: true);

        var lista = await svc.CondicoesAsync(incluirInativas: true);
        Assert.Equal([trinta!.Id], lista.Where(c => c.IsDefault).Select(c => c.Id));
        Assert.False(lista.Single(c => c.Id == aVista!.Id).IsDefault);
    }

    [Fact]
    public async Task Condicao_desativada_deixa_de_ser_a_padrao()
    {
        // se continuasse padrão, a tela sugeriria por omissão exatamente a condição
        // que ela mesma tirou da lista — e o comprador não teria como desfazer
        var (_, svc) = Build();
        var (condicao, _) = await svc.CriarCondicaoAsync("Parcelado 14/28", 2, 14, padrao: true);
        var (atualizada, erro) = await svc.AtualizarCondicaoAsync(condicao!.Id, null, null, null, null, ativa: false);

        Assert.Null(erro);
        Assert.False(atualizada!.IsDefault);
    }

    [Fact]
    public async Task Seed_entra_uma_vez_e_nao_ressuscita_o_que_foi_desativado()
    {
        // reiniciar a aplicação não pode desfazer a decisão de quem desligou um cadastro
        var (db, svc) = Build();
        await PagamentoSeeder.SeedAsync(db, TimeProvider.System);

        var formas = await svc.FormasAsync(incluirInativas: true);
        Assert.Equal(5, formas.Count);
        Assert.Contains(formas, f => f.Name == "Pix");

        var condicoes = await svc.CondicoesAsync(incluirInativas: true);
        Assert.Equal(4, condicoes.Count);
        // o prazo da primeira parcela é o primeiro número do nome — é ele que preenche
        // sozinho o "prazo para pagamento" da proposta
        Assert.Equal(30, condicoes.Single(c => c.Name == "Parcelado 30/60/90").FirstDueDays);
        Assert.Equal(0, condicoes.Single(c => c.Name == "À Vista").FirstDueDays);
        // nenhuma nasce padrão: sugerir uma que ninguém escolheu seria decidir pelo comprador
        Assert.DoesNotContain(condicoes, c => c.IsDefault);

        var pix = formas.Single(f => f.Name == "Pix");
        await svc.AtualizarFormaAsync(pix.Id, null, ativa: false);
        await PagamentoSeeder.SeedAsync(db, TimeProvider.System);

        Assert.Equal(5, (await svc.FormasAsync(incluirInativas: true)).Count);
        Assert.False((await svc.FormasAsync(incluirInativas: true)).Single(f => f.Name == "Pix").Active);
    }

    [Fact]
    public void Manter_o_cadastro_e_de_quem_compra()
    {
        Assert.True(PagamentoService.CanMaintain(Roles.PurchasingOfficer));
        Assert.True(PagamentoService.CanMaintain(Roles.SupplyManager));
        Assert.True(PagamentoService.CanMaintain(Roles.SystemAdministrator));
        // solicitante e auditor leem a lista, mas não a escrevem
        Assert.False(PagamentoService.CanMaintain(Roles.Requester));
        Assert.False(PagamentoService.CanMaintain(Roles.Auditor));
    }
}
