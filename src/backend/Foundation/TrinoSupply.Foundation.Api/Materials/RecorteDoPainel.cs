using Microsoft.EntityFrameworkCore;

namespace TrinoSupply.Foundation.Api.Materials;

/// <summary>
/// O recorte do Painel de Atendimentos: centro de custo, solicitante e a janela de criação.
///
/// <para>
/// O painel abria sempre as <b>últimas 500</b> solicitações e não tinha filtro nenhum — num
/// almoxarifado com movimento, "Em andamento: 37" era um número que ninguém conseguia recortar
/// para cobrar de alguém.
/// </para>
///
/// <para>
/// Três decisões moram aqui. O recorte vai ao <b>servidor</b>, e não fica no navegador: cada
/// cartão do topo promete a lista que abre, e filtrar só as tabelas deixaria o número dizendo
/// uma coisa e a tabela outra — é a mesma regra que já valia na Torre. Ele entra <b>antes do
/// teto de 500</b>: filtrar depois faria o filtro valer só dentro das últimas quinhentas, e a
/// solicitação antiga daquele centro sumiria sem ninguém entender por quê. E o solicitante
/// recorta <b>pelo id</b>, nunca pelo nome, porque dois "João Silva" são duas pessoas — é o
/// mesmo motivo pelo qual o responsável da ação é chave estrangeira.
/// </para>
///
/// <para>
/// A função fica fora da rota para ter teste: filtro é exatamente o tipo de código em que um
/// <c>&gt;=</c> no lugar de <c>&gt;</c> passa despercebido, e o painel passaria a esconder o dia
/// que o usuário pediu.
/// </para>
/// </summary>
public static class RecorteDoPainel
{
    /// <summary>Quantas solicitações o painel lê de uma vez, já recortadas.</summary>
    public const int Teto = 500;

    public static IQueryable<MaterialRequisition> Filtrar(
        IQueryable<MaterialRequisition> consulta,
        string? costCenter, Guid? requesterId, DateOnly? from, DateOnly? to)
    {
        if (!string.IsNullOrWhiteSpace(costCenter))
        {
            // o valor vem do próprio painel (a quebra por centro), mas a rota aceita o que o
            // navegador manda: comparar sem caixa evita o recorte vazio por causa de um "bah-001"
            var centro = costCenter.Trim().ToUpperInvariant();
            consulta = consulta.Where(r => r.CostCenter.ToUpper() == centro);
        }
        if (requesterId is { } quem) consulta = consulta.Where(r => r.RequesterId == quem);
        if (from is { } de) consulta = consulta.Where(r => r.CreatedAt >= MeiaNoite(de));
        // `to` é o dia inteiro: comparar com a meia-noite dele deixaria de fora tudo o que
        // entrou na tarde, e o usuário veria o painel esconder justamente o dia que pediu
        if (to is { } ate) consulta = consulta.Where(r => r.CreatedAt < MeiaNoite(ate.AddDays(1)));
        return consulta;
    }

    private static DateTimeOffset MeiaNoite(DateOnly dia) =>
        new(dia.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>O que o filtro oferece para escolher: centros e solicitantes.</summary>
    /// <param name="Centros">Códigos de centro de custo que aparecem em alguma solicitação.</param>
    /// <param name="Solicitantes">Quem já pediu material, pelo id — dois homônimos são duas linhas.</param>
    public record Opcoes(string[] Centros, Solicitante[] Solicitantes);

    public record Solicitante(Guid Id, string Label);

    /// <summary>
    /// As opções do filtro saem do <b>cadastro inteiro</b>, e não do recorte.
    ///
    /// <para>
    /// A quebra por centro e por solicitante conta o recorte — é o que faz o número bater com a
    /// lista. A lista de <b>escolhas</b> responde outra pergunta: "para onde eu posso ir agora".
    /// Tirá-la do recorte transformaria o filtro numa porta de mão única — quem filtrasse o
    /// BAH-001 veria o seletor passar a oferecer só o BAH-001, e trocar de centro exigiria
    /// limpar tudo e começar de novo.
    /// </para>
    /// </summary>
    public static async Task<Opcoes> OpcoesAsync(
        IQueryable<MaterialRequisition> todas, CancellationToken ct = default)
    {
        var centros = await todas.Select(r => r.CostCenter).Distinct().ToListAsync(ct);
        var pessoas = await todas.Select(r => new { r.RequesterId, r.RequesterLabel }).Distinct().ToListAsync(ct);
        return new Opcoes(
            [.. centros.Where(c => !string.IsNullOrWhiteSpace(c)).OrderBy(c => c, StringComparer.Ordinal)],
            // o mesmo id com dois rótulos (a pessoa trocou de nome) entra uma vez, pelo último
            [.. pessoas.GroupBy(x => x.RequesterId)
                .Select(g => new Solicitante(g.Key, g.Last().RequesterLabel))
                .OrderBy(s => s.Label, StringComparer.CurrentCulture)]);
    }
}
