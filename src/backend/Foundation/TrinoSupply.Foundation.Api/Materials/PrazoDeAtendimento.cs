using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Materials;

/// <summary>
/// Quanto tempo o almoxarifado tem para atender, por <b>família de produto</b>.
///
/// <para>
/// É cadastro próprio, em menu próprio (decisão da empresa, 2026-10). A fila do almoxarifado já
/// dizia há quantos dias cada solicitação espera; o que faltava era alguém dizer que aquilo é
/// tempo demais — e o tempo demais <b>depende do que se pediu</b>: luva da prateleira não tem o
/// mesmo prazo de uma peça que o almoxarife busca em outro galpão.
/// </para>
///
/// <para>
/// <b>Não é o lead time da família</b> (<c>ProductFamily.LeadRequestToQuote</c> e os outros
/// três). Aqueles são as metas do processo de <b>compra</b> — solicitação → cotação → aprovação
/// → O.C. → entrega — e o Dashboard os compara com o realizado. Este é o outro cano da casa: o
/// material que já está em estoque e sai pela porta do almoxarifado, sem compra nenhuma. Juntar
/// os dois num campo só faria o painel de compras cobrar o almoxarife, e o contrário.
/// </para>
///
/// <para>
/// A forma é a mesma do <see cref="StageSla"/>, de propósito: <b>família nula é o padrão</b>, a
/// família que não definiu o seu <b>herda</b>, voltar a herdar <b>apaga a exceção</b> em vez de
/// copiar o número (a cópia pararia no tempo), e <b>zero desliga</b> a cobrança. É a régua que o
/// administrador já conhece de Prazos por Etapa — duas réguas parecidas com comportamentos
/// diferentes seria pior que não ter a segunda.
/// </para>
/// </summary>
public class MaterialFulfillmentSla
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// A família do catálogo a que este prazo pertence. <b>Nulo é o padrão</b> — o prazo que
    /// vale para a família que não definiu o seu, e para o item cuja família saiu do cadastro.
    /// </summary>
    public string? Family { get; set; }

    /// <summary>
    /// Dias corridos que o atendimento pode levar, contados da liberação do Nível 1.
    /// <b>Zero desliga</b> a cobrança daquela família — é o modo honesto de dizer "aqui não
    /// cobramos tempo", em vez de deixar um número que ninguém respeita.
    /// </summary>
    public int MaxDays { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedByLabel { get; set; } = string.Empty;
}

/// <summary>
/// Como um atendimento está contra o prazo da sua família.
/// </summary>
/// <param name="Family">
/// Qual família impôs o prazo. Vai junto porque a solicitação tem itens de várias, e sem isso a
/// tela diria "2 dias" sem dizer de onde saiu o 2 — e o almoxarife não teria como conferir.
/// </param>
public record SituacaoDoAtendimento(int? MaxDays, int? Days, string? Status, string? Family)
{
    public static readonly SituacaoDoAtendimento Nenhuma = new(null, null, null, null);
    public bool Breached => Status == "ESTOURADO";
}

/// <summary>
/// Lê e grava os prazos de atendimento por família, e julga uma espera contra eles.
///
/// <para>
/// <b>Mede e expõe, nunca bloqueia</b> — a mesma decisão do prazo da etapa e do Compliance
/// Score. Prazo estourado aparece na linha e entra no filtro, mas não impede atender, aprovar
/// nem cancelar: travar o atendimento pelo relógio deixaria o material na prateleira e quem
/// pediu sem ele, que é o oposto do que o prazo existe para resolver.
/// </para>
/// </summary>
public class PrazoDeAtendimentoService(AppDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Quem define o prazo de atendimento. Como os pesos do score, as metas e os prazos por
    /// etapa: a régua vale para a operação inteira, e mudá-la muda o veredito de toda a fila
    /// de uma vez.
    /// </summary>
    public static bool CanEdit(string role) => role == Roles.SystemAdministrator;

    /// <summary>
    /// O prazo de fábrica, em dias. <b>Dois</b> não é número escolhido no ar: é o mesmo das 48h
    /// que o cockpit já trata como atenção nesta mesma fila, e começar com outro faria a parede
    /// e o cadastro cobrarem coisas diferentes antes de alguém configurar nada. É ponto de
    /// partida com dono declarado, como <c>PrazoDaEtapaService.Padrao</c> — a empresa troca o seu.
    /// </summary>
    public const int PadraoDeDias = 2;

    /// <summary>
    /// A fração do prazo em que a linha entra em atenção. É a <b>mesma</b> do prazo da etapa:
    /// dois critérios de "está chegando no limite" obrigariam quem lê a lembrar qual vale para
    /// qual tela.
    /// </summary>
    public static decimal FracaoDeAtencao => PrazoDaEtapaService.FracaoDeAtencao;

    /// <summary>
    /// Uma linha por família de almoxarifado, mais o <b>padrão</b> na frente.
    ///
    /// <para>
    /// As famílias são as que <b>entram em Solicitar Material</b> (<c>MaterialRequestable</c>):
    /// prazo de atendimento de uma família que nunca chega ao almoxarifado é linha que o
    /// administrador precisa ler para descobrir que não serve. O produto marcado "sempre entra"
    /// numa família não marcada cai no padrão — é o que o padrão existe para cobrir.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<MaterialFulfillmentSla>> AtuaisAsync(CancellationToken ct = default)
    {
        var gravados = await db.MaterialFulfillmentSlas.AsNoTracking().ToListAsync(ct);
        var familias = await db.ProductFamilies.AsNoTracking()
            .Where(f => f.MaterialRequestable).OrderBy(f => f.Name).Select(f => f.Name).ToListAsync(ct);

        var padrao = gravados.SingleOrDefault(s => s.Family is null)
            ?? new MaterialFulfillmentSla { Family = null, MaxDays = PadraoDeDias };
        var porFamilia = gravados.Where(s => s.Family is not null).ToDictionary(s => s.Family!);

        return [padrao, .. familias.Select(nome =>
            porFamilia.TryGetValue(nome, out var propria) ? propria
                // herdada: a linha é do padrão, mas responde pela família pedida
                : new MaterialFulfillmentSla { Family = nome, MaxDays = padrao.MaxDays })];
    }

    /// <summary>Quais famílias definiram o prazo por conta própria (o resto é herdado).</summary>
    public async Task<IReadOnlyList<string>> PropriasAsync(CancellationToken ct = default) =>
        await db.MaterialFulfillmentSlas.AsNoTracking()
            .Where(s => s.Family != null).Select(s => s.Family!).ToListAsync(ct);

    /// <summary>Família → dias, na forma que a fila consulta. A chave vazia é o padrão.</summary>
    public async Task<IReadOnlyDictionary<string, int>> MapaAsync(CancellationToken ct = default)
    {
        var gravados = await db.MaterialFulfillmentSlas.AsNoTracking().ToListAsync(ct);
        var mapa = new Dictionary<string, int>
        {
            [""] = gravados.SingleOrDefault(s => s.Family is null)?.MaxDays ?? PadraoDeDias,
        };
        foreach (var linha in gravados.Where(s => s.Family is not null))
            mapa[linha.Family!] = linha.MaxDays;
        return mapa;
    }

    /// <param name="herdar">
    /// Famílias que voltam a seguir o padrão. É como se desfaz uma exceção sem digitar de novo o
    /// número do padrão — que o congelaria, e a família deixaria de acompanhar a mudança.
    /// </param>
    public async Task<(IReadOnlyList<MaterialFulfillmentSla>? prazos, UserError? error)> SalvarAsync(
        Actor actor, IReadOnlyDictionary<string, int> pedido,
        IReadOnlyCollection<string>? herdar = null, CancellationToken ct = default)
    {
        if (!CanEdit(actor.Role))
            return (null, new("MSLA-ERR-900", "Seu papel não define o prazo de atendimento."));

        // a forma do pedido é conferida antes das referências: "0 a 365" é mais útil que
        // "família desconhecida" quando os dois estão errados
        if (pedido.Values.Any(v => v < 0 || v > 365))
            return (null, new("MSLA-ERR-010", "O prazo de atendimento vai de 0 a 365 dias (0 desliga)."));

        var chaves = pedido.Keys.Concat(herdar ?? [])
            .Select(CatalogService.NormalizarFamilia).Where(k => k.Length > 0).Distinct().ToArray();
        if (chaves.Length > 0)
        {
            var conhecidas = await db.ProductFamilies.Where(f => chaves.Contains(f.Name))
                .Select(f => f.Name).ToListAsync(ct);
            var desconhecida = chaves.FirstOrDefault(k => !conhecidas.Contains(k));
            if (desconhecida is not null)
                return (null, new("MSLA-ERR-011", $"Família desconhecida: {desconhecida}."));
        }

        var gravados = await db.MaterialFulfillmentSlas.ToListAsync(ct);
        foreach (var (bruta, dias) in pedido)
        {
            var familia = CatalogService.NormalizarFamilia(bruta);
            var chave = familia.Length == 0 ? null : familia;
            var linha = gravados.SingleOrDefault(s => s.Family == chave);
            if (linha is null)
            {
                linha = new MaterialFulfillmentSla { Family = chave };
                db.MaterialFulfillmentSlas.Add(linha);
            }
            linha.MaxDays = dias;
            linha.UpdatedAt = clock.GetUtcNow();
            linha.UpdatedByLabel = actor.Label;
        }

        // voltar a herdar apaga a exceção — o padrão nunca se apaga, porque é ele que sobra
        if (herdar is { Count: > 0 })
        {
            var apagar = herdar.Select(CatalogService.NormalizarFamilia).Where(k => k.Length > 0).ToHashSet();
            db.MaterialFulfillmentSlas.RemoveRange(
                gravados.Where(s => s.Family is not null && apagar.Contains(s.Family)));
        }

        await db.SaveChangesAsync(ct);
        return (await AtuaisAsync(ct), null);
    }

    // ---- o prazo de uma solicitação -------------------------------------------------

    /// <summary>
    /// O prazo de uma solicitação é o <b>mais curto</b> entre as famílias dos itens dela.
    ///
    /// <para>
    /// A solicitação tem itens de várias famílias, e o prazo é de uma só coisa. Valer o mais
    /// longo deixaria o item de dois dias parado dez dias dentro de uma solicitação "no prazo" —
    /// a cobrança desapareceria justamente onde ela serve. O mais curto é o primeiro
    /// compromisso que vence, e é dele que o almoxarife precisa saber.
    /// </para>
    ///
    /// <para>
    /// Família com <b>zero fica fora da conta</b>, em vez de ser a mais curta: zero quer dizer
    /// "aqui não cobramos tempo", e tratá-lo como prazo de zero dia faria toda solicitação que
    /// a tocasse nascer estourada. Se <b>todas</b> as famílias estão em zero, não há prazo.
    /// </para>
    /// </summary>
    public static (int Dias, string? Familia)? TetoDaSolicitacao(
        IEnumerable<string?> familiasDosItens, IReadOnlyDictionary<string, int> prazos)
    {
        var padrao = prazos.TryGetValue("", out var p) ? p : PadraoDeDias;
        (int Dias, string? Familia)? menor = null;
        foreach (var bruta in familiasDosItens)
        {
            var familia = (bruta ?? string.Empty).Trim();
            // família fora do cadastro cai no padrão: ela sai do cadastro e a solicitação
            // dela continua existindo, e deixá-la sem prazo seria perder a cobrança
            var dias = familia.Length > 0 && prazos.TryGetValue(familia, out var d) ? d : padrao;
            if (dias <= 0) continue;
            if (menor is null || dias < menor.Value.Dias)
                menor = (dias, familia.Length > 0 ? familia : null);
        }
        return menor;
    }

    /// <summary>
    /// Como este atendimento está contra o prazo.
    ///
    /// <para>
    /// O relógio começa na <b>liberação do Nível 1</b> (<c>ApprovedAt</c>), que é quando a
    /// solicitação entrou na fila do estoque — a mesma régua que a espera da fila já usa.
    /// Contar da criação cobraria do almoxarife o tempo que a solicitação passou esperando a
    /// aprovação do centro, que não é dele.
    /// </para>
    ///
    /// <para>
    /// Solicitação <b>já atendida</b> é medida até o atendimento, e não até agora: ali a
    /// pergunta deixou de ser "está atrasada?" e passou a ser "foi atendida no prazo?". Sem
    /// aprovação não há relógio: a vez é do Nível 1 do centro.
    /// </para>
    /// </summary>
    public static SituacaoDoAtendimento Avaliar(
        DateTimeOffset? aprovadaEm, DateTimeOffset? atendidaEm, DateTimeOffset agora,
        IEnumerable<string?> familiasDosItens, IReadOnlyDictionary<string, int> prazos)
    {
        if (TetoDaSolicitacao(familiasDosItens, prazos) is not { } teto) return SituacaoDoAtendimento.Nenhuma;
        if (aprovadaEm is not { } inicio) return new SituacaoDoAtendimento(teto.Dias, null, null, teto.Familia);

        var fim = atendidaEm ?? agora;
        var dias = (int)Math.Floor((fim - inicio).TotalDays);
        if (dias < 0) dias = 0;
        var status = dias > teto.Dias ? "ESTOURADO"
            : dias >= teto.Dias * FracaoDeAtencao ? "ATENCAO"
            : "OK";
        return new SituacaoDoAtendimento(teto.Dias, dias, status, teto.Familia);
    }

    /// <summary>
    /// A situação do prazo de cada solicitação da lista, numa consulta só.
    ///
    /// <para>
    /// A família não está no item da solicitação — ele guarda código e descrição, não a família —,
    /// então ela vem do catálogo pelo <c>CatalogItemId</c>. É por isso que esta função existe em
    /// vez de a tela resolver linha a linha: uma consulta por solicitação na fila seria uma
    /// consulta por linha da tela.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, SituacaoDoAtendimento>> SituacoesAsync(
        IReadOnlyCollection<MaterialRequisition> solicitacoes, CancellationToken ct = default)
    {
        if (solicitacoes.Count == 0) return new Dictionary<Guid, SituacaoDoAtendimento>();

        var prazos = await MapaAsync(ct);
        var ids = solicitacoes.SelectMany(r => r.Items.Select(i => i.CatalogItemId)).Distinct().ToArray();
        // filtro na entidade, `Contains` com array, projeção por último
        var familiaDoProduto = (await db.CatalogItems.AsNoTracking()
                .Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.Family }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.Family);

        var agora = clock.GetUtcNow();
        return solicitacoes.ToDictionary(r => r.Id, r => Avaliar(
            r.ApprovedAt, r.FulfilledAt, agora,
            r.Items.Select(i => familiaDoProduto.TryGetValue(i.CatalogItemId, out var f) ? f : null),
            prazos));
    }
}
