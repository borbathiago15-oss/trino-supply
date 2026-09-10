using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// O escalonamento: o gestor de suprimentos é avisado do que passou do prazo da etapa.
///
/// <para>
/// <b>Ele é avaliado quando o gestor abre a caixa</b>, e não por um relógio no servidor. Não é
/// preguiça: o projeto não tem agendador, e inventar um que roda de madrugada acrescentaria
/// uma peça que ninguém observa para entregar o mesmo recado — o gestor só lê quando abre a
/// caixa de qualquer forma. O que se perde é o aviso chegar antes de ele olhar; o que se
/// ganha é uma peça a menos que pode falhar em silêncio.
/// </para>
///
/// <para>
/// A chave de deduplicação é <c>PRAZO_ESTOURADO:solicitação:etapa</c>. Sem ela, cada visita à
/// caixa criaria de novo o aviso do mesmo atraso, e o gestor teria trinta linhas do mesmo
/// problema: repetir não faz o atraso ser atendido, faz a caixa ser ignorada. Com ela, o
/// mesmo item volta a avisar quando <b>muda de etapa</b> e estoura de novo — que é um fato
/// novo, e não o mesmo repetido.
/// </para>
/// </summary>
public class EscalonamentoDoPrazoService(AppDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Quantos itens estourados o escalonamento reporta de uma vez. O gestor precisa saber
    /// que existem, não receber uma linha para cada um de duzentos.
    /// </summary>
    public const int TetoDeAvisos = 20;

    /// <summary>
    /// Gera os avisos de estouro para os gestores, e devolve quantos nasceram agora.
    ///
    /// <para>
    /// O aviso nomeia <b>de quem</b> o item está esperando, porque "SC-2026-000123 estourou o
    /// prazo" manda o gestor abrir a Torre para descobrir o óbvio — e o dado já está na mão.
    /// </para>
    /// </summary>
    public async Task<int> AvaliarAsync(CancellationToken ct = default)
    {
        var avisos = new AvisoDoUsuarioService(db, clock);
        var gestores = await avisos.GestoresAsync(ct);
        if (gestores.Count == 0) return 0;   // sem gestor cadastrado não há a quem escalar

        var torre = new TorreDeControleService(db, clock);
        var pagina = await torre.ConsultarAsync(
            new FiltroTorre(SlaBreached: true, PageSize: TetoDeAvisos), ct);

        var antes = db.UserNotices.Local.Count;
        // uma solicitação com cinco itens estourados é um atraso, não cinco: o gestor age
        // sobre a SC, e cinco linhas iguais só fariam a caixa render menos
        foreach (var linha in pagina.Items.DistinctBy(i => (i.RequisitionId, i.Stage)))
        {
            var espera = linha.WaitingOn?.Who ?? "a etapa";
            var dias = linha.Sla?.Days;
            avisos.EnfileirarParaTodos(gestores, AvisoKinds.PrazoEstourado,
                $"{linha.PrNumber} passou do prazo em {linha.StageLabel}",
                $"Parado há {dias} dia(s) contra um prazo de {linha.Sla?.MaxDays}. "
                + $"Esperando: {espera}.",
                $"{AvisoKinds.PrazoEstourado}:{linha.RequisitionId}:{linha.Stage}",
                "/torre");
        }

        var novos = db.UserNotices.Local.Count - antes;
        if (novos > 0) await db.SaveChangesAsync(ct);
        return novos;
    }
}
