using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// Quanto tempo cada etapa pode levar, como a empresa definiu.
///
/// <para>
/// A Torre já dizia há quantos dias a linha está parada. O que faltava era alguém dizer que
/// aquilo é tempo demais: seis dias em aprovação e seis dias em recebimento são coisas
/// diferentes, e sem prazo por etapa o comprador comparava o número com a própria intuição.
/// </para>
/// </summary>
public class StageSla
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>A chave da etapa, como a Torre a nomeia (<c>COTACAO</c>, <c>APROVACAO</c>…).</summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>
    /// Dias corridos que a etapa pode levar. <b>Zero desliga</b> o prazo daquela etapa — é o
    /// modo honesto de dizer "aqui não cobramos tempo", em vez de deixar um número que
    /// ninguém respeita e que só ensina o comprador a ignorar a cor.
    /// </summary>
    public int MaxDays { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedByLabel { get; set; } = string.Empty;
}

/// <summary>Como a linha está contra o prazo da sua etapa.</summary>
/// <param name="MaxDays">O prazo da etapa. Nulo quando não há prazo definido.</param>
/// <param name="Status">
/// <c>OK</c>, <c>ATENCAO</c> (chegando no limite) ou <c>ESTOURADO</c>. Nulo quando não há
/// prazo ou não há espera a medir — sem base, um veredito seria invenção.
/// </param>
public record SituacaoDoPrazo(int? MaxDays, int? Days, string? Status)
{
    public static readonly SituacaoDoPrazo Nenhuma = new(null, null, null);
    public bool Breached => Status == "ESTOURADO";
}

/// <summary>
/// Lê e grava os prazos por etapa, e julga uma espera contra eles.
///
/// <para>
/// <b>Mede e expõe, nunca bloqueia</b> — a mesma decisão do Compliance Score. Prazo estourado
/// não impede aprovar, cotar nem fechar: ele aparece, entra no filtro e conta no KPI. Travar
/// o fluxo por causa do relógio empurraria o comprador para fora do sistema, que é o oposto
/// do que a Torre existe para fazer.
/// </para>
/// </summary>
public class PrazoDaEtapaService(AppDbContext db, TimeProvider clock)
{
    /// <summary>Quem define quanto tempo cada etapa pode levar.</summary>
    public static bool CanEdit(string role) => role == Roles.SystemAdministrator;

    /// <summary>
    /// A partir de que fração do prazo a etapa entra em atenção. Oitenta por cento dá ao
    /// comprador tempo de agir antes do estouro — avisar no dia do vencimento seria avisar
    /// tarde demais para mudar alguma coisa.
    /// </summary>
    public const decimal FracaoDeAtencao = 0.8m;

    /// <summary>
    /// Os prazos de fábrica, por etapa. Existem para a tela nascer com números que fazem
    /// sentido e não com zeros, e são ponto de partida — a empresa troca os seus.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> Padrao = new Dictionary<string, int>
    {
        ["SOLICITACAO"] = 2,        // atribuir comprador e abrir a cotação
        ["COTACAO"] = 7,            // convidar, esperar proposta e analisar
        ["APROVACAO"] = 3,          // as duas alçadas
        ["ORDEM_DE_COMPRA"] = 2,    // registrar no ERP o que já foi aprovado
        ["RECEBIMENTO"] = 15,       // faturar e entregar
    };

    /// <summary>
    /// Os prazos vigentes, com o padrão preenchendo o que ninguém configurou. Etapas fora do
    /// fluxo (<c>ENCERRADO</c>) não entram: não há prazo para o que já terminou.
    /// </summary>
    public async Task<IReadOnlyList<StageSla>> AtuaisAsync(CancellationToken ct = default)
    {
        var gravados = await db.StageSlas.AsNoTracking().ToDictionaryAsync(s => s.Stage, ct);
        return Padrao.Select(p => gravados.TryGetValue(p.Key, out var s)
                ? s
                : new StageSla { Stage = p.Key, MaxDays = p.Value })
            .ToList();
    }

    /// <summary>Os prazos na forma que a Torre consulta por etapa.</summary>
    public async Task<IReadOnlyDictionary<string, int>> MapaAsync(CancellationToken ct = default) =>
        (await AtuaisAsync(ct)).ToDictionary(s => s.Stage, s => s.MaxDays);

    public async Task<(IReadOnlyList<StageSla>? prazos, UserError? error)> SalvarAsync(
        Actor actor, IReadOnlyDictionary<string, int> pedido, CancellationToken ct = default)
    {
        if (!CanEdit(actor.Role))
            return (null, new("SLA-ERR-900", "Seu papel não define os prazos das etapas."));

        var desconhecida = pedido.Keys.FirstOrDefault(k => !Padrao.ContainsKey(k));
        if (desconhecida is not null)
            return (null, new("SLA-ERR-011", $"Etapa desconhecida: {desconhecida}."));
        if (pedido.Values.Any(v => v < 0 || v > 365))
            return (null, new("SLA-ERR-010", "O prazo de uma etapa vai de 0 a 365 dias (0 desliga)."));

        var gravados = await db.StageSlas.ToDictionaryAsync(s => s.Stage, ct);
        foreach (var (etapa, dias) in pedido)
        {
            if (!gravados.TryGetValue(etapa, out var linha))
            {
                linha = new StageSla { Stage = etapa };
                db.StageSlas.Add(linha);
            }
            linha.MaxDays = dias;
            linha.UpdatedAt = clock.GetUtcNow();
            linha.UpdatedByLabel = actor.Label;
        }

        await db.SaveChangesAsync(ct);
        return (await AtuaisAsync(ct), null);
    }

    /// <summary>
    /// Como esta espera está contra o prazo da sua etapa.
    ///
    /// <para>
    /// O prazo é <b>da etapa</b>, e o relógio é o da espera — o mesmo número que a linha já
    /// mostra. Medir contra a criação da SC diria que o item está atrasado na aprovação por
    /// causa do tempo que ele passou em cotação, e a cobrança cairia sobre quem não tem culpa.
    /// </para>
    /// </summary>
    public static SituacaoDoPrazo Avaliar(
        string etapa, int? diasParados, IReadOnlyDictionary<string, int> prazos)
    {
        if (!prazos.TryGetValue(etapa, out var teto) || teto <= 0) return SituacaoDoPrazo.Nenhuma;
        if (diasParados is not { } dias) return new SituacaoDoPrazo(teto, null, null);
        var status = dias > teto ? "ESTOURADO"
            : dias >= teto * FracaoDeAtencao ? "ATENCAO"
            : "OK";
        return new SituacaoDoPrazo(teto, dias, status);
    }
}
