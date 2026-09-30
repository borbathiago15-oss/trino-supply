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

/// <summary>
/// A vazão do dia: quanto trabalho entrou e quanto saiu, nos dois extremos do mesmo cano.
///
/// <para>
/// É a pergunta que o backlog sozinho não responde — "8 itens parados" não diz se o time
/// está ganhando ou perdendo terreno. <c>Saldo</c> positivo é backlog crescendo; negativo,
/// encolhendo. O sinal é a informação: o número absoluto não diz para que lado se anda.
/// </para>
///
/// <para>
/// A unidade é o <b>item</b>, como em toda a Torre: uma SC de cinco itens é cinco demandas,
/// e contá-la como uma esconderia quatro quintos do que entrou. <c>TaxaConclusaoPct</c> é nula
/// quando nada entrou — dividir por zero não dá 0%, dá pergunta sem sentido, e a parede não
/// deve fingir que a resposta é "não demos conta de nada".
/// </para>
/// </summary>
public record VazaoDoDia(int EntraramHoje, int ConcluidosHoje, int Saldo, decimal? TaxaConclusaoPct);

/// <summary>
/// O bloco do almoxarifado: a solicitação de material, que é o outro cano da casa. A parede
/// fica na sala de suprimentos, e o atendimento de material acontece ali do lado.
///
/// <para>
/// A unidade aqui é a <b>solicitação</b>, e não o item como no resto da Torre — de propósito.
/// <c>FulfillAsync</c> atende a solicitação inteira num ato: o almoxarife separa a lista toda
/// e fecha o atendimento de uma vez. Contar em item diria quanto se tira da prateleira, que é
/// informação de apoio (<c>FilaItens</c>), não o tamanho da fila de trabalho.
/// </para>
///
/// <para>
/// <b>Duas filas, e o que as separa é de quem é a vez.</b> <c>Submitted</c> espera o Nível 1 do
/// centro de custo e o estoque não pode fazer nada com ela; <c>Approved</c> espera o almoxarifado.
/// Somá-las cobraria do almoxarife trabalho que não é dele — é o mesmo erro que separou
/// "em faturamento" de "aguardando recebimento" na Torre.
/// </para>
///
/// <para>
/// <c>AtendidoPeloEstoquePct</c> é da <b>quantidade</b>, não da solicitação: entregar 8 de 10
/// unidades é 80% atendido, e contar a solicitação inteira como "não atendida" esconderia as
/// oito que saíram. Nulo quando nada foi atendido no mês — 0% diria que o estoque estava vazio.
/// <c>HorasMediaAtendimento</c> é nulo pelo mesmo motivo, e deixa de fora o atendimento sem as
/// duas marcas: a mesma regra da Torre, de nunca usar uma data que não seja a da própria etapa.
/// </para>
/// </summary>
public record AlmoxarifadoDoCockpit(
    int FilaSolicitacoes, int FilaItens, int HorasDoMaisAntigo, string Gargalo, string? MaisAntigaNumero,
    int AguardandoAprovacao, int AtendidasHoje,
    decimal? AtendidoPeloEstoquePct, int ViraramCompraNoMes, decimal? HorasMediaAtendimento);

public record CockpitResponse(
    DateTimeOffset SincronizadoEm,
    /// <summary>A unidade deste recorte; nulo é a visão geral (todas).</summary>
    string? Unidade,
    /// <summary>As unidades que existem, para a TV girar entre elas sozinha.</summary>
    IReadOnlyList<string> Unidades,
    CockpitKpis Kpis,
    VazaoDoDia Vazao,
    AlmoxarifadoDoCockpit Almoxarifado,
    IReadOnlyList<NoDaEsteira> Pipeline,
    IReadOnlyList<ExcecaoDoCockpit> ExcecoesCriticas,
    IReadOnlyList<BurndownDoComprador> BurndownCompradores,
    IReadOnlyList<DescargaDoDia> AgendaDocaHoje);
