using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Suporte;

/// <summary>Quem está do outro lado da chamada — e se essa pessoa atende o suporte.</summary>
public record QuemChama(Guid Id, string Nome, bool Atende);

public record AbrirChamado(
    string? Category, string? Subject, string? Description,
    string? Screen, string? ScreenLabel, string? ClientInfo);

/// <summary>
/// O chamado de suporte, aberto de qualquer tela.
///
/// <para>
/// <b>Abrir não pede módulo nem papel.</b> Pedir ajuda é a única porta que não pode depender
/// de permissão: quem não consegue entrar numa tela é justamente quem mais precisa dela.
/// <b>Atender</b> é do administrador e de quem recebeu o módulo <c>SUPORTE</c> — que, como o
/// plano de ação, não entra em padrão de papel nenhum: é função que se dá a alguém, não cargo.
/// </para>
///
/// <para>
/// Quem não atende enxerga só os próprios chamados, e o chamado alheio responde 404, não 403:
/// um número de chamado não pode servir para descobrir que o chamado de outra pessoa existe.
/// </para>
/// </summary>
public class ChamadoService(AppDbContext db, TimeProvider clock, AvisoDoUsuarioService avisos)
{
    public const int AssuntoMinimo = 5;
    public const int AssuntoMaximo = 150;
    public const int DescricaoMinima = 10;
    public const int TextoMaximo = 4000;
    public const int Teto = 200;

    public const string AvisoAberto = "SUPORTE_CHAMADO_ABERTO";
    public const string AvisoResposta = "SUPORTE_RESPOSTA";

    /// <summary>O <c>entity_type</c> do anexo em <c>stored_document</c>; o id é o do chamado.</summary>
    public const string TipoDoAnexo = "SUPPORT_TICKET";

    public static bool Atende(User u) =>
        u.Active && AppModules.EffectiveFor(u).Contains(AppModules.Suporte);

    public static bool Atende(string role, IReadOnlyCollection<string> modulos) =>
        role == Roles.SystemAdministrator || modulos.Contains(AppModules.Suporte);

    // ----------------------------------------------------------------------- abrir

    public async Task<(SupportTicket? chamado, UserError? erro)> AbrirAsync(
        QuemChama quem, AbrirChamado pedido, CancellationToken ct = default)
    {
        var categoria = (pedido.Category ?? CategoriaDoChamado.Duvida).Trim().ToUpperInvariant();
        if (!CategoriaDoChamado.Todas.Contains(categoria))
            return (null, new("CH-ERR-012", "Categoria inválida: escolha dúvida, erro, acesso ou sugestão."));
        var assunto = (pedido.Subject ?? "").Trim();
        if (assunto.Length < AssuntoMinimo)
            return (null, new("CH-ERR-010", $"Escreva o assunto em pelo menos {AssuntoMinimo} caracteres."));
        if (assunto.Length > AssuntoMaximo)
            return (null, new("CH-ERR-010", $"O assunto passa de {AssuntoMaximo} caracteres: o detalhe vai na descrição."));
        var descricao = (pedido.Description ?? "").Trim();
        if (descricao.Length < DescricaoMinima)
            return (null, new("CH-ERR-011",
                $"Descreva o que aconteceu em pelo menos {DescricaoMinima} caracteres — é o que o suporte vai ler primeiro."));
        if (descricao.Length > TextoMaximo)
            return (null, new("CH-ERR-011", $"A descrição passa de {TextoMaximo} caracteres."));

        var agora = clock.GetUtcNow();
        var chamado = new SupportTicket
        {
            Number = await ProximoNumeroAsync(agora, ct),
            Category = categoria,
            Subject = assunto,
            Screen = Cortar((pedido.Screen ?? "").Trim(), 300),
            ScreenLabel = Cortar(string.IsNullOrWhiteSpace(pedido.ScreenLabel) ? "Geral" : pedido.ScreenLabel.Trim(), 150),
            ClientInfo = string.IsNullOrWhiteSpace(pedido.ClientInfo) ? null : Cortar(pedido.ClientInfo.Trim(), 300),
            CreatedById = quem.Id, CreatedByLabel = quem.Nome,
            CreatedAt = agora, UpdatedAt = agora,
        };
        db.SupportTickets.Add(chamado);
        // pelo DbSet, e não pela navegação: filho com Id preenchido anexado pela coleção de um
        // pai rastreado vai ao banco como update de linha que nunca existiu
        db.SupportTicketMessages.Add(new SupportTicketMessage
        {
            TicketId = chamado.Id, AuthorId = quem.Id, AuthorLabel = quem.Nome,
            FromSupport = false, Text = descricao, CreatedAt = agora,
        });

        // cada atendente recebe o seu aviso; quem abriu e também atende não avisa a si mesmo
        var atendentes = (await AtendentesAsync(ct)).Where(id => id != quem.Id);
        avisos.EnfileirarParaTodos(atendentes, AvisoAberto,
            $"Chamado {chamado.Number} — {chamado.ScreenLabel}",
            $"{quem.Nome}: {assunto}",
            $"CHAMADO:{chamado.Id}:ABERTO", $"/suporte/{chamado.Id}");

        await db.SaveChangesAsync(ct);
        return (chamado, null);
    }

    // ----------------------------------------------------------------------- ler

    /// <summary>
    /// A lista de quem pede: os chamados da própria pessoa. A de quem atende (<paramref name="fila"/>)
    /// é a de todo mundo — e só atendente a vê.
    /// </summary>
    public async Task<IReadOnlyList<SupportTicket>> ListarAsync(
        QuemChama quem, bool fila, string? situacao, CancellationToken ct = default)
    {
        var q = db.SupportTickets.AsNoTracking().AsQueryable();
        if (!fila || !quem.Atende) q = q.Where(t => t.CreatedById == quem.Id);
        if (!string.IsNullOrWhiteSpace(situacao)) q = q.Where(t => t.Status == situacao);
        return await q.OrderByDescending(t => t.UpdatedAt).Take(Teto).ToListAsync(ct);
    }

    /// <summary>
    /// Os dois números do cabeçalho. <c>ParaMim</c> é o chamado meu que o suporte respondeu —
    /// a vez é minha; <c>Fila</c> é o que espera o suporte, e só existe para quem atende.
    ///
    /// <para>
    /// A fila <b>não conta o chamado do próprio atendente</b>: no chamado dele ele é quem pediu,
    /// e contá-lo diria "há trabalho esperando você" sobre um pedido que é dele esperar.
    /// </para>
    /// </summary>
    public async Task<(int ParaMim, int? Fila)> ResumoAsync(QuemChama quem, CancellationToken ct = default)
    {
        var paraMim = await db.SupportTickets.CountAsync(
            t => t.CreatedById == quem.Id && t.Status == SituacaoDoChamado.AguardandoUsuario, ct);
        int? fila = quem.Atende
            ? await db.SupportTickets.CountAsync(
                t => t.Status == SituacaoDoChamado.AguardandoSuporte && t.CreatedById != quem.Id, ct)
            : null;
        return (paraMim, fila);
    }

    public async Task<SupportTicket?> AbrirParaLerAsync(QuemChama quem, Guid id, CancellationToken ct = default)
    {
        var chamado = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        if (chamado is null || (!quem.Atende && chamado.CreatedById != quem.Id)) return null;
        chamado.Messages = await db.SupportTicketMessages.AsNoTracking()
            .Where(m => m.TicketId == id).OrderBy(m => m.CreatedAt).ToListAsync(ct);
        return chamado;
    }

    /// <summary>Quem pode ver o chamado — a mesma régua da leitura, usada pelo download do anexo.</summary>
    public async Task<bool> PodeVerAsync(QuemChama quem, Guid chamadoId, CancellationToken ct = default) =>
        quem.Atende || await db.SupportTickets.AnyAsync(t => t.Id == chamadoId && t.CreatedById == quem.Id, ct);

    // ----------------------------------------------------------------------- conversar

    /// <summary>
    /// Uma mensagem nova, que pode também encerrar o chamado.
    ///
    /// <para>
    /// A mensagem move a bola: a do suporte a passa para quem abriu, a de quem abriu a devolve
    /// ao suporte. Isso vale também para o chamado resolvido — responder reabre, porque "não
    /// resolveu" dito dentro do chamado vale mais que um chamado novo sem a história.
    /// </para>
    ///
    /// <para>
    /// <b>O suporte não resolve em silêncio</b> (<c>CH-ERR-021</c>): resolver sem dizer o que foi
    /// feito deixa quem pediu sem saber se o problema acabou ou se foi só a fila que andou.
    /// Quem abriu, ao contrário, pode encerrar sem texto — "resolvi sozinho" é resposta bastante.
    /// </para>
    /// </summary>
    public async Task<(SupportTicketMessage? mensagem, SupportTicket? chamado, UserError? erro)> ResponderAsync(
        QuemChama quem, Guid id, string? texto, bool resolver, CancellationToken ct = default)
    {
        var chamado = await db.SupportTickets.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (chamado is null || (!quem.Atende && chamado.CreatedById != quem.Id))
            return (null, null, new("CH-ERR-404", "Chamado não encontrado."));

        // no próprio chamado, quem abriu fala como quem pediu — mesmo que também atenda
        var doSuporte = quem.Atende && chamado.CreatedById != quem.Id;
        var limpo = (texto ?? "").Trim();
        if (limpo.Length > TextoMaximo)
            return (null, null, new("CH-ERR-020", $"A mensagem passa de {TextoMaximo} caracteres."));
        if (limpo.Length == 0 && !resolver)
            return (null, null, new("CH-ERR-020", "Escreva a mensagem."));
        if (resolver && doSuporte && limpo.Length < DescricaoMinima)
            return (null, null, new("CH-ERR-021",
                "Diga o que foi feito antes de resolver: quem abriu o chamado precisa saber se o problema acabou."));
        if (resolver && chamado.Status == SituacaoDoChamado.Resolvido)
            return (null, null, new("CH-ERR-022", "Este chamado já está resolvido."));

        var agora = clock.GetUtcNow();
        SupportTicketMessage? mensagem = null;
        if (limpo.Length > 0)
        {
            mensagem = new SupportTicketMessage
            {
                TicketId = chamado.Id, AuthorId = quem.Id, AuthorLabel = quem.Nome,
                FromSupport = doSuporte, Text = limpo, CreatedAt = agora,
            };
            db.SupportTicketMessages.Add(mensagem);
        }

        if (doSuporte && chamado.AssignedToId is null)
        {
            // responder é assumir: a fila passa a dizer quem está com o chamado
            chamado.AssignedToId = quem.Id;
            chamado.AssignedToLabel = quem.Nome;
        }

        if (resolver)
        {
            chamado.Status = SituacaoDoChamado.Resolvido;
            chamado.ResolvedAt = agora;
            chamado.ResolvedByLabel = quem.Nome;
        }
        else
        {
            chamado.Status = doSuporte ? SituacaoDoChamado.AguardandoUsuario : SituacaoDoChamado.AguardandoSuporte;
            // reabrir apaga o veredito: quem resolveu e quando não valem mais
            chamado.ResolvedAt = null;
            chamado.ResolvedByLabel = null;
        }
        chamado.UpdatedAt = agora;

        var chave = $"CHAMADO:{chamado.Id}:{mensagem?.Id.ToString() ?? "RESOLVIDO"}";
        if (doSuporte)
        {
            avisos.Enfileirar(chamado.CreatedById, AvisoResposta,
                resolver ? $"Chamado {chamado.Number} resolvido" : $"Resposta no chamado {chamado.Number}",
                $"{quem.Nome}: {Resumo(limpo)}", chave, $"/suporte/{chamado.Id}");
        }
        else
        {
            // quem pediu respondeu: avisa quem está com o chamado, ou todos, se ninguém o assumiu
            IReadOnlyList<Guid> destino = chamado.AssignedToId is { } dono ? [dono] : await AtendentesAsync(ct);
            avisos.EnfileirarParaTodos(destino.Where(d => d != quem.Id), AvisoResposta,
                resolver ? $"Chamado {chamado.Number} encerrado por quem abriu" : $"Resposta no chamado {chamado.Number}",
                $"{quem.Nome}: {(limpo.Length > 0 ? Resumo(limpo) : "encerrou o chamado.")}",
                chave, $"/suporte/{chamado.Id}");
        }

        await db.SaveChangesAsync(ct);
        return (mensagem, chamado, null);
    }

    /// <summary>
    /// O anexo de uma mensagem — o print de onde a dúvida apareceu. Só quem escreveu a mensagem
    /// anexa a ela (<c>CH-ERR-030</c>), e uma vez só: trocar o print depois da resposta faria a
    /// conversa responder a uma imagem que não está mais lá.
    /// </summary>
    public async Task<(SupportTicketMessage? mensagem, UserError? erro)> MensagemParaAnexarAsync(
        QuemChama quem, Guid chamadoId, Guid mensagemId, CancellationToken ct = default)
    {
        var mensagem = await db.SupportTicketMessages
            .SingleOrDefaultAsync(m => m.Id == mensagemId && m.TicketId == chamadoId, ct);
        if (mensagem is null || !await PodeVerAsync(quem, chamadoId, ct))
            return (null, new("CH-ERR-404", "Mensagem não encontrada."));
        if (mensagem.AuthorId != quem.Id)
            return (null, new("CH-ERR-030", "Só quem escreveu a mensagem anexa a ela."));
        if (mensagem.AttachmentId is not null)
            return (null, new("CH-ERR-031", "Esta mensagem já tem um anexo. Envie outro numa mensagem nova."));
        return (mensagem, null);
    }

    // ----------------------------------------------------------------------- apoio

    /// <summary>
    /// Quem atende, para os avisos. O filtro no banco só estreita (o texto dos módulos contém
    /// SUPORTE, ou é administrador); a palavra final é de <see cref="AppModules.EffectiveFor"/>,
    /// a mesma régua que o resto do sistema usa para módulo.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> AtendentesAsync(CancellationToken ct = default)
    {
        var candidatos = await db.Users
            .Where(u => u.Active && (u.Role == Roles.SystemAdministrator
                || (u.Modules != null && u.Modules.Contains(AppModules.Suporte))))
            .ToListAsync(ct);
        return candidatos.Where(Atende).Select(u => u.Id).ToList();
    }

    private async Task<string> ProximoNumeroAsync(DateTimeOffset agora, CancellationToken ct)
    {
        var prefixo = $"CH-{agora.Year}-";
        var usados = await db.SupportTickets.Where(t => t.Number.StartsWith(prefixo))
            .Select(t => t.Number).ToListAsync(ct);
        var maior = usados
            .Select(n => int.TryParse(n[prefixo.Length..], out var v) ? v : 0)
            .DefaultIfEmpty(0).Max();
        return $"{prefixo}{maior + 1:000000}";
    }

    private static string Resumo(string texto) => texto.Length <= 160 ? texto : texto[..157] + "…";
    private static string Cortar(string s, int max) => s.Length <= max ? s : s[..max];
}
