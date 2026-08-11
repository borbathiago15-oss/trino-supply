using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TrinoSupply.Foundation.Application.Email;
using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// Fluxo de convite/recuperação de senha (Fase C): forgot → e-mail com link → reset → login com a
/// nova senha; token é de uso único e as sessões antigas caem. O e-mail é capturado por um fake.
/// </summary>
[Collection("pilot")]
public sealed class PasswordSetupIntegrationTests(PilotFixture fixture)
{
    private sealed class CapturingEmailSender : IEmailSender
    {
        public readonly List<(string To, string Subject, string Body)> Sent = [];
        public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
        {
            lock (Sent) Sent.Add((to, subject, body));
            return Task.CompletedTask;
        }
    }

    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken, string RefreshToken);
    private record UserResp(Guid UserId, bool Invited);

    private const string Password = "senha12345";

    [Fact]
    public async Task Convite_e_esqueci_minha_senha_de_ponta_a_ponta()
    {
        var mailbox = new CapturingEmailSender();
        using var factory = fixture.Factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IEmailSender>(mailbox))));
        var c = factory.CreateClient();

        var adminEmail = $"{Guid.NewGuid():N}@pwd.com";
        var (_, comp) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = "Pwd Co", taxId = $"7{Guid.NewGuid():N}"[..14], adminSubject = $"adm-{Guid.NewGuid():N}",
            adminEmail, adminName = "Adm", adminPassword = Password,
        });
        var (_, adminLogin) = await PostAsync<LoginResp>(c, "/api/v1/auth/login",
            new { companyId = comp!.CompanyId, email = adminEmail, password = Password });
        var admin = adminLogin!.AccessToken;

        // 1) Usuário criado SEM senha → convite por e-mail com o link de definição.
        var userEmail = $"{Guid.NewGuid():N}@pwd.com";
        var (createdStatus, created) = await PostAsync<UserResp>(c, "/api/v1/users",
            new { subject = $"u-{Guid.NewGuid():N}", email = userEmail, displayName = "Convidado", password = (string?)null }, admin);
        Assert.Equal(201, createdStatus);
        Assert.True(created!.Invited);
        var invite = Assert.Single(mailbox.Sent.Where(m => m.To == userEmail));
        var token = ExtractToken(invite.Body);

        // Sem senha definida, login é recusado.
        Assert.Equal(401, await PostStatusAsync(c, "/api/v1/auth/login",
            new { companyId = comp.CompanyId, email = userEmail, password = Password }));

        // Senha fraca no reset → 400.
        Assert.Equal(400, await PostStatusAsync(c, "/api/v1/auth/password/reset",
            new { companyId = comp.CompanyId, token, newPassword = "curta" }));

        // 2) Reset com o token do convite → 204 e o login passa a funcionar.
        Assert.Equal(204, await PostStatusAsync(c, "/api/v1/auth/password/reset",
            new { companyId = comp.CompanyId, token, newPassword = Password }));
        var (loginOk, userLogin) = await PostAsync<LoginResp>(c, "/api/v1/auth/login",
            new { companyId = comp.CompanyId, email = userEmail, password = Password });
        Assert.Equal(200, loginOk);

        // Token é de uso único: repetir o reset com o mesmo token → 400.
        Assert.Equal(400, await PostStatusAsync(c, "/api/v1/auth/password/reset",
            new { companyId = comp.CompanyId, token, newPassword = "outraSenha123" }));

        // 3) "Esqueci minha senha": sempre 202 (mesmo para e-mail desconhecido — anti-enumeração).
        Assert.Equal(202, await PostStatusAsync(c, "/api/v1/auth/password/forgot",
            new { companyId = comp.CompanyId, email = "nao-existe@pwd.com" }));
        Assert.DoesNotContain(mailbox.Sent, m => m.To == "nao-existe@pwd.com");

        Assert.Equal(202, await PostStatusAsync(c, "/api/v1/auth/password/forgot",
            new { companyId = comp.CompanyId, email = userEmail }));
        var reset = mailbox.Sent.Last(m => m.To == userEmail);
        var token2 = ExtractToken(reset.Body);
        Assert.NotEqual(token, token2);

        Assert.Equal(204, await PostStatusAsync(c, "/api/v1/auth/password/reset",
            new { companyId = comp.CompanyId, token = token2, newPassword = "novaSenha123" }));

        // Nova senha vale; a antiga não; e o refresh token da sessão antiga foi revogado.
        Assert.Equal(200, await PostStatusAsync(c, "/api/v1/auth/login",
            new { companyId = comp.CompanyId, email = userEmail, password = "novaSenha123" }));
        Assert.Equal(401, await PostStatusAsync(c, "/api/v1/auth/login",
            new { companyId = comp.CompanyId, email = userEmail, password = Password }));
        Assert.Equal(401, await PostStatusAsync(c, "/api/v1/auth/refresh",
            new { companyId = comp.CompanyId, refreshToken = userLogin!.RefreshToken }));
    }

    private static string ExtractToken(string body)
    {
        var marker = "token=";
        var start = body.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "link com token não encontrado no e-mail");
        var token = body[(start + marker.Length)..];
        var end = token.IndexOfAny(['\n', '\r', ' ']);
        return (end >= 0 ? token[..end] : token).Trim();
    }
}
