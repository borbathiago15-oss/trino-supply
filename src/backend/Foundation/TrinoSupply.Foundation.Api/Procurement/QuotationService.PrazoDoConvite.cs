using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// A situação de um convite: respondeu, está no prazo, atrasou, ou foi dispensado.
/// </summary>
/// <param name="DaysLate">
/// Dias corridos além do prazo. Zero enquanto o prazo não venceu — e nulo quando não há
/// prazo nenhum, porque "0 dias de atraso" sobre um convite sem data é afirmar pontualidade
/// que ninguém combinou.
/// </param>
public record SituacaoDoConvite(
    Guid SupplierId, string SupplierName, DateOnly? Deadline, bool Responded,
    bool Waived, string? WaivedReason, int Extensions, int? DaysLate)
{
    /// <summary>Atrasado é o convite vivo, sem proposta e com prazo vencido.</summary>
    public bool Late => !Responded && !Waived && DaysLate > 0;

    /// <summary>Ainda se espera algo deste fornecedor.</summary>
    public bool Pending => !Responded && !Waived;
}

public partial class QuotationService
{
    /// <summary>
    /// Como está cada convite deste processo.
    ///
    /// <para>
    /// É a resposta a "estou esperando quem?" — que o processo já sabia e não dizia: a tela
    /// listava os convidados e as propostas em lugares diferentes, e cruzar as duas listas
    /// para descobrir quem faltava era trabalho do comprador.
    /// </para>
    /// </summary>
    public static IReadOnlyList<SituacaoDoConvite> SituacaoDosConvites(Quotation q, DateOnly hoje)
    {
        var responderam = q.Proposals.Select(p => p.SupplierId).ToHashSet();
        return q.Suppliers.Select(s =>
        {
            var prazo = s.PrazoEfetivo(q.Deadline);
            var atraso = prazo is { } p ? Math.Max(0, hoje.DayNumber - p.DayNumber) : (int?)null;
            return new SituacaoDoConvite(
                s.SupplierId, s.SupplierName, prazo, responderam.Contains(s.SupplierId),
                s.WaivedAt is not null, s.WaivedReason, s.DeadlineExtensions, atraso);
        }).OrderByDescending(s => s.Late).ThenBy(s => s.SupplierName).ToList();
    }

    /// <summary>
    /// Estica o prazo de um convite.
    ///
    /// <para>
    /// É a saída honesta para o fornecedor que pediu mais tempo, e a única que devolve a ele
    /// o direito de propor: o prazo vencido barra a proposta (<c>RFQ-ERR-020</c>), então
    /// "dar novo prazo" precisa mesmo mexer na data, e não só no aviso da tela.
    /// </para>
    ///
    /// <para>
    /// O contador de esticadas fica: três prorrogações para o mesmo fornecedor é um fato
    /// sobre ele, e some se cada nova data apagar a anterior.
    /// </para>
    /// </summary>
    public async Task<(Quotation? q, UserError? error)> ProrrogarConviteAsync(
        Actor actor, Guid id, Guid supplierId, DateOnly novoPrazo, CancellationToken ct = default)
    {
        var (q, convite, erro) = await ConviteEditavelAsync(actor, id, supplierId, ct);
        if (erro is not null) return (null, erro);

        var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (novoPrazo < hoje)
            return (null, new("RFQ-ERR-071", "O novo prazo não pode ser no passado."));

        var anterior = convite!.PrazoEfetivo(q!.Deadline);
        if (anterior is { } antes && novoPrazo <= antes)
            return (null, new("RFQ-ERR-071",
                $"O prazo atual é {antes:dd/MM/yyyy} — um novo prazo precisa ser depois dele. "
                + "Para encurtar, siga sem o fornecedor."));

        convite.ResponseDeadline = novoPrazo;
        convite.DeadlineExtensions++;
        AddEvent(q, "PRAZO_PRORROGADO",
            $"Prazo de {convite.SupplierName} prorrogado para {novoPrazo:dd/MM/yyyy}"
            + (anterior is { } a ? $" (era {a:dd/MM/yyyy})." : "."), actor);

        await TouchAndSaveAsync(q, ct);
        return (q, null);
    }

    /// <summary>
    /// Segue sem este fornecedor: o convite continua no processo, marcado como dispensado.
    ///
    /// <para>
    /// <b>Não apaga o convite</b>, e é o ponto todo: um BID com dois proponentes por ter
    /// chamado dois é diferente de um BID com dois por ter chamado três e um não ter
    /// respondido. Apagar contaria a primeira história tendo acontecido a segunda.
    /// </para>
    ///
    /// <para>
    /// Quem já respondeu não se dispensa (<c>RFQ-ERR-072</c>): proposta na mesa é o oposto de
    /// ausência, e recusar a proposta recebida é decisão de adjudicação, não de prazo.
    /// </para>
    /// </summary>
    public async Task<(Quotation? q, UserError? error)> DispensarConviteAsync(
        Actor actor, Guid id, Guid supplierId, string motivo, CancellationToken ct = default)
    {
        var (q, convite, erro) = await ConviteEditavelAsync(actor, id, supplierId, ct);
        if (erro is not null) return (null, erro);

        if (q!.Proposals.Any(p => p.SupplierId == supplierId))
            return (null, new("RFQ-ERR-072",
                "Este fornecedor já enviou proposta — seguir sem ele agora seria descartar o que ele "
                + "propôs. Se a proposta não serve, escolha outro vencedor na adjudicação."));

        var texto = (motivo ?? "").Trim();
        if (texto.Length < 10)
            return (null, new("RFQ-ERR-072",
                "Diga por que o processo segue sem este fornecedor (mínimo 10 caracteres) — "
                + "é o que explica depois um BID com menos proponentes."));

        convite!.WaivedAt = clock.GetUtcNow();
        convite.WaivedByLabel = actor.Label;
        convite.WaivedReason = texto;
        AddEvent(q, "CONVITE_DISPENSADO",
            $"Processo segue sem {convite.SupplierName}: {texto}", actor);

        await TouchAndSaveAsync(q, ct);
        return (q, null);
    }

    /// <summary>O convite existe, o processo aceita mudança e quem pede conduz compras?</summary>
    private async Task<(Quotation? q, QuotationSupplier? convite, UserError? error)> ConviteEditavelAsync(
        Actor actor, Guid id, Guid supplierId, CancellationToken ct)
    {
        if (!CanConduct(actor.Role))
            return (null, null, new("RFQ-ERR-900", "Seu papel não conduz processos de compra."));

        var q = await GetAsync(id, ct);
        if (q is null) return (null, null, new("RFQ-ERR-404", "Cotação não encontrada."));
        if (q.Status is not (QuotationStatus.Open or QuotationStatus.Analysis) || q.WinnerSupplierId is not null)
            return (null, null, new("RFQ-ERR-070",
                "O processo já saiu da fase de cotação — o prazo dos convites não muda mais."));

        var convite = q.Suppliers.SingleOrDefault(s => s.SupplierId == supplierId);
        if (convite is null)
            return (null, null, new("RFQ-ERR-050", "Fornecedor não convidado para esta cotação."));
        if (convite.WaivedAt is not null)
            return (null, null, new("RFQ-ERR-070",
                $"O processo já seguiu sem {convite.SupplierName}. Convide-o de novo para reabrir o prazo."));

        return (q, convite, null);
    }
}
