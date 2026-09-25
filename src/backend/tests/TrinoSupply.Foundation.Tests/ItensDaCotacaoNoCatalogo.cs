using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// Cadastra no catálogo, ao gravar, todo item de cotação que nasceu sem produto. Desde a regra
/// RFQ-ERR-026 comprar exige produto, e os cenários destes testes descrevem compras com itens
/// digitados — o que eles verificam é alçada, divisão, saving e prazo, não o cadastro. Em vez
/// de repetir "cadastre o produto" em cada um, o fixture os trata como já cadastrados: a
/// família fica a do item (o lote da adjudicação continua o mesmo) e só o vínculo aparece.
/// A regra em si tem teste próprio, sem este interceptor (<c>ProdutoForaDoCatalogoTests</c>).
/// </summary>
public sealed class ItensDaCotacaoNoCatalogo : SaveChangesInterceptor
{
    public static DbContextOptionsBuilder<T> Em<T>(DbContextOptionsBuilder<T> b) where T : DbContext =>
        b.AddInterceptors(new ItensDaCotacaoNoCatalogo());

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (eventData.Context is { } db) Catalogar(db);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } db) Catalogar(db);
        return base.SavingChanges(eventData, result);
    }

    private static void Catalogar(DbContext db)
    {
        var novos = db.ChangeTracker.Entries<QuotationItem>()
            .Where(e => e.State == EntityState.Added && e.Entity.CatalogItemId is null)
            .Select(e => e.Entity).ToList();
        foreach (var item in novos)
        {
            var produto = new CatalogItem
            {
                Code = "T-" + item.Id.ToString("N")[..8].ToUpperInvariant(),
                Description = item.Description,
                Family = QuotationAward.FamilyKey(item.Family),
                UnitOfMeasure = item.UnitOfMeasure,
                Purchasable = true,
            };
            db.Add(produto);
            item.CatalogItemId = produto.Id;
        }
    }
}
