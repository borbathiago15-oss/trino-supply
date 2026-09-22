namespace TrinoSupply.Foundation.Api.Domain;

/// <summary>
/// Setor da casa — RH, TI, Faturamento, Manutenção.
///
/// <para>
/// Não se confunde com centro de custo: o centro é <b>onde o dinheiro cai</b>, o setor é
/// <b>quem trabalha</b>. Um setor atende vários centros, e um centro é atendido por vários
/// setores. Misturar os dois faria "o plano do meu setor" virar "o plano do centro que eu
/// pago", que é outra pergunta.
/// </para>
///
/// <para>
/// Ele existe para o módulo de melhoria: o ciclo de um setor precisa aparecer para o colega
/// do mesmo setor, e sem esta entidade essa regra não tem como ser escrita.
/// </para>
/// </summary>
public class Sector
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Identidade curta e estável (ex.: RH, TI). Caixa alta, única.</summary>
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Setor fora de uso se inativa, nunca se apaga: as pessoas e os ciclos o citam.</summary>
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}
