namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// Uma etapa do ciclo do processo, como a tela a mostra.
/// </summary>
/// <param name="Chave">Identidade estável da etapa, para a tela e para o teste.</param>
/// <param name="Situacao"><c>feita</c>, <c>atual</c>, <c>pendente</c> ou <c>encerrada</c>.</param>
/// <param name="Quem">Quem fez (etapa feita) ou de quem se espera (atual e pendente).</param>
/// <param name="Em">Quando foi feita, ou desde quando se espera. Nulo quando não há marca honesta.</param>
/// <param name="SemAprovador">O centro não tem ninguém cadastrado neste nível — a tela aponta onde consertar.</param>
public record EtapaDoCaminho(
    string Chave, string Titulo, string Situacao, string? Quem, DateTimeOffset? Em, bool SemAprovador = false);

/// <summary>
/// O caminho do processo: quem pediu, o que já aconteceu, de quem se espera agora e o que
/// ainda falta — tudo numa lista só, para a tela do processo.
///
/// <para>
/// "Aguardando Aprovador 01" dizia a etapa e escondia o resto: quem é o aprovador, quem
/// pediu, quantas etapas faltam. O sistema sabia tudo isso — os aprovadores estão no centro
/// de custo, o solicitante na SC, as datas no próprio processo. A Torre já responde
/// "de quem está esperando" (<see cref="TorreDeControleService.EsperaDe"/>); aqui a mesma
/// resposta vem junto das etapas anteriores e das seguintes, porque quem abre o processo
/// quer ver o caminho inteiro, não só o passo.
/// </para>
///
/// <para>
/// Etapa <b>pendente</b> também diz quem vai aprovar: saber de antemão que o Nível 2 é do
/// diretor X é o que permite avisá-lo antes de a bola chegar. Centro <b>sem aprovador
/// cadastrado</b> é dito na linha: fila que nunca vai andar é o defeito que precisa aparecer,
/// não passar por espera normal.
/// </para>
/// </summary>
public static class CaminhoDoProcesso
{
    public const string Feita = "feita";
    public const string Atual = "atual";
    public const string Pendente = "pendente";
    public const string Encerrada = "encerrada";

    public static IReadOnlyList<EtapaDoCaminho> De(
        Quotation q, IReadOnlyList<string> solicitantes, DateTimeOffset? pedidaEm, AlcadasDoCentro alcadas)
    {
        var s = q.Status;
        var viva = s is not (QuotationStatus.Rejected or QuotationStatus.Cancelled);

        // Num processo encerrado a situação não diz onde ele parou; as datas dizem.
        var cotacaoFeita = viva ? s >= QuotationStatus.Analysis : q.SelectedAt is not null;
        var comprador = string.IsNullOrWhiteSpace(q.CreatedByLabel) ? "comprador" : q.CreatedByLabel;

        var etapas = new List<EtapaDoCaminho>
        {
            new("solicitacao", "Solicitação de compra", Feita, Nomes(solicitantes), pedidaEm),
            Etapa("cotacao", "Cotação — convites e propostas",
                feita: cotacaoFeita, atual: viva && s == QuotationStatus.Open, quem: comprador, em: q.CreatedAt),
            Etapa("escolha", "Escolha do fornecedor vencedor",
                feita: q.SelectedAt is not null, atual: viva && s == QuotationStatus.Analysis,
                quem: q.SelectedAt is null ? comprador : q.SelectedByLabel, em: q.SelectedAt),
            Alcada("nivel1", "Aprovação de Nível 1", q.ManagerApprovedAt, q.ManagerApprovedByLabel,
                atual: viva && s == QuotationStatus.AwaitingManager, desde: q.SelectedAt, alcadas.Nivel1),
            Alcada("nivel2", "Aprovação de Nível 2", q.DirectorApprovedAt, q.DirectorApprovedByLabel,
                atual: viva && s == QuotationStatus.AwaitingDirector, desde: q.ManagerApprovedAt, alcadas.Nivel2),
            Etapa("oc", "Registro da O.C. do ERP",
                feita: s == QuotationStatus.PoIssued, atual: viva && s == QuotationStatus.ApprovedForIssue,
                quem: s == QuotationStatus.ApprovedForIssue ? comprador : null,
                em: q.DirectorApprovedAt ?? q.ManagerApprovedAt),
            Etapa("entrega", "Faturamento e recebimento",
                feita: false, atual: viva && s == QuotationStatus.PoIssued, quem: null, em: null),
        };

        if (!viva)
            etapas.Add(new("encerrado", s == QuotationStatus.Rejected ? "Rejeitado" : "Cancelada", Encerrada, null, null));

        return etapas;
    }

    private static EtapaDoCaminho Etapa(
        string chave, string titulo, bool feita, bool atual, string? quem, DateTimeOffset? em) =>
        new(chave, titulo, feita ? Feita : atual ? Atual : Pendente, quem, feita || atual ? em : null);

    private static EtapaDoCaminho Alcada(
        string chave, string titulo, DateTimeOffset? feitaEm, string? feitaPor,
        bool atual, DateTimeOffset? desde, IReadOnlyList<string> aprovadores)
    {
        if (feitaEm is not null) return new(chave, titulo, Feita, feitaPor, feitaEm);
        var ninguem = aprovadores.Count == 0;
        var quem = ninguem ? "sem aprovador cadastrado no centro" : string.Join(", ", aprovadores);
        return new(chave, titulo, atual ? Atual : Pendente, quem, atual ? desde : null, SemAprovador: ninguem);
    }

    private static string? Nomes(IReadOnlyList<string> nomes)
    {
        var lista = nomes.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
        return lista.Count == 0 ? null : string.Join(", ", lista);
    }
}
