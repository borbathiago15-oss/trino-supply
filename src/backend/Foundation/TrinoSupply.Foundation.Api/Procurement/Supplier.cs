namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// Fornecedor (SUP-001 MVP): identidade fiscal única (SUP-BR-001) e situação.
/// Inativo não recebe novos pedidos (SUP-BR-002); pedidos já emitidos não são afetados (SUP-BR-003).
/// </summary>
public class Supplier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LegalName { get; set; } = string.Empty;
    public string? TradeName { get; set; }
    public string TaxId { get; set; } = string.Empty;   // somente dígitos (CPF 11 / CNPJ 14), único
    public string? Email { get; set; }
    public string? Phone { get; set; }
    /// <summary>Hash SHA-256 da chave do Portal do Fornecedor; a chave em claro nunca é persistida.</summary>
    public string? PortalKeyHash { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public int Version { get; set; } = 1;

    // ---- contrato de parceria ----------------------------------------------
    /// <summary>Número/identificação do contrato de parceria com este fornecedor.</summary>
    public string? ContractNumber { get; set; }
    /// <summary>Teto financeiro do contrato (V2-P2); as O.C.s abatem o saldo.</summary>
    public decimal? ContractValueLimit { get; set; }
    /// <summary>Consumo do contrato na vigência — derivado, preenchido na leitura (não mapeado).</summary>
    public decimal? ContractConsumed { get; set; }
    public DateOnly? ContractValidFrom { get; set; }
    public DateOnly? ContractValidUntil { get; set; }
    public string? ContractNotes { get; set; }
    /// <summary>Produtos com preço e prazos fixos enquanto o contrato valer.</summary>
    public List<SupplierContractItem> ContractItems { get; set; } = [];

    /// <summary>Contrato válido na data: dentro da vigência (quando informada) e com itens.</summary>
    public bool ContractIsCurrent(DateOnly today) =>
        ContractItems.Count > 0
        && (ContractValidFrom is null || today >= ContractValidFrom)
        && (ContractValidUntil is null || today <= ContractValidUntil);
}

/// <summary>
/// Produto do contrato de parceria: preço, prazo de pagamento e prazo de entrega fixos,
/// para o comprador não precisar renegociar o que já está contratado.
/// </summary>
public class SupplierContractItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SupplierId { get; set; }
    public Guid? CatalogItemId { get; set; }              // produto do catálogo (quando existir)
    public string Description { get; set; } = string.Empty;  // snapshot da descrição
    public string? CatalogCode { get; set; }
    public string UnitOfMeasure { get; set; } = "UN";
    public decimal UnitPrice { get; set; }
    public string? PaymentTerms { get; set; }             // condição (ex.: 30/60 dias)
    public int? PaymentDays { get; set; }                 // prazo para pagamento, em dias
    public int? DeliveryDays { get; set; }                // prazo de entrega, em dias
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
