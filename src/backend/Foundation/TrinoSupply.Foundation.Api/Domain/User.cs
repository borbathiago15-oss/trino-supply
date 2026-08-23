namespace TrinoSupply.Foundation.Api.Domain;

/// <summary>
/// Usuário da plataforma (FD-001-01 IAM — incremento mínimo).
/// A senha nunca é armazenada em claro: apenas o hash (PBKDF2 via PasswordHasher).
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = Roles.SystemAdministrator;
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class Roles
{
    public const string SystemAdministrator = "SystemAdministrator";
    public const string Requester = "Requester";
    public const string Approver = "Approver";
    public const string PurchasingOfficer = "PurchasingOfficer";
    public const string WarehouseOperator = "WarehouseOperator";
    public const string WarehouseSupervisor = "WarehouseSupervisor";
    public const string SupplyManager = "SupplyManager";
    public const string Auditor = "Auditor";
}
