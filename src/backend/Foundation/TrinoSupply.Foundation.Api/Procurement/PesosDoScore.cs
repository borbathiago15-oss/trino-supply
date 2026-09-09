using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// Os pesos do score multicritério, como a empresa os definiu.
///
/// <para>
/// É uma linha só, e de propósito: a régua com que se compara proposta é da empresa, não do
/// processo. Peso por família ou por categoria é outra conversa — e enquanto ninguém precisou
/// dela, uma tabela com uma linha diz a verdade melhor do que um cadastro que ninguém preenche.
/// </para>
///
/// <para>
/// Antes disso os pesos viviam fixos no código: mudar a prioridade da empresa — passar a
/// valorizar entrega acima de preço, por exemplo — exigia alterar C# e implantar. A régua era
/// da compra e estava trancada com o programador.
/// </para>
/// </summary>
public class ScoreWeights
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Peso do preço, em pontos percentuais.</summary>
    public int Price { get; set; } = 40;
    public int Delivery { get; set; } = 20;
    public int Payment { get; set; } = 10;
    public int Otif { get; set; } = 20;
    public int Risk { get; set; } = 10;

    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedByLabel { get; set; } = string.Empty;

    /// <summary>O que vale enquanto ninguém definiu nada — a régua que o código já usava.</summary>
    public static ScoreWeights Padrao() => new();

    /// <summary>Os pesos na forma que o cálculo e a tela consomem.</summary>
    public IReadOnlyList<CriterioDoScore> Criterios() =>
        MultiCriteriaScore.Padrao
            .Select(c => c with { Weight = PesoDe(c.Code) / 100.0 })
            .ToList();

    public int PesoDe(string code) => code switch
    {
        "price" => Price, "delivery" => Delivery, "payment" => Payment,
        "otif" => Otif, "risk" => Risk,
        _ => 0,
    };
}

/// <summary>Os pesos que o administrador enviou, em pontos percentuais.</summary>
public record PesosInput(int Price, int Delivery, int Payment, int Otif, int Risk);

/// <summary>
/// Lê e grava a régua do score.
///
/// <para>
/// Duas regras moram aqui, e nenhuma é burocracia:
/// </para>
/// <list type="bullet">
/// <item><b>Os pesos somam 100</b> (<c>SCR-ERR-010</c>). A tela publica "Preço 40% ·
/// Entrega 20% · …" como se fosse a repartição de um todo; peso que não fecha faria a
/// explicação mentir sobre a própria conta — e o administrador acharia estar dando 40% ao
/// preço quando estivesse dando 25%.</item>
/// <item><b>Peso zero desliga o critério</b>, e é o jeito honesto de dizer "aqui isto não
/// importa" — bem melhor do que deixá-lo na conta valendo quase nada. Zerar <i>tudo</i> a
/// primeira regra já impede: soma zero não é 100.</item>
/// </list>
/// </summary>
public class ScoreWeightsService(AppDbContext db, TimeProvider clock)
{
    /// <summary>Quem mexe na régua com que a empresa compara proposta.</summary>
    public static bool CanEdit(string role) => role == Roles.SystemAdministrator;

    /// <summary>
    /// A régua vigente. Sem linha gravada devolve o padrão — nunca nulo: uma tela que
    /// precisa perguntar "e se ninguém configurou?" acaba inventando peso próprio.
    /// </summary>
    public async Task<ScoreWeights> AtuaisAsync(CancellationToken ct = default) =>
        await db.ScoreWeights.AsNoTracking().FirstOrDefaultAsync(ct) ?? ScoreWeights.Padrao();

    public async Task<(ScoreWeights? pesos, UserError? error)> SalvarAsync(
        Actor actor, PesosInput pedido, CancellationToken ct = default)
    {
        if (!CanEdit(actor.Role))
            return (null, new("SCR-ERR-900", "Seu papel não altera os pesos do score."));

        int[] valores = [pedido.Price, pedido.Delivery, pedido.Payment, pedido.Otif, pedido.Risk];
        if (valores.Any(v => v < 0 || v > 100))
            return (null, new("SCR-ERR-012", "Cada peso vai de 0 a 100."));

        var soma = valores.Sum();
        if (soma != 100)
            return (null, new("SCR-ERR-010",
                $"Os pesos precisam somar 100 — os enviados somam {soma}. "
                + "Sem isso, o percentual que a tela mostra não é o percentual que a conta usa."));

        var atual = await db.ScoreWeights.FirstOrDefaultAsync(ct);
        if (atual is null)
        {
            atual = ScoreWeights.Padrao();
            db.ScoreWeights.Add(atual);
        }

        atual.Price = pedido.Price;
        atual.Delivery = pedido.Delivery;
        atual.Payment = pedido.Payment;
        atual.Otif = pedido.Otif;
        atual.Risk = pedido.Risk;
        atual.UpdatedAt = clock.GetUtcNow();
        atual.UpdatedByLabel = actor.Label;

        await db.SaveChangesAsync(ct);
        return (atual, null);
    }
}
