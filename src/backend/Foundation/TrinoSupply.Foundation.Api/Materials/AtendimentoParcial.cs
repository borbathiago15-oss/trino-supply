namespace TrinoSupply.Foundation.Api.Materials;

/// <summary>
/// O que quem atende decide quando não entrega tudo.
///
/// <para>
/// São <b>duas perguntas independentes</b> (decisão da empresa, 2026-10), e era a falta delas que
/// fazia o almoxarifado perder o caso mais comum: entregou 3 das 10 botas porque o resto chega na
/// quinta. Antes, qualquer atendimento <b>encerrava</b> a solicitação e <b>comprava</b> o faltante —
/// então ou se esperava a carga sem registrar o que já saiu, ou se comprava o que já estava a
/// caminho.
/// </para>
///
/// <list type="bullet">
///   <item><see cref="Concluir"/> — encerra o atendimento mesmo parcial, ou mantém a solicitação
///     na fila do estoque esperando o resto.</item>
///   <item><see cref="GerarCompra"/> — manda o que faltou para compra, ou não.</item>
/// </list>
///
/// <para>
/// As duas se combinam de quatro jeitos, e os quatro querem dizer coisa diferente: concluir e
/// comprar é o caminho de antes; concluir sem comprar é "o que faltou não vai ser comprado"; ficar
/// pendente sem comprar é "o resto chega e eu entrego"; e ficar pendente comprando é "entrego o
/// que chegar, e já pedi reposição". Amarrar uma à outra tiraria justamente a escolha que o
/// almoxarife tem na mão.
/// </para>
/// </summary>
/// <param name="Concluir">Encerra o atendimento; <c>false</c> deixa a solicitação na fila.</param>
/// <param name="GerarCompra">Abre a SC do que faltou no nome de quem pediu.</param>
public record DecisaoDoAtendimento(bool Concluir, bool GerarCompra)
{
    /// <summary>
    /// O que toda chamada anterior à regra significa: encerrar e comprar o faltante. É o padrão
    /// da porta interna e dos testes antigos — mudar o sentido deles em silêncio seria pior que
    /// pedir a decisão.
    /// </summary>
    public static readonly DecisaoDoAtendimento Padrao = new(Concluir: true, GerarCompra: true);
}
