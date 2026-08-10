using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Procurement.Application;

public sealed record PayingCompanyView(
    Guid Id, string Code, string LegalName, string TaxId, string StateRegistration, string Address,
    string District, string City, string State, string ZipCode, string Phone, string Email, string Status);

/// <summary>Dados de uma empresa pagadora (CNPJ do grupo responsável pelo pagamento na OC).</summary>
public sealed record PayingCompanyInput(
    string Code, string LegalName, string TaxId, string? StateRegistration = null, string? Address = null,
    string? District = null, string? City = null, string? State = null, string? ZipCode = null,
    string? Phone = null, string? Email = null);

/// <summary>
/// Cadastro de empresas pagadoras (múltiplos CNPJs por tenant). Na emissão da OC seleciona-se qual
/// empresa é a responsável pelo pagamento — ela compõe o cabeçalho comprador do documento.
/// </summary>
public interface IPayingCompanyService
{
    Task<Result<Guid>> CreateAsync(PayingCompanyInput input, CancellationToken ct = default);
    Task<Result> UpdateAsync(Guid id, PayingCompanyInput input, CancellationToken ct = default);
    Task<Result<PayingCompanyView>> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PayingCompanyView>> ListAsync(CancellationToken ct = default);
}
