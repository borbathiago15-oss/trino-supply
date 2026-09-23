namespace TrinoSupply.Foundation.Api.Suporte;

/// <summary>
/// A situação do chamado diz <b>de quem é a vez</b>, e não só "aberto ou fechado".
///
/// <para>
/// Um chamado aberto não conta a história inteira: aberto esperando o suporte e aberto
/// esperando a resposta de quem pediu são filas diferentes, com donos diferentes. É a mesma
/// pergunta que a Torre responde para a compra, e a resposta é a mesma: a situação diz com
/// quem está a bola.
/// </para>
/// </summary>
public static class SituacaoDoChamado
{
    /// <summary>Novo, ou quem abriu respondeu: o suporte precisa agir.</summary>
    public const string AguardandoSuporte = "AGUARDANDO_SUPORTE";

    /// <summary>O suporte respondeu e a vez é de quem abriu.</summary>
    public const string AguardandoUsuario = "AGUARDANDO_USUARIO";

    /// <summary>Encerrado. Uma mensagem nova reabre — o chamado não precisa nascer de novo.</summary>
    public const string Resolvido = "RESOLVIDO";

    public static readonly string[] Todas = [AguardandoSuporte, AguardandoUsuario, Resolvido];
}

/// <summary>Do que se trata o chamado — é o que deixa a fila ser lida de relance.</summary>
public static class CategoriaDoChamado
{
    public const string Duvida = "DUVIDA";
    public const string Erro = "ERRO";
    public const string Acesso = "ACESSO";
    public const string Sugestao = "SUGESTAO";

    public static readonly string[] Todas = [Duvida, Erro, Acesso, Sugestao];
}

/// <summary>
/// Um pedido de ajuda, aberto de dentro da tela onde a dúvida apareceu.
///
/// <para>
/// A <see cref="Screen"/> é gravada no momento da abertura porque é o dado que quem abre o
/// chamado nunca lembra de escrever e quem atende sempre precisa: "não consigo aprovar"
/// quer dizer coisas diferentes na Central de Aprovação e na tela do processo.
/// </para>
/// </summary>
public class SupportTicket
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary><c>CH-ano-sequência</c>. É o que se fala ao telefone.</summary>
    public string Number { get; set; } = string.Empty;

    public string Status { get; set; } = SituacaoDoChamado.AguardandoSuporte;
    public string Category { get; set; } = CategoriaDoChamado.Duvida;
    public string Subject { get; set; } = string.Empty;

    /// <summary>A rota de onde o chamado foi aberto (<c>/cotacoes/…</c>).</summary>
    public string Screen { get; set; } = string.Empty;

    /// <summary>O nome da tela, como o menu o chama — é o que o atendente lê.</summary>
    public string ScreenLabel { get; set; } = string.Empty;

    /// <summary>Navegador e tamanho de tela: o que explica o defeito que só acontece "aqui".</summary>
    public string? ClientInfo { get; set; }

    public Guid CreatedById { get; set; }
    public string CreatedByLabel { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Quem atende. Preenchido pela primeira resposta do suporte: responder é assumir.</summary>
    public Guid? AssignedToId { get; set; }
    public string? AssignedToLabel { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedByLabel { get; set; }

    public List<SupportTicketMessage> Messages { get; set; } = [];
}

/// <summary>
/// Uma fala do chamado. A descrição da abertura é a primeira delas: a conversa inteira fica
/// num lugar só, na ordem em que aconteceu.
/// </summary>
public class SupportTicketMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    public Guid AuthorId { get; set; }
    public string AuthorLabel { get; set; } = string.Empty;

    /// <summary>Escrita por quem atende. É o que decide para quem vai a bola.</summary>
    public bool FromSupport { get; set; }

    public string Text { get; set; } = string.Empty;

    /// <summary>O print ou arquivo que acompanha a mensagem, em <c>stored_document</c>.</summary>
    public Guid? AttachmentId { get; set; }
    public string? AttachmentName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
