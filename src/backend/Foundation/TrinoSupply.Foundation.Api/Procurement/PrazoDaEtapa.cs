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
    /// De que tipo de solicitação é este prazo. <b>Nulo é o padrão</b> — o prazo que vale para
    /// quem não tem tipo, e para o tipo que não definiu o seu naquela etapa.
    ///
    /// <para>
    /// O fallback é <b>por etapa</b>, e não pelo conjunto inteiro: um tipo emergencial que só
    /// precisa apertar a aprovação define aquela etapa e herda o resto. Copiar o conjunto todo
    /// obrigaria a manter cinco números onde um mudou, e os quatro iguais envelheceriam
    /// parados quando o padrão mudasse.
    /// </para>
    /// </summary>
    public string? RequestType { get; set; }

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
    /// Os prazos vigentes de um tipo, com o padrão preenchendo o que ele não definiu. Etapas
    /// fora do fluxo (<c>ENCERRADO</c>) não entram: não há prazo para o que já terminou.
    /// </summary>
    /// <param name="tipo">O tipo de solicitação, ou nulo para o conjunto padrão.</param>
    public async Task<IReadOnlyList<StageSla>> AtuaisAsync(
        string? tipo = null, CancellationToken ct = default)
    {
        var alvo = TipoDeSolicitacaoService.Normalizar(tipo);
        var gravados = await db.StageSlas.AsNoTracking().ToListAsync(ct);
        var doPadrao = gravados.Where(s => s.RequestType is null).ToDictionary(s => s.Stage);
        var doTipo = alvo.Length == 0
            ? []
            : gravados.Where(s => s.RequestType == alvo).ToDictionary(s => s.Stage);

        return Padrao.Select(p =>
            doTipo.TryGetValue(p.Key, out var t) ? t
            : doPadrao.TryGetValue(p.Key, out var d)
                // herdado do padrão: a linha é do padrão, mas responde pelo tipo pedido
                ? new StageSla { Stage = p.Key, MaxDays = d.MaxDays, RequestType = alvo.Length == 0 ? null : alvo,
                    UpdatedAt = d.UpdatedAt, UpdatedByLabel = d.UpdatedByLabel }
            : new StageSla { Stage = p.Key, MaxDays = p.Value, RequestType = alvo.Length == 0 ? null : alvo })
            .ToList();
    }

    /// <summary>Quais etapas este tipo definiu por conta própria (o resto é herdado).</summary>
    public async Task<IReadOnlyList<string>> PropriosAsync(string tipo, CancellationToken ct = default)
    {
        var alvo = TipoDeSolicitacaoService.Normalizar(tipo);
        return alvo.Length == 0 ? []
            : await db.StageSlas.AsNoTracking().Where(s => s.RequestType == alvo)
                .Select(s => s.Stage).ToListAsync(ct);
    }

    /// <summary>Os prazos de um tipo, na forma que a Torre consulta por etapa.</summary>
    public async Task<IReadOnlyDictionary<string, int>> MapaAsync(
        string? tipo = null, CancellationToken ct = default) =>
        (await AtuaisAsync(tipo, ct)).ToDictionary(s => s.Stage, s => s.MaxDays);

    /// <summary>
    /// Os prazos de <b>todos</b> os tipos de uma vez, para a Torre não consultar por linha.
    /// A chave vazia é o padrão.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>> MapaPorTipoAsync(
        CancellationToken ct = default)
    {
        var gravados = await db.StageSlas.AsNoTracking().ToListAsync(ct);
        var doPadrao = gravados.Where(s => s.RequestType is null).ToDictionary(s => s.Stage, s => s.MaxDays);
        IReadOnlyDictionary<string, int> Resolver(IEnumerable<StageSla> linhas)
        {
            var proprios = linhas.ToDictionary(s => s.Stage, s => s.MaxDays);
            return Padrao.ToDictionary(p => p.Key,
                p => proprios.TryGetValue(p.Key, out var t) ? t
                    : doPadrao.TryGetValue(p.Key, out var d) ? d : p.Value);
        }

        var saida = new Dictionary<string, IReadOnlyDictionary<string, int>> { [""] = Resolver([]) };
        foreach (var grupo in gravados.Where(s => s.RequestType is not null).GroupBy(s => s.RequestType!))
            saida[grupo.Key] = Resolver(grupo);
        return saida;
    }

    /// <param name="tipo">
    /// O tipo cujos prazos se está gravando, ou nulo para o conjunto padrão.
    /// </param>
    /// <param name="herdar">
    /// Etapas em que este tipo volta a seguir o padrão. É como se desfaz uma exceção sem
    /// precisar adivinhar o número do padrão e digitá-lo de novo — que congelaria a cópia.
    /// </param>
    public async Task<(IReadOnlyList<StageSla>? prazos, UserError? error)> SalvarAsync(
        Actor actor, IReadOnlyDictionary<string, int> pedido, string? tipo = null,
        IReadOnlyCollection<string>? herdar = null, CancellationToken ct = default)
    {
        if (!CanEdit(actor.Role))
            return (null, new("SLA-ERR-900", "Seu papel não define os prazos das etapas."));

        var alvo = TipoDeSolicitacaoService.Normalizar(tipo);
        var chave = alvo.Length == 0 ? null : alvo;
        if (chave is not null && !await db.RequestTypes.AnyAsync(t => t.Code == chave, ct))
            return (null, new("SLA-ERR-012", $"Tipo de solicitação desconhecido: {chave}."));

        var etapas = pedido.Keys.Concat(herdar ?? []).ToList();
        var desconhecida = etapas.FirstOrDefault(k => !Padrao.ContainsKey(k));
        if (desconhecida is not null)
            return (null, new("SLA-ERR-011", $"Etapa desconhecida: {desconhecida}."));
        if (pedido.Values.Any(v => v < 0 || v > 365))
            return (null, new("SLA-ERR-010", "O prazo de uma etapa vai de 0 a 365 dias (0 desliga)."));

        var gravados = await db.StageSlas.Where(s => s.RequestType == chave).ToListAsync(ct);
        foreach (var (etapa, dias) in pedido)
        {
            var linha = gravados.SingleOrDefault(s => s.Stage == etapa);
            if (linha is null)
            {
                linha = new StageSla { Stage = etapa, RequestType = chave };
                db.StageSlas.Add(linha);
            }
            linha.MaxDays = dias;
            linha.UpdatedAt = clock.GetUtcNow();
            linha.UpdatedByLabel = actor.Label;
        }

        // voltar a herdar é apagar a exceção, e não copiar o número do padrão para cá:
        // a cópia pararia no tempo e o tipo deixaria de acompanhar a mudança do padrão
        if (chave is not null && herdar is { Count: > 0 })
            db.StageSlas.RemoveRange(gravados.Where(s => herdar.Contains(s.Stage)));

        await db.SaveChangesAsync(ct);
        return (await AtuaisAsync(tipo, ct), null);
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
