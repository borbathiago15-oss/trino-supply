using Microsoft.EntityFrameworkCore;

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
