namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>Os cinco números de comando do cockpit.</summary>
public record CockpitKpis(
    int ItensAtrasados, decimal TaxaRiscoPct,
    int BacklogTotalItens, decimal BacklogTotalValor,
    decimal SlaSemanalPct, decimal MetaSlaPct,
    decimal SavingMesTotal, decimal MetaSavingMes,
    decimal? OtifGeralPct, int OtifMedidos);

/// <summary>
/// Um nó da esteira. `HorasNaFila` é a espera do item <b>mais antigo</b> da etapa, e não a
/// média: a média esconde justamente o item parado há três dias, que é o que a TV precisa
/// mostrar. O gargalo sai daí — amarelo acima de 48h, vermelho acima de 72h.
/// </summary>
public record NoDaEsteira(string Etapa, string Rotulo, int Quantidade, int HorasNaFila, string Gargalo)
{
    public const string Normal = "NORMAL";
    public const string Atencao = "ATENCAO";
    public const string Critico = "CRITICO";
}

/// <summary>
/// Uma linha do radar de exceções. `Ordem` é a gravidade (menor = mais grave) e existe para
/// o critério de aceite 3: item urgente novo assume o topo sozinho, sem ninguém reordenar.
/// </summary>
public record ExcecaoDoCockpit(
    string Id, string TipoAlerta, string CodigoReferencia, string DescricaoItem,
    string UnidadeCentroCusto, string TempoRestanteOuAtraso, string ResponsavelNome, int Ordem)
{
    public const string AtrasoCritico = "ATRASO_CRITICO";
    public const string CotacaoVencendo = "COTACAO_VENCENDO";
    public const string PropostaUnica = "PROPOSTA_UNICA";
    public const string OcPendente = "OC_PENDENTE";
}

public record BurndownDoComprador(
    string CompradorNome, int AtendidosHoje, int TotalHoje, int PendenciasCriticas);

/// <summary>Uma descarga prevista para hoje na doca.</summary>
public record DescargaDoDia(
    string NumeroNfe, string FornecedorNome, string HorarioPrevisto, string StatusEntrega)
{
    public const string NoPrazo = "NO_PRAZO";
    public const string Atrasado = "ATRASADO";
    public const string Descarregando = "DESCARREGANDO";
}

public record CockpitResponse(
    DateTimeOffset SincronizadoEm,
    /// <summary>A unidade deste recorte; nulo é a visão geral (todas).</summary>
    string? Unidade,
    /// <summary>As unidades que existem, para a TV girar entre elas sozinha.</summary>
    IReadOnlyList<string> Unidades,
    CockpitKpis Kpis,
    IReadOnlyList<NoDaEsteira> Pipeline,
    IReadOnlyList<ExcecaoDoCockpit> ExcecoesCriticas,
    IReadOnlyList<BurndownDoComprador> BurndownCompradores,
    IReadOnlyList<DescargaDoDia> AgendaDocaHoje);
