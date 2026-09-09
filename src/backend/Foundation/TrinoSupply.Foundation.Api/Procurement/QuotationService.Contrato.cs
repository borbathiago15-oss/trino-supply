using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>Preço de contrato de um item do processo, para preencher a proposta.</summary>
public record PrecoDeContrato(
    Guid QuotationItemId, string Description, decimal UnitPrice,
    int? DeliveryDays, string? PaymentTerms, int? PaymentDays);

/// <summary>
/// O que o contrato de parceria já responde sobre este processo. Vazio quando não há
/// contrato vigente ou quando nenhum item do processo está nele.
/// </summary>
public record CoberturaDoContrato(
    bool Current, string? ContractNumber, DateOnly? ValidUntil,
    IReadOnlyList<PrecoDeContrato> Items)
{
    public static readonly CoberturaDoContrato Nenhuma = new(false, null, null, []);
}

public partial class QuotationService
{
    /// <summary>
    /// Preços que o contrato de parceria já fixou para os itens deste processo.
    ///
    /// <para>
    /// Existe porque o comprador estava redigitando um preço que o contrato já define: se
    /// há acordo vigente para aquele produto com aquele fornecedor, o número da proposta
    /// não é uma novidade a descobrir — é o que foi combinado. A tela usa isto para
    /// preencher, e o comprador continua podendo mudar: contrato é o ponto de partida,
    /// não uma trava.
    /// </para>
    ///
    /// <para>
    /// <b>Contrato fora da vigência não preenche nada.</b> É a parte que importa: um preço
    /// de contrato vencido entrando calado na proposta seria pior do que campo vazio,
    /// porque o comprador fecharia por um valor que já não vale.
    /// </para>
    /// </summary>
    public async Task<CoberturaDoContrato> ContractPricesAsync(
        Guid quotationId, Guid supplierId, CancellationToken ct = default)
    {
        var q = await db.Quotations.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == quotationId, ct);
        if (q is null) return CoberturaDoContrato.Nenhuma;

        var supplier = await db.Suppliers.Include(s => s.ContractItems)
            .SingleOrDefaultAsync(s => s.Id == supplierId, ct);
        var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (supplier is null || !supplier.Active || !supplier.ContractIsCurrent(hoje))
            return CoberturaDoContrato.Nenhuma;

        var precos = new List<PrecoDeContrato>();
        foreach (var item in q.Items.OrderBy(i => i.Sequence))
        {
            var doContrato = Casar(supplier.ContractItems, item);
            if (doContrato is null) continue;
            precos.Add(new PrecoDeContrato(item.Id, item.Description, doContrato.UnitPrice,
                doContrato.DeliveryDays, doContrato.PaymentTerms, doContrato.PaymentDays));
        }

        return new CoberturaDoContrato(true, supplier.ContractNumber, supplier.ContractValidUntil, precos);
    }

    /// <summary>
    /// Fecha pelo contrato de parceria: abre o processo já decidido, com o fornecedor
    /// parceiro e os preços que o contrato fixou, e o deixa pronto para as aprovações.
    ///
    /// <para>
    /// <b>Isto não pula o BID — reconhece que ele já aconteceu.</b> A concorrência foi feita
    /// quando o contrato foi negociado; repeti-la a cada reposição de item contratado é
    /// pedir ao comprador que refaça um trabalho cujo resultado já está assinado. O que
    /// desaparece é o convite, a espera e a comparação; o que fica é tudo que decide
    /// dinheiro: as duas alçadas, a segregação de funções (<c>RFQ-ERR-030</c>, porque quem
    /// aciona este caminho é quem "escolheu") e o registro da O.C. do ERP
    /// (<c>PO-BR-011</c>).
    /// </para>
    ///
    /// <para>
    /// Por isso o processo é uma <b>cotação de verdade</b>, e não um pedido direto: a
    /// <c>PO-ERR-023</c> continua valendo, e reaproveitar a máquina de aprovação existente
    /// evita uma segunda régua de alçada vivendo em paralelo — que foi o que a regra
    /// <c>RFQ-BR-010</c> quis impedir desde o começo.
    /// </para>
    ///
    /// <para>
    /// Itens <b>não cobertos</b> pelo contrato não entram. Ficam na fila e seguem para
    /// cotação normal, que é o caminho honesto quando não há preço acordado para eles.
    /// </para>
    /// </summary>
    public async Task<(Quotation? q, UserError? error)> FecharPorContratoAsync(
        Actor actor, IReadOnlyList<Guid> prItemIds, Guid supplierId, CancellationToken ct = default)
    {
        if (!CanConduct(actor.Role))
            return (null, new("RFQ-ERR-900", "Seu papel não conduz processos de compra."));

        var (q, erroAbertura) = await CreateFromItemsAsync(
            actor, prItemIds, QuotationKind.Purchase, null,
            "Compra por contrato de parceria: preço já acordado, sem nova concorrência.", ct);
        if (erroAbertura is not null) return (null, erroAbertura);

        var cobertura = await ContractPricesAsync(q!.Id, supplierId, ct);
        if (!cobertura.Current)
            return (null, new("CT-ERR-020",
                "Este fornecedor não tem contrato de parceria vigente — a compra segue por cotação."));

        // todo item do processo precisa de preço no contrato: fechar com parte dos itens
        // sem preço acordado seria inventar o acordo para o resto
        var semPreco = q.Items.Where(i => cobertura.Items.All(c => c.QuotationItemId != i.Id)).ToList();
        if (semPreco.Count > 0)
            return (null, new("CT-ERR-021",
                $"Fora do contrato: {string.Join(", ", semPreco.Select(i => i.Description))}. "
                + "Abra estes itens em cotação normal e deixe no contrato apenas os que ele cobre."));

        var (_, erroConvite) = await InviteSuppliersAsync(actor, q.Id, [supplierId], ct);
        if (erroConvite is not null) return (null, erroConvite);

        var doContrato = cobertura.Items;
        var proposta = new ProposalInput(
            DeliveryDays: doContrato.Select(i => i.DeliveryDays).FirstOrDefault(d => d is not null),
            PaymentTerms: doContrato.Select(i => i.PaymentTerms).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)),
            FreightValue: null, ValidUntil: cobertura.ValidUntil, Notes: null,
            Items: doContrato.Select(i => new ProposalItemInput(i.QuotationItemId, i.UnitPrice, null)).ToList(),
            PaymentDays: doContrato.Select(i => i.PaymentDays).FirstOrDefault(d => d is not null));
        var rotulo = cobertura.ContractNumber is { } numero ? $"Contrato {numero}" : "Contrato de parceria";
        var (_, erroProposta) = await SubmitProposalAsync(q.Id, supplierId, proposta, "CONTRATO", rotulo, ct);
        if (erroProposta is not null) return (null, erroProposta);

        var (_, erroAnalise) = await CloseForAnalysisAsync(actor, q.Id, ct);
        if (erroAnalise is not null) return (null, erroAnalise);

        var atual = await GetAsync(q.Id, ct);
        var doFornecedor = atual!.Proposals.Single(p => p.SupplierId == supplierId);
        var justificativa = cobertura.ValidUntil is { } ate
            ? $"{rotulo}, vigente até {ate:dd/MM/yyyy}: preço acordado previamente, sem nova concorrência."
            : $"{rotulo}: preço acordado previamente, sem nova concorrência.";
        var (fechado, erroEscolha) = await AwardByItemAsync(actor, q.Id,
            atual.Items.Select(i => new AwardInput("", doFornecedor.Id, "Contrato", justificativa, i.Id)).ToList(), ct);
        if (erroEscolha is not null) return (null, erroEscolha);

        return (fechado, null);
    }

    /// <summary>
    /// Qual linha do contrato corresponde ao item do processo. O produto do catálogo é a
    /// identidade boa e vem primeiro; o código é o segundo caminho, para o contrato que foi
    /// cadastrado por código antes de o produto existir no catálogo.
    ///
    /// <para>
    /// A descrição <b>não</b> entra: "BOTA BIQUEIRA DE PVC" e "BOTA BIQUEIRA DE AÇO" são
    /// dois produtos com nomes quase iguais, e casar por texto colocaria o preço de um no
    /// outro sem ninguém notar.
    /// </para>
    /// </summary>
    private static SupplierContractItem? Casar(
        IEnumerable<SupplierContractItem> contrato, QuotationItem item)
    {
        var linhas = contrato as IList<SupplierContractItem> ?? contrato.ToList();
        if (item.CatalogItemId is { } catalogo
            && linhas.FirstOrDefault(c => c.CatalogItemId == catalogo) is { } porCatalogo)
            return porCatalogo;
        if (!string.IsNullOrWhiteSpace(item.CatalogCode))
            return linhas.FirstOrDefault(c => c.CatalogItemId is null
                && string.Equals(c.CatalogCode, item.CatalogCode, StringComparison.OrdinalIgnoreCase));
        return null;
    }
}
