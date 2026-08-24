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
}
