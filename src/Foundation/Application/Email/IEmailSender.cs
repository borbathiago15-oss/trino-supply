namespace TrinoSupply.Foundation.Application.Email;

/// <summary>
/// Envio de e-mail transacional (convite de usuário, recuperação de senha — OPS/GO-LIVE).
/// A implementação real (SMTP) entra quando <c>Email:Smtp:Host</c> está configurado; sem
/// configuração cai no fallback de log (piloto/dev), que registra o conteúdo em vez de enviar.
/// </summary>
public interface IEmailSender
{
    /// <summary>Envia (ou registra, no fallback) um e-mail. Nunca deve lançar — falha é logada.</summary>
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}
