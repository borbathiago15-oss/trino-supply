using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O fornecedor cadastrado duas vezes.
///
/// O CNPJ é opcional no cadastro, e por isso não podia ser o único guarda contra duplicata:
/// o pré-cadastro da cotação nasce só com razão social e telefone (§7), e cadastrar de novo o
/// mesmo fornecedor "agora com CNPJ" criava um segundo registro. Ficavam dois: o primeiro
/// PROSPECT e preso à cotação que o convidou, o segundo homologado. O comprador via
/// "não pode vencer" num fornecedor que ele mesmo tinha homologado.
/// </summary>
public class CadastroDuplicadoTests
{
    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    private static readonly Guid Comprador = Guid.NewGuid();

    private static SupplierService Build() => new(
        new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
        new RelogioFixo(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero)));

    [Theory]
    [InlineData("Pontes Tour", "PONTES TOUR")]
    [InlineData("PONTES  TOUR.", "Pontes Tour")]
    [InlineData("Pontes Tóur", "Pontes Tour")]
    [InlineData("pontes-tour", "PONTES TOUR")]
    public async Task A_mesma_razao_social_escrita_de_outro_jeito_nao_cria_um_segundo_cadastro(
        string primeiro, string segundo)
    {
        var svc = Build();
        var (um, semErro) = await svc.CreateAsync(Comprador, primeiro, null, null, null, "11999998888");
        Assert.Null(semErro);
        Assert.NotNull(um);

        var (dois, erro) = await svc.CreateAsync(Comprador, segundo, null, "11222333000181", null, "1133334444");

        Assert.Null(dois);
        Assert.Equal("SUP-ERR-015", erro!.Code);
        Assert.Contains(primeiro, erro.Message);
    }

    [Fact]
    public async Task O_erro_do_pre_cadastro_diz_o_caminho_que_resolve()
    {
        // "já existe" sem dizer o que fazer manda o comprador cadastrar de novo com outro
        // nome, que é exatamente a duplicata que se quer evitar
        var svc = Build();
        await svc.CreateAsync(Comprador, "Pontes Tour", null, null, null, "11999998888");

        var (_, erro) = await svc.CreateAsync(Comprador, "PONTES TOUR", null, "11222333000181", null, "1133334444");

        Assert.Contains("pré-cadastro", erro!.Message);
        Assert.Contains("CPF/CNPJ", erro.Message);
    }

    [Fact]
    public async Task Fornecedor_inativo_e_recusado_dizendo_que_o_caminho_e_reativar()
    {
        var svc = Build();
        var (existente, _) = await svc.CreateAsync(Comprador, "Pontes Tour", null, "11222333000181", null, "11999998888");
        await svc.UpdateAsync(existente!.Id, null, null, null, active: false);

        var (_, erro) = await svc.CreateAsync(Comprador, "Pontes Tour", null, null, null, "1133334444");

        Assert.Equal("SUP-ERR-015", erro!.Code);
        Assert.Contains("inativo", erro.Message);
        Assert.Contains("Reative", erro.Message);
    }

    [Fact]
    public async Task O_CNPJ_repetido_continua_respondendo_pelo_erro_dele()
    {
        // SUP-ERR-010 é mais preciso que SUP-ERR-015 quando o documento é o mesmo: a
        // pergunta do comprador ali é sobre o CNPJ, não sobre o nome
        var svc = Build();
        await svc.CreateAsync(Comprador, "Pontes Tour", null, "11.222.333/0001-81", null, "11999998888");

        var (_, erro) = await svc.CreateAsync(Comprador, "Outra Empresa", null, "11222333000181", null, "1133334444");

        Assert.Equal("SUP-ERR-010", erro!.Code);
    }

    [Fact]
    public async Task Nomes_de_empresas_diferentes_continuam_passando()
    {
        // um bloqueio que atrapalha cadastro legítimo acaba contornado por fora: o sufixo
        // societário não é normalizado justamente por isso
        var svc = Build();
        await svc.CreateAsync(Comprador, "Alfa Ltda", null, null, null, "11999998888");

        var (segundo, erro) = await svc.CreateAsync(Comprador, "Alfa ME", null, null, null, "1133334444");

        Assert.Null(erro);
        Assert.NotNull(segundo);
    }

    [Fact]
    public void A_chave_do_nome_e_a_mesma_regra_que_o_navegador_usa()
    {
        // o pré-cadastro da cotação compara com esta chave antes de criar; se as duas
        // divergirem, a tela reaproveita um fornecedor e o servidor recusa outro
        Assert.Equal("PONTESTOUR", SupplierService.ChaveDoNome("  Pontes  Tóur. "));
        Assert.Equal("ACOSDOBRASIL2", SupplierService.ChaveDoNome("Aços do Brasil 2"));
        Assert.Equal("", SupplierService.ChaveDoNome("   "));
    }

    [Fact]
    public async Task Achar_por_razao_social_encontra_o_cadastro_que_a_cotacao_deve_reaproveitar()
    {
        var svc = Build();
        var (existente, _) = await svc.CreateAsync(Comprador, "Pontes Tour", null, null, null, "11999998888");

        var achado = await svc.PorRazaoSocialAsync("PONTES  TOUR");

        Assert.Equal(existente!.Id, achado!.Id);
        Assert.Null(await svc.PorRazaoSocialAsync("Outra Empresa"));
        Assert.Null(await svc.PorRazaoSocialAsync("   "));
    }
}
