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
        admin.PasswordHash = hasher.HashPassword(admin, "SenhaForte#2026");
        db.Users.Add(admin);
        db.SaveChanges();

        return (new UserService(db, hasher, clock), new AuthService(db, new TokenService(Jwt), hasher, clock), db, admin);
    }

    [Fact]
    public async Task Criacao_valida_persiste_com_papel_e_hash()
    {
        var (users, auth, db, _) = Build();

        var (user, error) = await users.CreateAsync("maria@trino.test", "Maria", Roles.WarehouseOperator, "SenhaDaMaria#1");

        Assert.Null(error);
        Assert.Equal(Roles.WarehouseOperator, user!.Role);
        Assert.NotEqual("SenhaDaMaria#1", user.PasswordHash);
        Assert.NotNull(await auth.LoginAsync("maria@trino.test", "SenhaDaMaria#1"));
        Assert.Equal(2, await db.Users.CountAsync());
    }

    [Theory]
    [InlineData("sem-arroba", "Maria", "WarehouseOperator", "SenhaDaMaria#1", "IAM-ERR-010")]
    [InlineData("maria@trino.test", "M", "WarehouseOperator", "SenhaDaMaria#1", "IAM-ERR-011")]
    [InlineData("maria@trino.test", "Maria", "PapelInventado", "SenhaDaMaria#1", "IAM-ERR-012")]
    [InlineData("maria@trino.test", "Maria", "WarehouseOperator", "curta", "IAM-ERR-013")]
    [InlineData("ADMIN@trino.test", "Outro", "Auditor", "SenhaDoOutro#1", "IAM-ERR-014")]
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
        await users.CreateAsync("admin2@trino.test", "Admin 2", Roles.SystemAdministrator, "SenhaDoAdmin2#");

        var (updated, error) = await users.UpdateAsync(admin.Id, Guid.NewGuid(), null, Roles.Auditor, null);

        Assert.Null(error);
        Assert.Equal(Roles.Auditor, updated!.Role);
    }

    [Fact]
    public async Task Usuario_nao_inativa_a_si_mesmo()
    {
        var (users, _, _, admin) = Build();
        await users.CreateAsync("admin2@trino.test", "Admin 2", Roles.SystemAdministrator, "SenhaDoAdmin2#");

        var (_, error) = await users.UpdateAsync(admin.Id, admin.Id, null, null, false);

        Assert.Equal("IAM-ERR-016", error!.Code);
    }

    [Fact]
    public async Task Inativacao_revoga_sessoes_e_bloqueia_login()
    {
        var (users, auth, _, admin) = Build();
        await users.CreateAsync("admin2@trino.test", "Admin 2", Roles.SystemAdministrator, "SenhaDoAdmin2#");
        var (maria, _) = await users.CreateAsync("maria@trino.test", "Maria", Roles.WarehouseOperator, "SenhaDaMaria#1");
        var session = await auth.LoginAsync("maria@trino.test", "SenhaDaMaria#1");

        var (_, error) = await users.UpdateAsync(maria!.Id, admin.Id, null, null, false);

        Assert.Null(error);
        Assert.Null(await auth.LoginAsync("maria@trino.test", "SenhaDaMaria#1"));
        Assert.Null(await auth.RefreshAsync(session!.RefreshToken));
    }

    [Fact]
    public async Task Reset_de_senha_troca_a_credencial_e_encerra_sessoes()
    {
        var (users, auth, _, _) = Build();
        var (maria, _) = await users.CreateAsync("maria@trino.test", "Maria", Roles.WarehouseOperator, "SenhaDaMaria#1");
        var session = await auth.LoginAsync("maria@trino.test", "SenhaDaMaria#1");

        var error = await users.ResetPasswordAsync(maria!.Id, "SenhaNovaDaMaria#2");

        Assert.Null(error);
        Assert.Null(await auth.LoginAsync("maria@trino.test", "SenhaDaMaria#1"));
        Assert.NotNull(await auth.LoginAsync("maria@trino.test", "SenhaNovaDaMaria#2"));
        Assert.Null(await auth.RefreshAsync(session!.RefreshToken));
    }

    [Fact]
    public async Task Reset_com_senha_fraca_e_negado()
    {
        var (users, _, _, admin) = Build();

        var error = await users.ResetPasswordAsync(admin.Id, "curta");

        Assert.Equal("IAM-ERR-013", error!.Code);
    }
}
