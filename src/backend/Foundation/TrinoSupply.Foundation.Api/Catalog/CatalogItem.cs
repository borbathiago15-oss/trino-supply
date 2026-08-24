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
    /// <summary>Item de almoxarifado: tem saldo controlado, entra/sai por documento e pode ser solicitado ao estoque.</summary>
    public bool StockControlled { get; set; }
    /// <summary>Estoque mínimo do almoxarifado — abaixo dele o item entra na lista de reposição.</summary>
    public decimal? MinimumQty { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public int Version { get; set; } = 1;
    /// <summary>Fornecedores conhecidos deste produto (texto livre, vários por item).</summary>
    public List<CatalogItemSupplier> Suppliers { get; set; } = [];
}

/// <summary>
/// Fornecedor de um produto do catálogo. O mesmo produto costuma ter vários fornecedores,
/// e aqui eles são texto livre: fornecedor de produto não precisa estar cadastrado na
/// plataforma (o cadastro oficial existe para quem participa de cotação/OC).
/// </summary>
public class CatalogItemSupplier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CatalogItemId { get; set; }
    public string SupplierName { get; set; } = string.Empty;   // razão social ou nome de mercado
    public string? TaxId { get; set; }                         // CNPJ/CPF (opcional, só dígitos)
    public string? Contact { get; set; }                       // telefone, e-mail ou vendedor
    public string? SupplierItemCode { get; set; }              // código do produto no fornecedor
    public decimal? LastPrice { get; set; }                    // última referência de preço
    public string? Notes { get; set; }
    public Guid? SupplierId { get; set; }                      // vínculo opcional com o cadastro oficial
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Família (grupo) de produtos — cadastro próprio para o catálogo não acumular variações
/// da mesma família escritas de formas diferentes. O nome, em caixa alta, é a identidade.
/// </summary>
public class ProductFamily
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;   // único, caixa alta (ex.: MATERIAL DE LIMPEZA)
    public string? Notes { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}
