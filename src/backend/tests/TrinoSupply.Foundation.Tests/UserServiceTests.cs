using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Auth;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Tests;

public class UserServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly JwtOptions Jwt = new() { Secret = new string('k', 48) };

    private static (UserService users, AuthService auth, AppDbContext db, User admin) Build()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        var hasher = new PasswordHasher<User>();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));

        var admin = new User { Email = "admin@trino.test", Name = "Admin", Role = Roles.SystemAdministrator };
        admin.PasswordHash = hasher.HashPassword(admin, "Hx8@baseSegura!");
        db.Users.Add(admin);
        db.SaveChanges();

        return (new UserService(db, hasher, clock), new AuthService(db, new TokenService(Jwt), hasher, clock), db, admin);
    }

    [Fact]
    public async Task Criacao_valida_persiste_com_papel_e_hash()
    {
        var (users, auth, db, _) = Build();

        var (user, error) = await users.CreateAsync("maria@trino.test", "Maria", Roles.WarehouseOperator, "Vt7#trocaAgora!");

        Assert.Null(error);
        Assert.Equal(Roles.WarehouseOperator, user!.Role);
        Assert.NotEqual("Vt7#trocaAgora!", user.PasswordHash);
        Assert.NotNull(await auth.LoginAsync("maria@trino.test", "Vt7#trocaAgora!"));
        Assert.Equal(2, await db.Users.CountAsync());
    }

    [Theory]
    [InlineData("sem-arroba", "Maria", "WarehouseOperator", "Vt7#trocaAgora!", "IAM-ERR-010")]
    [InlineData("maria@trino.test", "M", "WarehouseOperator", "Vt7#trocaAgora!", "IAM-ERR-011")]
    [InlineData("maria@trino.test", "Maria", "PapelInventado", "Vt7#trocaAgora!", "IAM-ERR-012")]
    [InlineData("maria@trino.test", "Maria", "WarehouseOperator", "curta", "IAM-ERR-013")]
    [InlineData("ADMIN@trino.test", "Outro", "Auditor", "Kp4$mudaTudo!x", "IAM-ERR-014")]
    public async Task Criacao_invalida_retorna_o_erro_correto(string email, string name, string role, string pass, string expected)
    {
        var (users, _, _, _) = Build();

        var (user, error) = await users.CreateAsync(email, name, role, pass);

        Assert.Null(user);
        Assert.Equal(expected, error!.Code);
    }

    [Fact]
    public async Task Unico_admin_ativo_nao_pode_ser_rebaixado_nem_inativado()
    {
        var (users, _, _, admin) = Build();
        var actor = Guid.NewGuid(); // outro operador qualquer

        var (_, demote) = await users.UpdateAsync(admin.Id, actor, null, Roles.Auditor, null);
        var (_, deactivate) = await users.UpdateAsync(admin.Id, actor, null, null, false);

        Assert.Equal("IAM-ERR-015", demote!.Code);
        Assert.Equal("IAM-ERR-015", deactivate!.Code);
    }

    [Fact]
    public async Task Com_segundo_admin_ativo_o_primeiro_pode_ser_rebaixado()
    {
        var (users, _, _, admin) = Build();
        await users.CreateAsync("admin2@trino.test", "Admin 2", Roles.SystemAdministrator, "Zq9%novoAcesso!");

        var (updated, error) = await users.UpdateAsync(admin.Id, Guid.NewGuid(), null, Roles.Auditor, null);

        Assert.Null(error);
        Assert.Equal(Roles.Auditor, updated!.Role);
    }

    [Fact]
    public async Task Usuario_nao_inativa_a_si_mesmo()
    {
        var (users, _, _, admin) = Build();
        await users.CreateAsync("admin2@trino.test", "Admin 2", Roles.SystemAdministrator, "Zq9%novoAcesso!");

        var (_, error) = await users.UpdateAsync(admin.Id, admin.Id, null, null, false);

        Assert.Equal("IAM-ERR-016", error!.Code);
    }

    [Fact]
    public async Task Inativacao_revoga_sessoes_e_bloqueia_login()
    {
        var (users, auth, _, admin) = Build();
        await users.CreateAsync("admin2@trino.test", "Admin 2", Roles.SystemAdministrator, "Zq9%novoAcesso!");
        var (maria, _) = await users.CreateAsync("maria@trino.test", "Maria", Roles.WarehouseOperator, "Vt7#trocaAgora!");
        var session = await auth.LoginAsync("maria@trino.test", "Vt7#trocaAgora!");

        var (_, error) = await users.UpdateAsync(maria!.Id, admin.Id, null, null, false);

        Assert.Null(error);
        Assert.Null(await auth.LoginAsync("maria@trino.test", "Vt7#trocaAgora!"));
        Assert.Null(await auth.RefreshAsync(session!.RefreshToken));
    }

    [Fact]
    public async Task Reset_de_senha_troca_a_credencial_e_encerra_sessoes()
    {
        var (users, auth, _, _) = Build();
        var (maria, _) = await users.CreateAsync("maria@trino.test", "Maria", Roles.WarehouseOperator, "Vt7#trocaAgora!");
        var session = await auth.LoginAsync("maria@trino.test", "Vt7#trocaAgora!");

        var error = await users.ResetPasswordAsync(maria!.Id, "Ln3^outraSaida!");

        Assert.Null(error);
        Assert.Null(await auth.LoginAsync("maria@trino.test", "Vt7#trocaAgora!"));
        Assert.NotNull(await auth.LoginAsync("maria@trino.test", "Ln3^outraSaida!"));
        Assert.Null(await auth.RefreshAsync(session!.RefreshToken));
    }

    // ---- senha provisória no primeiro acesso (SEC-004) ----------------------
    [Fact]
    public async Task Usuario_novo_nasce_com_senha_provisoria()
    {
        var (users, auth, _, _) = Build();

        var (maria, _) = await users.CreateAsync("maria@trino.test", "Maria", Roles.WarehouseOperator, "Vt7#trocaAgora!");

        Assert.True(maria!.MustChangePassword);
        Assert.Null(maria.PasswordChangedAt);
        var sessao = await auth.LoginAsync("maria@trino.test", "Vt7#trocaAgora!");
        Assert.True(sessao!.User.MustChangePassword);
    }

    [Fact]
    public async Task Troca_pelo_dono_exige_a_senha_atual_e_encerra_as_outras_sessoes()
    {
        var (users, auth, _, _) = Build();
        var (maria, _) = await users.CreateAsync("maria@trino.test", "Maria", Roles.WarehouseOperator, "Vt7#trocaAgora!");
        var antiga = await auth.LoginAsync("maria@trino.test", "Vt7#trocaAgora!");

        var (_, erradA) = await users.ChangeOwnPasswordAsync(maria!.Id, "chute qualquer", "Ln3^outraSaida!");
        Assert.Equal("IAM-ERR-020", erradA!.Code);

        var (trocada, erro) = await users.ChangeOwnPasswordAsync(maria.Id, "Vt7#trocaAgora!", "Ln3^outraSaida!");

        Assert.Null(erro);
        Assert.False(trocada!.MustChangePassword);
        Assert.NotNull(trocada.PasswordChangedAt);
        Assert.Null(await auth.LoginAsync("maria@trino.test", "Vt7#trocaAgora!"));
        Assert.NotNull(await auth.LoginAsync("maria@trino.test", "Ln3^outraSaida!"));
        // a senha antiga já circulou: nenhuma sessão aberta com ela sobrevive
        Assert.Null(await auth.RefreshAsync(antiga!.RefreshToken));
    }

    [Fact]
    public async Task Reset_pelo_admin_devolve_a_senha_ao_estado_provisorio()
    {
        var (users, _, db, _) = Build();
        var (maria, _) = await users.CreateAsync("maria@trino.test", "Maria", Roles.WarehouseOperator, "Vt7#trocaAgora!");
        await users.ChangeOwnPasswordAsync(maria!.Id, "Vt7#trocaAgora!", "Ln3^outraSaida!");
        Assert.False((await db.Users.FindAsync(maria.Id))!.MustChangePassword);

        Assert.Null(await users.ResetPasswordAsync(maria.Id, "Bc2*maisUmaBoa!"));

        Assert.True((await db.Users.FindAsync(maria.Id))!.MustChangePassword);
    }

    [Theory]
    [InlineData("curta", "IAM-ERR-013")]                    // menos de 12
    [InlineData("Vt7#trocaAgora!", "IAM-ERR-021")]          // igual à atual
    [InlineData("TrinoSupply#2026", "IAM-ERR-021")]         // nome do sistema
    [InlineData("Xk9#mariaSegura!", "IAM-ERR-021")]         // nome/e-mail do dono
    [InlineData("Xk9#abcdefgh!", "IAM-ERR-021")]            // sequência longa
    [InlineData("aaaaaaaaaaaaaa", "IAM-ERR-021")]           // um caractere só
    [InlineData("apenasminusculas", "IAM-ERR-021")]         // um tipo de caractere só
    public async Task Senha_previsivel_e_recusada_na_troca(string nova, string codigo)
    {
        var (users, _, _, _) = Build();
        var (maria, _) = await users.CreateAsync("maria@trino.test", "Maria", Roles.WarehouseOperator, "Vt7#trocaAgora!");

        var (_, erro) = await users.ChangeOwnPasswordAsync(maria!.Id, "Vt7#trocaAgora!", nova);

        Assert.Equal(codigo, erro!.Code);
    }

    [Fact]
    public async Task Reset_com_senha_fraca_e_negado()
    {
        var (users, _, _, admin) = Build();

        var error = await users.ResetPasswordAsync(admin.Id, "curta");

        Assert.Equal("IAM-ERR-013", error!.Code);
    }

    [Fact]
    public async Task Modulos_do_cadastro_restringem_e_invalidos_sao_recusados()
    {
        var (users, _, _, admin) = Build();

        var (maria, ok) = await users.CreateAsync("maria@trino.test", "Maria", Roles.Requester,
            "Vt7#trocaAgora!", ["SOLICITACOES"]);
        Assert.Null(ok);
        Assert.Equal(["SOLICITACOES"], AppModules.EffectiveFor(maria!));

        var (_, invalid) = await users.CreateAsync("jose@trino.test", "José", Roles.Requester,
            "Rt6&outraChave!", ["NAO_EXISTE"]);
        Assert.Equal("IAM-ERR-017", invalid!.Code);

        // sem módulos definidos → padrão do papel; admin sempre tem todos
        var (padrao, _) = await users.CreateAsync("rita@trino.test", "Rita", Roles.Requester, "Bc2*maisUmaBoa!");
        Assert.Equal(AppModules.DefaultsFor(Roles.Requester), AppModules.EffectiveFor(padrao!));
        Assert.Equal(AppModules.All, AppModules.EffectiveFor(admin));

        // atualização substitui a autorização
        var (updated, upOk) = await users.UpdateAsync(maria!.Id, admin.Id, null, null, null, ["MATERIAL", "ESTOQUE"]);
        Assert.Null(upOk);
        Assert.Equal(["MATERIAL", "ESTOQUE"], AppModules.EffectiveFor(updated!));
    }
}
