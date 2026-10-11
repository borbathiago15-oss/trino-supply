using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Auth;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Tests;

/// <summary>A verificação em duas etapas: o TOTP em si e o caminho do login (ver Auth/Mfa.cs).</summary>
public class MfaTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly JwtOptions Jwt = new() { Secret = new string('k', 48) };

    private sealed record Mundo(AuthService Auth, MfaService Mfa, AppDbContext Db, FixedTimeProvider Clock,
        CofreDeSegredos Cofre, User Diretor, User Comprador);

    private static Mundo Build()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var hasher = new PasswordHasher<User>();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));
        var diretor = new User { Email = "dir@trino.test", Name = "Dora Diretora", Role = Roles.Director };
        diretor.PasswordHash = hasher.HashPassword(diretor, "Hx8@baseSegura!");
        var comprador = new User { Email = "comp@trino.test", Name = "Caio Comprador", Role = Roles.PurchasingOfficer };
        comprador.PasswordHash = hasher.HashPassword(comprador, "Hx8@baseSegura!");
        db.Users.AddRange(diretor, comprador);
        db.SaveChanges();
        var tokens = new TokenService(Jwt);
        var cofre = new CofreDeSegredos(Jwt);
        var auth = new AuthService(db, tokens, hasher, clock);
        return new(auth, new MfaService(db, tokens, cofre, auth, clock), db, clock, cofre, diretor, comprador);
    }

    // ---- o algoritmo ---------------------------------------------------------------------
    [Theory]
    [InlineData(59, "287082")]
    [InlineData(1111111109, "081804")]
    [InlineData(1234567890, "005924")]
    [InlineData(2000000000, "279037")]
    public void Totp_bate_com_os_vetores_da_rfc_6238(long segundos, string esperado)
    {
        // segredo da RFC: "12345678901234567890" em ASCII
        var segredo = Base32.Codificar("12345678901234567890"u8.ToArray());
        Assert.Equal(esperado, Totp.Codigo(segredo, DateTimeOffset.FromUnixTimeSeconds(segundos)));
    }

    [Fact]
    public void Base32_vai_e_volta_e_o_segredo_novo_tem_160_bits()
    {
        var bytes = new byte[] { 0, 1, 2, 250, 251, 252, 253, 254, 255 };
        Assert.Equal(bytes, Base32.Decodificar(Base32.Codificar(bytes)));
        Assert.Equal(20, Base32.Decodificar(Totp.NovoSegredo()).Length);
        Assert.Equal("JBSWY3DPEHPK3PXP", Base32.Codificar("Hello!\xDE\xAD\xBE\xEF"u8.ToArray()[..6].Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }).ToArray()));
    }

    [Fact]
    public void Confere_aceita_o_passo_vizinho_e_recusa_o_de_dois_passos_atras()
    {
        var segredo = Totp.NovoSegredo();
        var agora = new DateTimeOffset(2026, 10, 11, 12, 0, 15, TimeSpan.Zero);
        Assert.True(Totp.Confere(segredo, Totp.Codigo(segredo, agora), agora));
        Assert.True(Totp.Confere(segredo, Totp.Codigo(segredo, agora.AddSeconds(-30)), agora));
        Assert.True(Totp.Confere(segredo, Totp.Codigo(segredo, agora.AddSeconds(30)), agora));
        Assert.False(Totp.Confere(segredo, Totp.Codigo(segredo, agora.AddSeconds(-90)), agora));
        Assert.False(Totp.Confere(segredo, "12345", agora));
        Assert.False(Totp.Confere(segredo, null, agora));
    }

    [Fact]
    public void O_cofre_cifra_com_nonce_novo_e_decifra_de_volta()
    {
        var cofre = new CofreDeSegredos(Jwt);
        var a = cofre.Cifrar("SEGREDO"); var b = cofre.Cifrar("SEGREDO");
        Assert.NotEqual(a, b);
        Assert.DoesNotContain("SEGREDO", a);
        Assert.Equal("SEGREDO", cofre.Decifrar(a));
    }

    // ---- o caminho ------------------------------------------------------------------------
    [Fact]
    public async Task Diretor_sem_autenticador_entra_preso_no_cadastro_e_o_comprador_entra_livre()
    {
        var w = Build();
        var dir = await w.Auth.LoginAsync("dir@trino.test", "Hx8@baseSegura!");
        Assert.NotNull(dir);
        Assert.Contains(Mfa.ClaimCadastro, dir!.AccessToken.Length > 0 ? Claims(dir.AccessToken) : []);
        Assert.True(Mfa.PrecisaCadastrar(w.Diretor));

        var comp = await w.Auth.LoginAsync("comp@trino.test", "Hx8@baseSegura!");
        Assert.DoesNotContain(Mfa.ClaimCadastro, Claims(comp!.AccessToken));
        Assert.False(Mfa.PrecisaCadastrar(w.Comprador));
    }

    [Fact]
    public async Task Cadastrar_confirmar_e_entrar_em_duas_etapas()
    {
        var w = Build();
        var (cadastro, erro) = await w.Mfa.IniciarAsync(w.Diretor.Id);
        Assert.Null(erro);
        Assert.StartsWith("otpauth://totp/Trino%20Supply:dir%40trino.test?secret=" + cadastro!.Secret, cadastro.OtpauthUrl);
        // gravado cifrado, e ainda não vale
        Assert.NotEqual(cadastro.Secret, w.Diretor.TotpSecret);
        Assert.False(Mfa.Ativa(w.Diretor));

        var (_, _, errado) = await w.Mfa.AtivarAsync(w.Diretor.Id, "000000");
        Assert.Equal("IAM-ERR-027", errado?.Code);

        var (sessao, codigos, ok) = await w.Mfa.AtivarAsync(w.Diretor.Id, Totp.Codigo(cadastro.Secret, w.Clock.Now));
        Assert.Null(ok);
        Assert.True(Mfa.Ativa(w.Diretor));
        Assert.Equal(Mfa.CodigosDeRecuperacao, codigos!.Length);
        Assert.DoesNotContain(Mfa.ClaimCadastro, Claims(sessao!.AccessToken));   // a sessão nova já não está presa

        // daqui em diante a senha certa não dá sessão: dá o token do meio do login
        var tentativa = await w.Auth.TentarEntrarAsync("dir@trino.test", "Hx8@baseSegura!");
        Assert.Null(tentativa.Tokens);
        Assert.NotNull(tentativa.MfaToken);

        var (etapa, erroEtapa) = await w.Mfa.VerificarAsync(tentativa.MfaToken, Totp.Codigo(cadastro.Secret, w.Clock.Now));
        Assert.Null(erroEtapa);
        Assert.False(etapa!.UsedRecoveryCode);
        Assert.Equal(w.Diretor.Id, etapa.Tokens.User.Id);
        Assert.Equal(8, etapa.RecoveryCodesLeft);
    }

    [Fact]
    public async Task O_token_do_meio_do_login_nao_e_sessao_e_expira_em_cinco_minutos()
    {
        var w = Build();
        await Cadastrar(w, w.Diretor);
        var tentativa = await w.Auth.TentarEntrarAsync("dir@trino.test", "Hx8@baseSegura!");

        // a API recusa o token pendente como Bearer: audiência diferente
        var parametros = TokenService.BuildValidationParameters(Jwt);
        Assert.ThrowsAny<Microsoft.IdentityModel.Tokens.SecurityTokenException>(() =>
            new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ValidateToken(tentativa.MfaToken!, parametros, out _));
        // e um token de sessão não serve como token pendente
        var sessao = await w.Auth.IssueForAsync(w.Comprador);
        var (_, deSessao) = await w.Mfa.VerificarAsync(sessao.AccessToken, "000000");
        Assert.Equal("IAM-ERR-029", deSessao?.Code);

        w.Clock.Now = w.Clock.Now.AddMinutes(6);
        var (_, expirado) = await w.Mfa.VerificarAsync(tentativa.MfaToken, "000000");
        Assert.Equal("IAM-ERR-029", expirado?.Code);
    }

    [Fact]
    public async Task Codigo_errado_conta_como_senha_errada_e_cinco_fecham_a_conta()
    {
        var w = Build();
        var segredo = await Cadastrar(w, w.Diretor);
        var tentativa = await w.Auth.TentarEntrarAsync("dir@trino.test", "Hx8@baseSegura!");
        for (var i = 0; i < 5; i++)
            Assert.Equal("IAM-ERR-027", (await w.Mfa.VerificarAsync(tentativa.MfaToken, "000000")).error?.Code);
        Assert.NotNull(w.Diretor.LockedUntil);

        var (_, bloqueada) = await w.Mfa.VerificarAsync(tentativa.MfaToken, Totp.Codigo(segredo, w.Clock.Now));
        Assert.Equal("IAM-ERR-003", bloqueada?.Code);

        w.Clock.Now = w.Clock.Now.AddMinutes(16);
        var nova = await w.Auth.TentarEntrarAsync("dir@trino.test", "Hx8@baseSegura!");
        var (etapa, erro) = await w.Mfa.VerificarAsync(nova.MfaToken, Totp.Codigo(segredo, w.Clock.Now));
        Assert.Null(erro);
        Assert.NotNull(etapa);
        Assert.Equal(0, w.Diretor.FailedLogins);
    }

    [Fact]
    public async Task Codigo_de_recuperacao_entra_uma_vez_so()
    {
        var w = Build();
        var (cadastro, _) = await w.Mfa.IniciarAsync(w.Diretor.Id);
        var (_, codigos, _) = await w.Mfa.AtivarAsync(w.Diretor.Id, Totp.Codigo(cadastro!.Secret, w.Clock.Now));

        var t1 = await w.Auth.TentarEntrarAsync("dir@trino.test", "Hx8@baseSegura!");
        var (etapa, erro) = await w.Mfa.VerificarAsync(t1.MfaToken, codigos![0].ToUpperInvariant());   // caixa e hífen não importam
        Assert.Null(erro);
        Assert.True(etapa!.UsedRecoveryCode);
        Assert.Equal(7, etapa.RecoveryCodesLeft);

        var t2 = await w.Auth.TentarEntrarAsync("dir@trino.test", "Hx8@baseSegura!");
        Assert.Equal("IAM-ERR-027", (await w.Mfa.VerificarAsync(t2.MfaToken, codigos[0])).error?.Code);
    }

    [Fact]
    public async Task Quem_o_papel_obriga_nao_desliga_e_quem_e_opcional_desliga_com_o_codigo()
    {
        var w = Build();
        var segredoDir = await Cadastrar(w, w.Diretor);
        Assert.Equal("IAM-ERR-028", (await w.Mfa.DesativarAsync(w.Diretor.Id, Totp.Codigo(segredoDir, w.Clock.Now)))?.Code);
        Assert.True(Mfa.Ativa(w.Diretor));

        var segredoComp = await Cadastrar(w, w.Comprador);
        Assert.Equal("IAM-ERR-027", (await w.Mfa.DesativarAsync(w.Comprador.Id, "000000"))?.Code);
        Assert.Null(await w.Mfa.DesativarAsync(w.Comprador.Id, Totp.Codigo(segredoComp, w.Clock.Now)));
        Assert.False(Mfa.Ativa(w.Comprador));
        Assert.NotNull(await w.Auth.LoginAsync("comp@trino.test", "Hx8@baseSegura!"));   // volta a entrar direto
    }

    [Fact]
    public async Task Recomecar_o_cadastro_com_o_autenticador_ativo_e_recusado()
    {
        var w = Build();
        await Cadastrar(w, w.Diretor);
        Assert.Equal("IAM-ERR-025", (await w.Mfa.IniciarAsync(w.Diretor.Id)).error?.Code);
    }

    [Fact]
    public async Task O_administrador_redefine_e_a_pessoa_volta_a_cadastrar_sem_sessao_antiga()
    {
        var w = Build();
        var segredo = await Cadastrar(w, w.Diretor);
        var t = await w.Auth.TentarEntrarAsync("dir@trino.test", "Hx8@baseSegura!");
        var (etapa, _) = await w.Mfa.VerificarAsync(t.MfaToken, Totp.Codigo(segredo, w.Clock.Now));

        Assert.Null(await w.Mfa.RedefinirAsync(w.Diretor.Id));
        Assert.False(Mfa.Ativa(w.Diretor));
        Assert.Null(await w.Auth.RefreshAsync(etapa!.Tokens.RefreshToken));   // sessão derrubada
        var denovo = await w.Auth.LoginAsync("dir@trino.test", "Hx8@baseSegura!");
        Assert.Contains(Mfa.ClaimCadastro, Claims(denovo!.AccessToken));      // presa no cadastro outra vez
        Assert.Equal("IAM-ERR-404", (await w.Mfa.RedefinirAsync(Guid.NewGuid()))?.Code);
    }

    private static async Task<string> Cadastrar(Mundo w, User u)
    {
        var (cadastro, _) = await w.Mfa.IniciarAsync(u.Id);
        var (_, _, erro) = await w.Mfa.AtivarAsync(u.Id, Totp.Codigo(cadastro!.Secret, w.Clock.Now));
        Assert.Null(erro);
        return cadastro.Secret;
    }

    private static string[] Claims(string jwt) =>
        new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims.Select(c => c.Type).ToArray();
}
