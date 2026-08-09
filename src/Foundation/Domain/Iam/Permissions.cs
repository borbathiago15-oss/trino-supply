namespace TrinoSupply.Foundation.Domain.Iam;

/// <summary>
/// Catálogo de permissões da plataforma (FD-001-01, SEC-001 §deny-by-default).
/// Permissão é um verbo de negócio no formato <c>recurso.acao</c>. A ausência de permissão
/// explícita é negação — nunca há acesso implícito. Novos módulos ADICIONAM constantes aqui.
/// </summary>
public static class PermissionCatalog
{
    // Foundation / IAM
    public const string UsersRead = "users.read";
    public const string UsersManage = "users.manage";     // criar, ativar/desativar, atribuir papéis
    public const string RolesRead = "roles.read";
    public const string RolesManage = "roles.manage";     // criar papéis, conceder/revogar permissões
    public const string AuditRead = "audit.read";          // ler a trilha de auditoria

    // Materiais (MMS-002). TODO: evoluir para catálogo por módulo (cada BC contribui suas permissões).
    public const string MaterialsRead = "materials.read";
    public const string MaterialsManage = "materials.manage";

    // Compras (PR-001). Segregação: requisitar e aprovar são permissões distintas (SoD).
    public const string PurchasesRead = "purchases.read";
    public const string PurchasesRequest = "purchases.request";
    public const string PurchasesApprove = "purchases.approve";
    public const string PurchasesOrder = "purchases.order";   // emitir pedido + cadastrar fornecedor

    /// <summary>Todas as permissões conhecidas — usado para validar concessões (não conceder desconhecida).</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        UsersRead, UsersManage, RolesRead, RolesManage, AuditRead,
        MaterialsRead, MaterialsManage,
        PurchasesRead, PurchasesRequest, PurchasesApprove, PurchasesOrder
    };

    public static bool IsKnown(string permission) => All.Contains(permission);
}
