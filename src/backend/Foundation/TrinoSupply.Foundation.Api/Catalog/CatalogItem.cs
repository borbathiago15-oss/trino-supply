namespace TrinoSupply.Foundation.Api.Catalog;

/// <summary>
/// Item do catálogo (MMS-002 MVP): identidade oficial do material, agrupada por família
/// (ex.: MATERIAL DE LIMPEZA, MATERIAL DE ESCRITÓRIO, EPI, FARDAMENTO).
/// Somente itens Ativos são operáveis em novas requisições (IC-BR-001).
/// </summary>
public class CatalogItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;        // único (ex.: LMP-001)
    public string Description { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;      // família/grupo em caixa alta
    public string UnitOfMeasure { get; set; } = "UN";
    public decimal? ReferencePrice { get; set; }            // preço de referência (gerencial)
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public int Version { get; set; } = 1;
}
