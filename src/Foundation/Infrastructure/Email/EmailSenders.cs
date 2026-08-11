using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using TrinoSupply.Foundation.Application.Email;

namespace TrinoSupply.Foundation.Infrastructure.Email;

/// <summary>Configuração de e-mail (seção <c>Email</c>). Host vazio = fallback de log.</summary>
public sealed record EmailOptions(
    string? SmtpHost, int SmtpPort, string? SmtpUser, string? SmtpPassword,
    bool UseSsl, string From, string WebBaseUrl)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(SmtpHost);
}

/// <summary>Envio real via SMTP. Falha de envio é logada e engolida — e-mail é melhor-esforço.</summary>
public sealed class SmtpEmailSender(EmailOptions options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        try
        {
            using var client = new SmtpClient(options.SmtpHost!, options.SmtpPort)
            {
                EnableSsl = options.UseSsl,
                Credentials = string.IsNullOrWhiteSpace(options.SmtpUser)
                    ? null
                    : new NetworkCredential(options.SmtpUser, options.SmtpPassword)
            };
            using var message = new MailMessage(options.From, to, subject, body);
            await client.SendMailAsync(message, ct);
            logger.LogInformation("E-mail enviado para {To}: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao enviar e-mail para {To}: {Subject}", to, subject);
        }
    }
}

/// <summary>
/// Fallback sem SMTP (piloto/dev): registra o e-mail no log da API — o operador copia o link de
/// definição de senha de lá. Troca-se por SMTP real apenas configurando <c>Email:Smtp:Host</c>.
/// </summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        logger.LogWarning("SMTP não configurado — e-mail NÃO enviado. Para {To} | {Subject}\n{Body}", to, subject, body);
        return Task.CompletedTask;
    }
}
