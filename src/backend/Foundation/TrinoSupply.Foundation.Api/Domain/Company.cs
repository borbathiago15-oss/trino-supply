namespace TrinoSupply.Foundation.Api.Domain;

/// <summary>
/// Empresa do grupo (CNPJ). O grupo possui vários CNPJs e cada centro de custo
/// pode estar ligado a um CNPJ diferente — a OC usa os dados da empresa do CC de origem.
/// </summary>
public class Company
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LegalName { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;        // CNPJ (dígitos), único
    public string? StateRegistration { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? District { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Zip { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}
