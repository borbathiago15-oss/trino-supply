using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// Um aviso endereçado a uma pessoa.
///
/// <para>
/// A Central de Avisos que já existe é <b>derivada</b>: ela conta o que está aberto e o número
/// muda sozinho quando o trabalho anda. Serve para "o que há para eu fazer agora", e não para
/// "o que aconteceu comigo": um contador não avisa que a SC <i>passou</i> a ser sua, porque
/// quando você olha ele já é outro número. Este registro é o que faltava — o aviso como
/// <b>fato datado, com dono e com lido</b>.
/// </para>
///
/// <para>
/// As duas coisas convivem de propósito. A contagem derivada some quando o trabalho é feito,
/// que é o comportamento certo para uma fila; o aviso fica até alguém o marcar como lido, que
/// é o comportamento certo para um recado.
/// </para>
/// </summary>
public class UserNotice
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Quem recebe. Um aviso tem um dono só: "para todos" é comunicado, não aviso.</summary>
    public Guid UserId { get; set; }

    /// <summary>O que aconteceu — ver <see cref="AvisoDoUsuarioService"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    /// <summary>Para onde a tela leva quem clicar. Nulo quando não há destino.</summary>
    public string? Link { get; set; }

    /// <summary>
    /// A chave que impede o mesmo aviso de nascer duas vezes: <c>tipo:documento:etapa</c>.
    ///
    /// <para>
    /// Sem ela, o escalonamento — que é avaliado toda vez que alguém abre a caixa — criaria um
    /// aviso novo do mesmo atraso a cada visita, e o gestor teria trinta linhas do mesmo
    /// problema. Repetir o aviso não faz o atraso ser mais atendido; faz a caixa ser ignorada.
    /// </para>
    /// </summary>
    public string DedupeKey { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>Os tipos de aviso, e o que cada um significa.</summary>
public static class AvisoKinds
{
    /// <summary>1 — chegou demanda sem comprador: o gestor precisa atribuir.</summary>
    public const string DemandaParaTriar = "DEMANDA_PARA_TRIAR";
    /// <summary>2 — a SC passou a ser do comprador.</summary>
    public const string DemandaAtribuida = "DEMANDA_ATRIBUIDA";
    /// <summary>3 — o processo chegou ao Nível 1.</summary>
    public const string AprovacaoNivel1 = "APROVACAO_NIVEL_1";
    /// <summary>4 — o processo chegou ao Nível 2.</summary>
    public const string AprovacaoNivel2 = "APROVACAO_NIVEL_2";
    /// <summary>5 — aprovado: volta ao comprador para registrar a O.C. do ERP.</summary>
    public const string LiberadoParaOc = "LIBERADO_PARA_OC";
    /// <summary>O orçamento da SC está pronto: o comprador escolheu e o processo parou para quem pediu.</summary>
    public const string OrcamentoApresentado = "ORCAMENTO_APRESENTADO";
    /// <summary>Escalonamento: algo passou do prazo da etapa e o gestor precisa saber.</summary>
    public const string PrazoEstourado = "PRAZO_ESTOURADO";
}

/// <summary>
/// Cria e lê os avisos endereçados.
///
/// <para>
/// <b>Nada aqui interrompe o fluxo.</b> Um aviso que falha ao ser gravado não pode impedir
/// uma aprovação de acontecer — o recado é sobre o fato, e o fato é mais importante que o
/// recado. Por isso a emissão entra na mesma transação de quem a chama, e nunca lança.
/// </para>
/// </summary>
public class AvisoDoUsuarioService(AppDbContext db, TimeProvider clock)
{
    /// <summary>Teto do que a caixa devolve — caixa infinita é caixa que ninguém termina de ler.</summary>
    public const int Teto = 100;

    /// <summary>
    /// Enfileira um aviso, se ele ainda não existe.
    ///
    /// <para>
    /// <b>Não salva.</b> Quem chama decide quando gravar, para o aviso entrar na mesma
    /// transação do fato que o gerou: aviso gravado e aprovação perdida seria pior do que
    /// nenhum aviso.
    /// </para>
    /// </summary>
    public void Enfileirar(
        Guid destinatario, string tipo, string titulo, string corpo, string chave, string? link = null)
    {
        if (destinatario == Guid.Empty) return;
        // já enfileirado nesta mesma transação, ou já no banco: não duplica
        var jaExiste = db.UserNotices.Local.Any(n => n.UserId == destinatario && n.DedupeKey == chave)
            || db.UserNotices.Any(n => n.UserId == destinatario && n.DedupeKey == chave);
        if (jaExiste) return;

        db.UserNotices.Add(new UserNotice
        {
            UserId = destinatario, Kind = tipo, Title = titulo, Body = corpo,
            Link = link, DedupeKey = chave, CreatedAt = clock.GetUtcNow(),
        });
    }

    /// <summary>
    /// O mesmo aviso para várias pessoas — os aprovadores de um nível, por exemplo.
    ///
    /// <para>
    /// Cada um recebe o seu: marcar como lido é decisão de quem leu, e um aviso compartilhado
    /// sumiria da caixa dos outros quando o primeiro o lesse.
    /// </para>
    /// </summary>
    public void EnfileirarParaTodos(
        IEnumerable<Guid> destinatarios, string tipo, string titulo, string corpo,
        string chave, string? link = null)
    {
        foreach (var id in destinatarios.Distinct())
            Enfileirar(id, tipo, titulo, corpo, chave, link);
    }

    /// <summary>Os avisos de uma pessoa, do mais novo para o mais velho.</summary>
    public async Task<IReadOnlyList<UserNotice>> DaPessoaAsync(
        Guid userId, bool somenteNaoLidos = false, CancellationToken ct = default) =>
        await db.UserNotices.AsNoTracking()
            .Where(n => n.UserId == userId && (!somenteNaoLidos || n.ReadAt == null))
            .OrderByDescending(n => n.CreatedAt)
            .Take(Teto)
            .ToListAsync(ct);

    public async Task<int> NaoLidosAsync(Guid userId, CancellationToken ct = default) =>
        await db.UserNotices.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);

    /// <summary>Marca um aviso como lido. Só o dono do aviso o marca.</summary>
    public async Task<bool> MarcarLidoAsync(Guid userId, Guid aviso, CancellationToken ct = default)
    {
        var n = await db.UserNotices.SingleOrDefaultAsync(x => x.Id == aviso && x.UserId == userId, ct);
        if (n is null) return false;
        n.ReadAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Marca tudo como lido — o gesto de quem já olhou a caixa inteira.</summary>
    public async Task<int> MarcarTudoLidoAsync(Guid userId, CancellationToken ct = default)
    {
        var agora = clock.GetUtcNow();
        var pendentes = await db.UserNotices.Where(n => n.UserId == userId && n.ReadAt == null).ToListAsync(ct);
        foreach (var n in pendentes) n.ReadAt = agora;
        await db.SaveChangesAsync(ct);
        return pendentes.Count;
    }

    /// <summary>Quem são os gestores de suprimentos — o destino do escalonamento.</summary>
    public async Task<IReadOnlyList<Guid>> GestoresAsync(CancellationToken ct = default) =>
        await db.Users.Where(u => u.Active && u.Role == Roles.SupplyManager)
            .Select(u => u.Id).ToListAsync(ct);
}
