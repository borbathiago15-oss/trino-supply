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
    /// <summary>Número do C.A. (Certificado de Aprovação) — obrigatório em EPI e EPC.</summary>
    public string? CaNumber { get; set; }
    /// <summary>FISPQ (Ficha de Informações de Segurança) — obrigatória em produto químico.</summary>
    public Guid? FispqDocumentId { get; set; }
    public string? FispqFileName { get; set; }
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

/// <summary>
/// Tipos de produto do catálogo. O tipo define exigências de conformidade:
/// químico exige FISPQ anexada; EPI e EPC exigem o número do C.A.
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

    /// <summary>EPI e EPC só operam com o C.A. informado (NR-06).</summary>
    public static bool RequiresCa(string? key) => key is Epi or Epc;

    /// <summary>Produto químico só opera com a FISPQ anexada (NR-26 / GHS).</summary>
    public static bool RequiresFispq(string? key) => key == Quimicos;
}
