using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Domain;

/// <summary>De onde o local de entrega veio: o estoque ou o cadastro de centros de custo.</summary>
public static class TiposDeLocal
{
    public const string Almoxarifado = "ALMOXARIFADO";
    public const string CentroDeCusto = "CENTRO_DE_CUSTO";
}

/// <summary>Um endereço onde o material pode ser entregue. `Kind` diz de qual cadastro ele saiu.</summary>
public record LocalDeEntrega(Guid Id, string Code, string Name, string Kind);

/// <summary>
/// Os locais de entrega dos formulários de solicitação. São dois cadastros, não um:
/// os almoxarifados do estoque e os centros de custo marcados como quem recebe material.
/// O material vai para o endereço do cliente e o centro que paga pode ser outro — pedir
/// para o "Novo Atacarejo PB" e entregar no "Whirlpool PB" é o caso comum, e antes desta
/// regra só o almoxarifado aparecia na lista.
///
/// São duas consultas e não uma união: `Concat` sobre entidades diferentes não tem tradução
/// no Npgsql, e a lista é curta o bastante para ser juntada em memória. Filtro e ordenação
/// ficam na entidade; a projeção vem por último.
/// </summary>
public static class LocaisDeEntrega
{
    public static async Task<List<LocalDeEntrega>> ListarAsync(AppDbContext db, CancellationToken ct = default)
    {
        var almoxarifados = await db.StorageLocations
            .Where(l => l.Active).OrderBy(l => l.Code)
            .Select(l => new LocalDeEntrega(l.Id, l.Code, l.Name, TiposDeLocal.Almoxarifado))
            .ToListAsync(ct);

        var centros = await db.CostCenters
            .Where(c => c.Active && c.ReceivesMaterial).OrderBy(c => c.Code)
            .Select(c => new LocalDeEntrega(c.Id, c.Code, c.Name, TiposDeLocal.CentroDeCusto))
            .ToListAsync(ct);

        // o almoxarifado vem primeiro: é o destino da maioria das SCs
        return [.. almoxarifados, .. centros];
    }
}
