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
    /// <summary>Módulos autorizados (CSV de chaves de AppModules); null = padrão do papel.</summary>
    public string? Modules { get; set; }
    /// <summary>Centros de custo vinculados (CSV de códigos) — escopo de solicitação do Júnior/Pleno; vazio = sem restrição.</summary>
    public string? CostCenters { get; set; }
    /// <summary>Diretor responsável pela 2ª alçada dos processos deste gerente (RFQ-001).</summary>
    public Guid? DirectorId { get; set; }
    /// <summary>
    /// Gestor de Suprimentos responsável pela 2ª alçada das compras <b>deste comprador</b>
    /// (decisão da empresa, 2026-09). O comprador solicita, cota e dá o próprio Nível 1; quem
    /// fecha é o gestor dele, não a diretoria. Nulo devolve o processo à régua de sempre —
    /// comprador sem responsável cadastrado não trava a fila, vai para o Nível 2 do centro.
    /// </summary>
    public Guid? SupplyManagerId { get; set; }
    /// <summary>
    /// Setor a que a pessoa pertence. Quem tem setor <b>não escolhe</b> o setor de um ciclo
    /// de melhoria: é o dela. Deixar escolher abriria a porta de pendurar trabalho no setor
    /// alheio, e a visibilidade por setor deixaria de querer dizer alguma coisa.
    /// </summary>
    public Guid? SectorId { get; set; }
    public bool Active { get; set; } = true;
    /// <summary>
    /// Senha provisória: quem cadastrou o usuário escolheu a senha, então ela precisa
    /// ser trocada no primeiro acesso (SEC-004). Enquanto estiver marcada, o token do
    /// usuário só abre a troca de senha — nenhuma outra rota responde.
    /// </summary>
    public bool MustChangePassword { get; set; }
    /// <summary>Quando o próprio usuário definiu a senha atual. Nulo = senha ainda é a de cadastro.</summary>
    public DateTimeOffset? PasswordChangedAt { get; set; }
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
    public const string Director = "Director";           // Diretor — 2ª alçada do processo de compras (RFQ-001)
    public const string Auditor = "Auditor";
}

/// <summary>
/// Módulos autorizáveis por usuário (autorização granular do cadastro):
/// o acesso efetivo é papel E módulo autorizado. Admin sempre tem todos.
/// </summary>
public static class AppModules
{
    public const string Solicitacoes = "SOLICITACOES";   // requisições de compra (unitária/múltipla)
    public const string Aprovacao = "APROVACAO";         // central de aprovação
    public const string Material = "MATERIAL";           // solicitação de material ao almoxarifado
    public const string Estoque = "ESTOQUE";             // dashboard, entrada/saída, fila de atendimento
    public const string Compras = "COMPRAS";             // demandas e pedidos de compra
    public const string Produtos = "PRODUTOS";           // cadastro de produtos (catálogo)
    public const string Fornecedores = "FORNECEDORES";   // cadastro de fornecedores
    public const string CentrosCusto = "CENTROS_CUSTO";  // cadastro de centros de custo (regional/gerente/cliente)
    public const string Usuarios = "USUARIOS";           // cadastro de usuários (somente admin)
    public const string Contratos = "CONTRATOS";         // painel de contratos de parceria (V2-P2)
    public const string Compliance = "COMPLIANCE";       // compliance score e controles (V2-P2)
    public const string Insights = "INSIGHTS";           // insights determinísticos + visão executiva (V2-P3)
    public const string PlanoAcao = "PLANO_ACAO";        // plano de ação: tarefa com dono e prazo (5W2H)
    public const string Pdca = "PDCA";                   // ciclo de melhoria: onde se trata a causa
    // Atender chamado de suporte. Abrir não pede módulo nenhum — pedir ajuda não depende de
    // permissão; atender é função que se dá a alguém, e por isso fica fora de todo padrão.
    public const string Suporte = "SUPORTE";

    public static readonly string[] All =
        [Solicitacoes, Aprovacao, Material, Estoque, Compras, Produtos, Fornecedores, CentrosCusto, Usuarios, Contratos, Compliance, Insights, PlanoAcao, Pdca, Suporte];

    /// <summary>Padrão por papel, aplicado quando o cadastro não define módulos.</summary>
    public static string[] DefaultsFor(string role) => role switch
    {
        Roles.SystemAdministrator => All,
        Roles.Requester => [Solicitacoes, Material],
        Roles.Approver => [Solicitacoes, Aprovacao],
        Roles.PurchasingOfficer => [Compras, Fornecedores, Estoque, Produtos],
        Roles.WarehouseOperator => [Estoque],
        Roles.WarehouseSupervisor => [Estoque, Produtos],
        Roles.SupplyManager => [Solicitacoes, Aprovacao, Material, Estoque, Compras, Produtos, Fornecedores, CentrosCusto, Compliance, Insights],
        Roles.Director => [Solicitacoes, Aprovacao, Material, Compras, Compliance, Insights],
        Roles.Auditor => [Solicitacoes, Estoque, Compras, Compliance, Insights],
        _ => [],
    };

    public static string[] EffectiveFor(User user)
    {
        if (user.Role == Roles.SystemAdministrator) return All; // admin sempre completo
        if (string.IsNullOrWhiteSpace(user.Modules)) return DefaultsFor(user.Role);
        return user.Modules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(All.Contains).Distinct().ToArray();
    }
}
