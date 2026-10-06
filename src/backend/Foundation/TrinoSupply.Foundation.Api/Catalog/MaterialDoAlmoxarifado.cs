using System.Linq.Expressions;

namespace TrinoSupply.Foundation.Api.Catalog;

/// <summary>
/// Quem aparece na tela <b>Solicitar Material</b>.
///
/// <para>
/// A regra é uma frase: <b>a família manda, o produto ajusta</b> (decisão da empresa, 2026-10).
/// A família diz se o grupo é de almoxarifado; o produto só se manifesta quando é exceção —
/// o papel A4 que não sai do estoque dentro de MATERIAL DE ESCRITÓRIO, ou o item de
/// almoxarifado solto numa família que no geral é de compra.
/// </para>
///
/// <para>
/// Por que não bastava o flag do produto: ele já existia (<c>StockControlled</c>), estava
/// <c>true</c> em toda linha do banco e <b>nenhuma rota o consultava</b> — era por isso que a
/// tela mostrava o catálogo inteiro. Dar a decisão à família é o que permite marcar quatro
/// grupos em vez de oitocentos produtos.
/// </para>
///
/// <para>
/// <b>Sem nada dito, entra.</b> Produto com família fora do cadastro cai no padrão, como a SC
/// sem tipo cai no prazo padrão: é o que todo registro anterior à regra significa, e recusar
/// em silêncio o que sempre apareceu seria pior que mostrar demais.
/// </para>
/// </summary>
public static class MaterialDoAlmoxarifado
{
    /// <param name="doProduto">O ajuste do produto; nulo é "segue a família".</param>
    /// <param name="daFamilia">A marca da família; nulo é família fora do cadastro.</param>
    public static bool Entra(bool? doProduto, bool? daFamilia) => doProduto ?? daFamilia ?? true;

    /// <summary>
    /// A <b>mesma</b> regra, dita como filtro de consulta. O banco não tem o flag da família ao
    /// lado do produto — a família é texto no item —, então o que vai para o SQL é a lista curta
    /// das que <b>não</b> entram (o cadastro inteiro cabe em trezentas linhas), e o resto segue
    /// de <see cref="Entra"/>: ajuste do produto vence, nulo cai na família, família fora da
    /// lista entra.
    ///
    /// <para>
    /// `Contains` sobre <b>array</b> e filtro na <b>entidade</b>, que é o que o Npgsql traduz.
    /// As duas formas da regra moram neste arquivo de propósito, e há teste que as compara item
    /// a item: separadas, a tela mostraria um conjunto e a conta diria outro.
    /// </para>
    /// </summary>
    public static Expression<Func<CatalogItem, bool>> Filtro(string[] familiasQueNaoEntram) =>
        item => item.MaterialRequestable == true
                || (item.MaterialRequestable == null && !familiasQueNaoEntram.Contains(item.Family));

    // ---- o ajuste do produto, na porta da API -------------------------------
    //
    // Três estados viajam como texto, e não como booleano anulável, porque na rota de edição
    // "não mandei o campo" e "mandei vazio" seriam o mesmo `null` — e a diferença entre não
    // mexer e voltar a seguir a família é justamente o que o cadastro precisa dizer.

    public const string SegueAFamilia = "FAMILIA";
    public const string SempreEntra = "SEMPRE";
    public const string NuncaEntra = "NUNCA";

    /// <summary>O texto da API vira o ajuste gravado. Texto desconhecido é "segue a família".</summary>
    public static bool? Ajuste(string? texto) => texto?.Trim().ToUpperInvariant() switch
    {
        SempreEntra => true,
        NuncaEntra => false,
        _ => null,
    };

    /// <summary>O ajuste gravado vira o texto que a tela mostra no seletor.</summary>
    public static string Texto(bool? ajuste) => ajuste switch
    {
        true => SempreEntra,
        false => NuncaEntra,
        _ => SegueAFamilia,
    };
}
