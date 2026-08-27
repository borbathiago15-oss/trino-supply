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
    /// <summary>Disponível para solicitação de compra/cotação. Um item pode ser as duas coisas.</summary>
    public bool Purchasable { get; set; } = true;
    /// <summary>Estoque mínimo do almoxarifado — abaixo dele o item entra na lista de reposição.</summary>
    public decimal? MinimumQty { get; set; }
    /// <summary>Tipo do produto (ProductTypes) — define as exigências de conformidade.</summary>
    public string? ProductType { get; set; }
    /// <summary>Código do produto sem o tamanho (EPI/Fardamento: 12003 para 12003-P, 12003-M…).</summary>
    public string? BaseCode { get; set; }
    /// <summary>Tamanho da variante (P, M, G, GG, XG, XXG ou numérico 35–46).</summary>
    public string? Size { get; set; }
    // o C.A. saiu do produto e passou para o fornecedor (CatalogItemSupplier.CaNumber):
    // a mesma bota com biqueira tem um C.A. no fornecedor X e outro no fornecedor Y.

    /// <summary>Foto do produto — miniatura na lista, ampliada ao clicar (revisão de telas).</summary>
    public Guid? ImageDocumentId { get; set; }
    public string? ImageFileName { get; set; }
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
    /// <summary>C.A. (Certificado de Aprovação) deste fornecedor para o produto — EPI/EPC.</summary>
    public string? CaNumber { get; set; }
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

/// <summary>
/// Tipos de produto do catálogo. O tipo define a exigência de conformidade: EPI e EPC só
/// circulam com o C.A. informado em pelo menos um fornecedor do produto (NR-06).
/// </summary>
public static class ProductTypes
{
    public const string Insumos = "INSUMOS";
    public const string Epi = "EPI";
    public const string Epc = "EPC";
    public const string Quimicos = "QUIMICOS";
    public const string Fardamento = "FARDAMENTO";
    public const string Mro = "MRO";
    public const string Ti = "TI";
    public const string Administrativo = "ADMINISTRATIVO";
    public const string Higiene = "HIGIENE";
    public const string Imobilizado = "IMOBILIZADO";
    public const string Servicos = "SERVICOS";
    public const string Marketing = "MARKETING";

    /// <summary>Chave → rótulo apresentado no cadastro (ordem da lista).</summary>
    public static readonly (string Key, string Label)[] All =
    [
        (Insumos, "Insumos (Matérias-Primas e Apoio)"),
        (Epi, "EPI (Equipamento de Proteção Individual)"),
        (Epc, "EPC (Equipamento de Proteção Coletiva)"),
        (Quimicos, "Produtos Químicos"),
        (Fardamento, "Fardamento"),
        (Mro, "MRO (Manutenção, Reparo e Operação)"),
        (Ti, "TI e Informática (Hardware e Software)"),
        (Administrativo, "Bens de Consumo Administrativo (Escritório e Papelaria)"),
        (Higiene, "Higiene, Limpeza e Descartáveis"),
        (Imobilizado, "Ativos Imobilizados"),
        (Servicos, "Serviços (Mão de Obra e Contratos)"),
        (Marketing, "Brindes e Material de Marketing"),
    ];

    public static bool IsValid(string key) => All.Any(t => t.Key == key);
    public static string LabelOf(string? key) => All.FirstOrDefault(t => t.Key == key).Label ?? key ?? "—";

    /// <summary>EPI e EPC só operam com o C.A. informado em algum fornecedor (NR-06).</summary>
    public static bool RequiresCa(string? key) => key is Epi or Epc;
}
