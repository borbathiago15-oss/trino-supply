using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// Escolha do fornecedor: a adjudicação por família (cada família é um lote com um
/// vencedor), o mapa que diz quem pode levar cada uma e o rateio que dá o valor de
/// cada fatia. É aqui que mora o RFQ-BR-005.
/// </summary>
public partial class QuotationService
{
    // ---- escolha do fornecedor (RFQ-BR-005) ----------------------------------
    /// <summary>
    /// Vencedor único: adjudica TODAS as famílias da cotação ao mesmo fornecedor.
    /// Continua sendo o caminho da compra que não se divide.
    /// </summary>
    public Task<(Quotation? q, UserError? error)> SelectWinnerAsync(
        Actor actor, Guid id, Guid proposalId, string? criteria, string? justification, CancellationToken ct = default) =>
        AwardAsync(actor, id, [new AwardInput(string.Empty, proposalId, criteria, justification ?? string.Empty)],
            todasAsFamilias: true, ct);

    /// <summary>
    /// Adjudicação por família (multi-fornecedor): a mesma compra vai para vários fornecedores,
    /// um por família, cada um com a sua justificativa. Toda família cotada precisa de vencedor.
    /// </summary>
    public Task<(Quotation? q, UserError? error)> AwardByFamilyAsync(
        Actor actor, Guid id, IReadOnlyList<AwardInput> awards, CancellationToken ct = default) =>
        AwardAsync(actor, id, awards, todasAsFamilias: false, ct);

    /// <summary>
    /// Adjudicação <b>por item</b>: dentro da mesma família, cada item pode ir para um
    /// fornecedor diferente — o papel com um, a caneta com outro. Todo item cotado precisa
    /// de vencedor, e cada um é adjudicado uma vez só.
    /// </summary>
    public Task<(Quotation? q, UserError? error)> AwardByItemAsync(
        Actor actor, Guid id, IReadOnlyList<AwardInput> awards, CancellationToken ct = default) =>
        AwardAsync(actor, id, awards, todasAsFamilias: false, ct);

    private async Task<(Quotation? q, UserError? error)> AwardAsync(
        Actor actor, Guid id, IReadOnlyList<AwardInput> pedidos, bool todasAsFamilias, CancellationToken ct)
    {
        var q = await GetAsync(id, ct);
        if (q is null) return (null, new("RFQ-ERR-404", "Cotação não encontrada."));
        if (q.Status != QuotationStatus.Analysis)
            return (null, new("RFQ-ERR-020", "A escolha do fornecedor acontece na etapa de análise."));
        if (pedidos.Count == 0)
            return (null, new("RFQ-ERR-021", "Informe o fornecedor vencedor de cada família."));

        var familias = q.Families;
        // vencedor único: a mesma escolha vale para todas as famílias do processo
        if (todasAsFamilias)
            pedidos = familias.Select(f => pedidos[0] with { Family = f }).ToList();

        // A conferência é sempre a mesma, e é sobre ITENS: cada item do processo precisa de
        // exatamente um vencedor. Falar em "família adjudicada duas vezes" já não descreve o
        // que pode dar errado quando o grão é o item — dois pedidos podem cobrir o mesmo item
        // por caminhos diferentes (um pela família, outro pelo item) e isso é o mesmo furo.
        var porItem = pedidos.Any(a => a.QuotationItemId is not null);
        foreach (var pedido in pedidos)
        {
            if (pedido.QuotationItemId is { } item && q.Items.All(i => i.Id != item))
                return (null, new("RFQ-ERR-023", "O item escolhido não faz parte desta cotação."));
            if (pedido.QuotationItemId is null
                && QuotationAward.FamilyKey(pedido.Family) is { } f && !familias.Contains(f))
                return (null, new("RFQ-ERR-023", $"A família {f} não faz parte desta cotação."));
        }

        var cobertos = pedidos.SelectMany(a => EscopoDe(q, a)).ToList();
        var repetido = cobertos.GroupBy(i => i).FirstOrDefault(g => g.Count() > 1);
        if (repetido is not null)
            return (null, new("RFQ-ERR-023", porItem
                ? $"O item {DescricaoDoItem(q, repetido.Key)} foi adjudicado mais de uma vez."
                : "Cada família só pode ser adjudicada uma vez."));

        var descobertos = q.Items.Where(i => !cobertos.Contains(i.Id)).ToList();
        if (descobertos.Count > 0)
            return (null, new("RFQ-ERR-023", porItem
                ? $"Falta escolher o fornecedor de: {string.Join(", ", descobertos.Select(i => i.Description))} — todo item cotado precisa de um vencedor."
                : $"Falta escolher o fornecedor da(s) família(s) {string.Join(", ", familias.Where(f => !pedidos.Any(a => QuotationAward.FamilyKey(a.Family) == f)))} — toda família cotada precisa de um vencedor."));

        var now = clock.GetUtcNow();
        var hoje = DateOnly.FromDateTime(now.UtcDateTime);
        var idsFornecedores = q.Proposals.Where(p => pedidos.Any(x => x.ProposalId == p.Id))
            .Select(p => p.SupplierId).Distinct().ToList();
        var fornecedores = await db.Suppliers.Include(f => f.Documents)
            .Where(f => idsFornecedores.Contains(f.Id)).ToListAsync(ct);

        // valida tudo antes de gravar: escolha parcial não pode deixar o processo pela metade
        var novas = new List<QuotationAward>();
        foreach (var pedido in pedidos)
        {
            // no pedido por item a família vem do próprio item: exigir que a tela a repita
            // seria pedir um dado que o servidor já tem, e que ela poderia errar
            var familia = pedido.QuotationItemId is { } alvo
                ? QuotationAward.FamilyKey(q.Items.Single(i => i.Id == alvo).Family)
                : QuotationAward.FamilyKey(pedido.Family);
            if (string.IsNullOrWhiteSpace(pedido.Justification))
                return (null, new("RFQ-ERR-021", familias.Count > 1
                    ? $"A justificativa da escolha é obrigatória (família {familia})."
                    : "A justificativa da escolha é obrigatória."));
            var proposal = q.Proposals.SingleOrDefault(p => p.Id == pedido.ProposalId);
            if (proposal is null) return (null, new("RFQ-ERR-021", "Proposta vencedora inexistente nesta cotação."));
            var latest = q.Proposals.Where(p => p.SupplierId == proposal.SupplierId)
                .OrderByDescending(p => p.VersionNumber).First();
            if (latest.Id != proposal.Id)
                return (null, new("RFQ-ERR-021", "Selecione a versão mais recente da proposta do fornecedor."));

            var itens = EscopoDe(q, pedido);
            if (itens.Any(i => proposal.Items.All(pi => pi.QuotationItemId != i)))
                return (null, new("RFQ-ERR-024", pedido.QuotationItemId is { } naoCotado
                    ? $"{proposal.SupplierName} não cotou {DescricaoDoItem(q, naoCotado)}."
                    : $"{proposal.SupplierName} não cotou todos os itens da família {familia} — escolha um fornecedor que tenha cotado a família inteira."));

            // homologação (V2-P2): prospect participa da cotação, mas só homologado é selecionado
            var vencedor = fornecedores.SingleOrDefault(f => f.Id == proposal.SupplierId)
                ?? await db.Suppliers.Include(f => f.Documents).SingleAsync(f => f.Id == proposal.SupplierId, ct);
            var situacao = vencedor.EffectiveHomologation(hoje);
            if (situacao != SupplierHomologation.Homologado)
                // o prospect é o caso do pré-cadastro feito para cotar: ele ganhou o BID e
                // agora é a hora de completar a ficha. Dizer só "está PROSPECT" deixaria o
                // comprador adivinhando qual é o próximo passo
                return (null, new("SUP-ERR-030", situacao == SupplierHomologation.Prospect
                    ? $"{proposal.SupplierName} venceu, mas ainda é um pré-cadastro (PROSPECT). "
                      + "Complete o cadastro em Cadastros → Fornecedores e peça a homologação ao gestor de suprimentos para seguir."
                    : $"O fornecedor {proposal.SupplierName} está {situacao} — conclua a homologação (ou regularize as certidões) antes de selecioná-lo."));

            var (valorItens, total) = ShareOf(proposal, itens);
            novas.Add(new QuotationAward
            {
                QuotationId = q.Id, Family = familia, QuotationItemId = pedido.QuotationItemId,
                SupplierId = proposal.SupplierId, SupplierName = proposal.SupplierName,
                ProposalId = proposal.Id, ProposalVersion = proposal.VersionNumber,
                ItemsValue = valorItens, TotalValue = total,
                Criteria = string.IsNullOrWhiteSpace(pedido.Criteria) ? null : pedido.Criteria.Trim(),
                Justification = pedido.Justification.Trim(),
                SelectedBy = actor.Id, SelectedByLabel = actor.Label, SelectedAt = now,
            });
        }

        // reescolha depois de "solicitar ajustes": a adjudicação anterior é substituída inteira
        foreach (var antiga in q.AwardList) db.QuotationAwards.Remove(antiga);
        q.Awards.Clear();
        foreach (var nova in novas) { db.QuotationAwards.Add(nova); q.Awards.Add(nova); }

        // cabeçalho: continua apontando o fornecedor de maior fatia (compatibilidade das telas e alçadas)
        var principal = novas.OrderByDescending(a => a.TotalValue).First();
        var distintos = novas.Select(a => a.SupplierId).Distinct().Count();
        var totalGeral = novas.Sum(a => a.TotalValue);
        q.WinnerSupplierId = principal.SupplierId;
        q.WinnerProposalId = principal.ProposalId;
        var criterios = novas.Select(a => a.Criteria).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        q.SelectionCriteria = criterios.Count == 0 ? null : string.Join(", ", criterios);
        q.SelectionJustification = distintos == 1 && novas.Select(a => a.Justification).Distinct().Count() == 1
            ? principal.Justification
            : string.Join(" | ", novas.Select(a => $"{a.Family}: {a.Justification}"));
        q.SelectedBy = actor.Id;
        q.SelectedByLabel = actor.Label;
        q.SelectedAt = now;
        // o orçamento vem das SCs que este processo atende — `SourcePrIds` já cobre o
        // agrupamento multi-SC, então processo de três SCs compara com os três orçamentos
        var orcamentos = await db.Requisitions.Where(r => q.SourcePrIds.Contains(r.Id))
            .Select(r => r.Budget).ToListAsync(ct);
        ApplyAwardSaving(q, novas, orcamentos);

        var from = q.Status;
        q.Status = QuotationStatus.AwaitingManager;
        AddEvent(q, distintos == 1 ? "FORNECEDOR_SELECIONADO" : "COMPRA_DIVIDIDA",
            distintos == 1
                ? $"Fornecedor {principal.SupplierName} selecionado (proposta v{principal.ProposalVersion}, total {totalGeral:0.00}). Processo encaminhado à aprovação gerencial."
                : $"Compra dividida entre {distintos} fornecedores por família — " +
                  string.Join("; ", novas.Select(a => $"{a.Family} → {a.SupplierName} ({a.TotalValue:0.00})")) +
                  $". Total {totalGeral:0.00}. Processo encaminhado à aprovação gerencial.",
            actor, from, q.Status, q.SelectionJustification);
        await TouchAndSaveAsync(q, ct);
        return (q, null);
    }

    /// <summary>
    /// Mapa de adjudicação: para cada família, quanto sai com cada fornecedor. Só quem cotou a
    /// família inteira pode levá-la; quem cotou parte aparece marcado como incompleto.
    /// </summary>
    public IReadOnlyList<FamilyLot> FamilyMap(Quotation q)
    {
        var atuais = q.Proposals.GroupBy(p => p.SupplierId)
            .Select(g => g.OrderByDescending(p => p.VersionNumber).First()).ToList();
        var lotes = new List<FamilyLot>();
        foreach (var familia in q.Families)
        {
            var itens = ItemsOfFamily(q, familia);
            var ofertas = new List<FamilyOffer>();
            foreach (var p in atuais)
            {
                var cobre = itens.All(i => p.Items.Any(pi => pi.QuotationItemId == i));
                var (valorItens, total) = ShareOf(p, itens);
                if (!cobre && valorItens <= 0) continue;    // não cotou nada desta família
                ofertas.Add(new FamilyOffer(p.SupplierId, p.SupplierName, p.Id, p.VersionNumber,
                    valorItens, total, p.DeliveryDays, p.PaymentTerms, cobre, false));
            }
            lotes.Add(new FamilyLot(familia, itens.Count,
                q.Items.Where(i => itens.Contains(i.Id)).Sum(i => i.Quantity),
                Ordenadas(ofertas)));
        }
        return lotes;
    }

    /// <summary>
    /// O mesmo mapa, com a situação de cada fornecedor no cadastro. É o que a tela precisa para
    /// só oferecer quem pode mesmo vencer a família: sem isso o comprador escolhe, escreve a
    /// justificativa e só então leva SUP-ERR-030 (não homologado) ou RFQ-ERR-040 (inativo).
    /// O "mais barato" também passa a ser o mais barato <em>entre os que podem vencer</em> —
    /// destacar como melhor oferta quem o servidor vai recusar seria a mesma armadilha.
    /// </summary>
    public async Task<IReadOnlyList<FamilyLot>> FamilyMapAsync(Quotation q, CancellationToken ct = default)
    {
        var lotes = FamilyMap(q);
        var ids = lotes.SelectMany(l => l.Offers).Select(o => o.SupplierId).Distinct().ToList();
        if (ids.Count == 0) return lotes;
        var fornecedores = await db.Suppliers.Include(f => f.Documents)
            .Where(f => ids.Contains(f.Id)).ToListAsync(ct);
        var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return lotes.Select(l => l with { Offers = Ordenadas(l.Offers.Select(o =>
        {
            var f = fornecedores.SingleOrDefault(x => x.Id == o.SupplierId);
            // fornecedor sumido do cadastro não vira "homologado por omissão": não pode vencer
            return o with { Homologation = f?.EffectiveHomologation(hoje) ?? SupplierHomologation.Bloqueado,
                            Active = f?.Active ?? false };
        }).ToList()) }).ToList();
    }

    /// <summary>
    /// Ordem e destaque do lote: quem pode levar a família primeiro, do mais barato ao mais caro,
    /// com o menor total marcado. Recalcula a marca porque o que pode vencer muda quando a
    /// situação do fornecedor entra na conta.
    /// </summary>
    private static IReadOnlyList<FamilyOffer> Ordenadas(IReadOnlyList<FamilyOffer> ofertas)
    {
        var menor = ofertas.Where(o => o.CanWin).OrderBy(o => o.TotalValue).FirstOrDefault();
        return ofertas.Select(o => o with { Cheapest = menor is not null && o.CanWin && o.TotalValue == menor.TotalValue })
            .OrderByDescending(o => o.CanWin).ThenByDescending(o => o.Complete).ThenBy(o => o.TotalValue).ToList();
    }

    /// <summary>O que um pedido de adjudicação cobre: um item, ou a família inteira.</summary>
    private static List<Guid> EscopoDe(Quotation q, AwardInput pedido) =>
        pedido.QuotationItemId is { } item ? [item] : ItemsOfFamily(q, QuotationAward.FamilyKey(pedido.Family));

    /// <summary>Descrição do item para a mensagem de erro — id cru não ajuda quem lê.</summary>
    private static string DescricaoDoItem(Quotation q, Guid itemId) =>
        q.Items.SingleOrDefault(i => i.Id == itemId)?.Description ?? "o item escolhido";

    /// <summary>Itens de uma família dentro do processo (a família do item é snapshot do catálogo).</summary>
    private static List<Guid> ItemsOfFamily(Quotation q, string familia) =>
        q.Items.Where(i => QuotationAward.FamilyKey(i.Family) == familia).Select(i => i.Id).ToList();

    /// <summary>
    /// O que uma adjudicação cobre. É aqui que os dois grãos convivem: com item apontado,
    /// ela cobre aquele item só — a divisão do papel e da caneta entre fornecedores
    /// diferentes; sem item, cobre a família inteira, como sempre cobriu.
    ///
    /// <para>
    /// Todo cálculo a jusante (rateio de frete, valor da O.C., saving) pergunta por aqui em
    /// vez de assumir a família, e por isso nenhum deles precisou mudar de conta.
    /// </para>
    /// </summary>
    internal static List<Guid> ItemsCovered(Quotation q, QuotationAward award) =>
        award.QuotationItemId is { } item ? [item] : ItemsOfFamily(q, award.Family);

    /// <summary>
    /// Fatia de uma proposta: os itens indicados pelo preço cotado mais o rateio proporcional de
    /// frete, impostos, outros custos e desconto. Fornecedor que leva tudo fica com o total cheio.
    /// </summary>
    private static (decimal items, decimal total) ShareOf(Proposal p, IReadOnlyCollection<Guid> quotationItemIds)
    {
        var cotado = p.Items.Sum(i => i.UnitPrice * i.Quantity);
        var fatia = p.Items.Where(i => quotationItemIds.Contains(i.QuotationItemId))
            .Sum(i => i.UnitPrice * i.Quantity);
        if (p.Items.All(i => quotationItemIds.Contains(i.QuotationItemId)))
            return (fatia, p.TotalValue);          // levou a proposta inteira: sem rateio, sem arredondamento
        var extras = (p.FreightValue ?? 0) + (p.TaxValue ?? 0) + (p.OtherCosts ?? 0) - (p.DiscountValue ?? 0);
        var proporcao = cotado > 0 ? fatia / cotado : 0m;
        return (fatia, Math.Round(fatia + extras * proporcao, 2));
    }

    /// <summary>
    /// As três réguas do saving (S.17), apuradas juntas e guardadas separadas.
    ///
    /// | Régua | Base | Mede |
    /// |---|---|---|
    /// | **negociação** | 1ª proposta do vencedor | o que o comprador arrancou do mesmo fornecedor |
    /// | **competição** | maior proposta comparável | o que valeu ter chamado mais gente para o BID |
    /// | **orçamento** | orçamento das SCs | o quanto ficou abaixo do que o solicitante previa |
    ///
    /// Nenhuma substitui a outra, e somar as três contaria o mesmo dinheiro três vezes.
    /// Cada uma vira nula quando não tem base honesta: sem concorrente que cotasse a
    /// família inteira não há ganho de concorrência; sem orçamento em toda SC do
    /// processo não há como comparar com orçamento.
    /// </summary>
    private void ApplyAwardSaving(Quotation q, IReadOnlyList<QuotationAward> awards,
        IReadOnlyList<decimal?>? orcamentos = null)
    {
        decimal baseline = 0, fechado = 0, maiorProposta = 0;
        var houveConcorrencia = false;

        foreach (var a in awards)
        {
            // o baseline compara a MESMA fatia: se a adjudicação é de um item só, a
            // primeira proposta entra por aquele item, não pela família inteira
            var itens = ItemsCovered(q, a);
            var primeira = q.Proposals.Where(p => p.SupplierId == a.SupplierId)
                .OrderBy(p => p.VersionNumber).First();
            baseline += ShareOf(primeira, itens).total;
            fechado += a.TotalValue;

            // competição: a maior entre as ofertas que cobrem a família inteira. Só entra
            // quem cotou tudo — comparar com quem cotou metade inflaria o ganho de graça
            var lote = FamilyMap(q).FirstOrDefault(l => l.Family == a.Family);
            var completas = lote?.Offers.Where(o => o.Complete).ToList() ?? [];
            if (completas.Count > 1)
            {
                houveConcorrencia = true;
                maiorProposta += completas.Max(o => o.TotalValue);
            }
            else
            {
                // sem concorrente nesta família, ela entra pelo próprio valor fechado:
                // não gera ganho nem esconde o que as outras famílias geraram
                maiorProposta += a.TotalValue;
            }
        }

        q.BaselineValue = baseline;
        q.NegotiatedValue = fechado;
        q.SavingValue = baseline - fechado;
        q.SavingPercent = baseline > 0 ? Math.Round((baseline - fechado) / baseline * 100m, 2) : 0m;

        q.CompetitionBaselineValue = houveConcorrencia ? maiorProposta : null;
        q.CompetitionSaving = houveConcorrencia ? maiorProposta - fechado : null;

        // orçamento: só compara quando TODAS as SCs do processo informaram o seu. Com uma
        // sem orçamento, o total fechado seria comparado a um orçamento parcial — e o
        // "saving" sairia inflado pela SC que ninguém orçou
        var completos = orcamentos is not null && orcamentos.Count > 0 && orcamentos.All(o => o is > 0);
        q.BudgetBaselineValue = completos ? orcamentos!.Sum(o => o!.Value) : null;
        q.BudgetSaving = completos ? q.BudgetBaselineValue - fechado : null;
    }

    /// <summary>Famílias do catálogo, em caixa alta; item digitado (sem catálogo) entra em DIVERSOS.</summary>
    private async Task<Dictionary<Guid, string>> FamiliesOfAsync(IEnumerable<Guid?> catalogItemIds, CancellationToken ct)
    {
        var ids = catalogItemIds.Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.CatalogItems.Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => QuotationAward.FamilyKey(c.Family), ct);
    }

    /// <summary>
    /// Família do item na cotação. A do <b>item da SC</b> vem primeiro: é ela que o
    /// solicitante escolheu para o produto não cadastrado, e sem isso o item digitado à
    /// mão caía sempre em DIVERSOS, fora de todo agrupamento. O catálogo é a origem para o
    /// produto cadastrado, e continua atendendo os itens gravados antes deste campo existir.
    /// </summary>
    private static string FamilyOf(RequisitionItem item, IReadOnlyDictionary<Guid, string> familias) =>
        !string.IsNullOrWhiteSpace(item.Family) ? QuotationAward.FamilyKey(item.Family)
        : item.CatalogItemId is { } id && familias.TryGetValue(id, out var f) ? f
        : QuotationAward.Default;
}
