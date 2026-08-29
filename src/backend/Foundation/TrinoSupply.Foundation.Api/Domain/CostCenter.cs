namespace TrinoSupply.Foundation.Api.Domain;

/// <summary>
/// Centro de custo (master data mínimo, antecipação do FD-001-09): além do código,
/// carrega as dimensões gerenciais dos dashboards — regional, gerente e cliente.
/// Requisições e solicitações referenciam o código (texto), preservando o histórico.
/// </summary>
public class CostCenter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;        // único, caixa alta (ex.: CC-ADM-001)
    public string Name { get; set; } = string.Empty;
    public string? Region { get; set; }                     // regional (ex.: NORDESTE, SP-CAPITAL)
    public Guid? CompanyId { get; set; }                    // CNPJ do grupo responsável por este CC
    public Guid? ManagerUserId { get; set; }                // vínculo antigo: gerente responsável
    public string? ManagerName { get; set; }                // snapshot do nome do gerente
    public List<CostCenterApprover> Approvers { get; set; } = [];   // alçadas por nível (1 e 2)
    // limite de valor opcional por nível (V2-P3 — decisão C8): MEDE, não bloqueia.
    // Processo aprovado acima do limite do nível vira penalidade no Compliance (−10).
    public decimal? Level1ValueLimit { get; set; }
    public decimal? Level2ValueLimit { get; set; }
    public string? ClientName { get; set; }                 // cliente/contrato atendido
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public int Version { get; set; } = 1;
}
