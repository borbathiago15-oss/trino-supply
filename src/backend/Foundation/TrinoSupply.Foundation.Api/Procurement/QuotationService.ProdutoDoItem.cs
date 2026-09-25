using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// O item fora do catálogo na cotação. Pedir o que ninguém cadastrou continua possível — a SC
/// aceita, a cotação cota —, mas <b>comprar</b> não: sem produto, a compra não tem código para a
/// O.C., não entra no histórico de preço, não casa com contrato nem com C.A., e a próxima compra
/// do mesmo item começa do zero. É a mesma lógica do fornecedor pré-cadastrado, que cota em pé
/// de igualdade e só precisa estar homologado para vencer (SUP-ERR-030): aqui, o item pode ser
/// cotado de qualquer jeito, e precisa virar produto antes de alguém escolher o vencedor
/// (<c>RFQ-ERR-026</c>). Quem cadastra é o comprador (decisão da empresa, 2026-09), na própria
/// cotação — mandar o cadastro para uma fila travaria a escolha esperando outra pessoa.
/// </summary>
public partial class QuotationService
{
    /// <summary>Etapas em que o produto do item ainda pode ser definido: antes da escolha e no orçamento parado.</summary>
    private static readonly QuotationStatus[] EtapasDoVinculo =
        [QuotationStatus.Open, QuotationStatus.Analysis, QuotationStatus.BudgetPresented];

    /// <summary>
    /// Os itens que ainda precisam virar produto. É a mesma pergunta que a escolha do vencedor
    /// e a conversão do orçamento fazem, e a que a tela faz para avisar antes do clique.
    /// </summary>
    public static IReadOnlyList<QuotationItem> ForaDoCatalogo(Quotation q) =>
        q.Items.Where(i => i.CatalogItemId is null).OrderBy(i => i.Sequence).ToList();

    /// <summary>RFQ-ERR-026: a lista do que falta cadastrar, pelo nome — o comprador precisa saber qual.</summary>
    public static UserError? ErroDeCatalogo(Quotation q)
    {
        var fora = ForaDoCatalogo(q);
        if (fora.Count == 0) return null;
        return new("RFQ-ERR-026",
            $"Cadastre no catálogo antes de escolher o vencedor: {string.Join(", ", fora.Select(i => i.Description))}. "
            + "Use \"Cadastrar produto\" no item da cotação.");
    }

    /// <summary>
    /// O que impede definir o produto de um item — conferido <b>antes</b> de cadastrar um produto
    /// novo, para o cadastro não nascer à toa quando o vínculo seria recusado em seguida.
    /// </summary>
    public async Task<(Quotation? q, UserError? error)> ImpedimentoDoProdutoAsync(
        Actor actor, Guid quotationId, Guid itemId, CancellationToken ct = default)
    {
        if (!CanConduct(actor.Role))
            return (null, new("RFQ-ERR-900", "Só quem conduz compra define o produto do item."));
        var q = await GetAsync(quotationId, ct);
        if (q is null) return (null, new("RFQ-ERR-404", "Cotação não encontrada."));
        if (!EtapasDoVinculo.Contains(q.Status))
            return (null, new("RFQ-ERR-027", "O produto do item se define até a escolha do vencedor."));
        var item = q.Items.SingleOrDefault(i => i.Id == itemId);
        if (item is null) return (null, new("RFQ-ERR-023", "O item não faz parte desta cotação."));
        if (item.CatalogItemId is not null)
            return (null, new("RFQ-ERR-027", $"{item.Description} já é um produto do catálogo ({item.CatalogCode})."));
        return (q, null);
    }

    /// <summary>
    /// Transforma o item digitado no produto do catálogo: código, descrição e família passam a
    /// ser os do cadastro, e a SC de origem ganha o mesmo vínculo — a Torre e o histórico
    /// passam a enxergar o produto. A quantidade e a unidade ficam: as propostas foram dadas
    /// sobre elas. No orçamento já apresentado a família também fica, porque a adjudicação por
    /// família já está feita e mudar a chave faria o item sair do lote que o fornecedor levou.
    /// </summary>
    public async Task<(Quotation? q, UserError? error)> DefinirProdutoDoItemAsync(
        Actor actor, Guid quotationId, Guid itemId, Guid catalogItemId, CancellationToken ct = default)
    {
        var (q, erro) = await ImpedimentoDoProdutoAsync(actor, quotationId, itemId, ct);
        if (erro is not null) return (null, erro);
        var produto = await db.CatalogItems.SingleOrDefaultAsync(c => c.Id == catalogItemId, ct);
        if (produto is null) return (null, new("IC-ERR-404", "Produto não encontrado no catálogo."));
        if (!produto.Active)
            return (null, new("IC-ERR-032", $"{produto.Code} está inativo — reative no cadastro antes de usá-lo na compra."));

        var item = q!.Items.Single(i => i.Id == itemId);
        var textoAntigo = item.Description;
        item.CatalogItemId = produto.Id;
        item.CatalogCode = produto.Code;
        item.Description = produto.Description;
        if (q.Status != QuotationStatus.BudgetPresented)
            item.Family = QuotationAward.FamilyKey(produto.Family);

        if (item.SourcePrItemId is { } scItem
            && await db.RequisitionItems.SingleOrDefaultAsync(r => r.Id == scItem, ct) is { } daSc
            && daSc.CatalogItemId is null)
        {
            daSc.CatalogItemId = produto.Id;
            daSc.CatalogCode = produto.Code;
            daSc.Family = produto.Family;
        }

        AddEvent(q, "PRODUTO_DO_ITEM",
            $"Item fora do catálogo \"{textoAntigo}\" agora é o produto {produto.Code} — {produto.Description}.",
            actor);
        await TouchAndSaveAsync(q, ct);
        return (q, null);
    }
}
