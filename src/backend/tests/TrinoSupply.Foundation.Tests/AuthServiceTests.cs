using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Auth;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Tests;

public class AuthServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly JwtOptions Jwt = new()
    {
        Secret = new string('k', 48),
        AccessTokenMinutes = 15,
        RefreshTokenDays = 7,
    };

    private static (AuthService svc, AppDbContext db, FixedTimeProvider clock, User admin) Build()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        var hasher = new PasswordHasher<User>();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));

        var admin = new User { Email = "admin@trino.test", Name = "Admin", Role = Roles.SystemAdministrator };
        admin.PasswordHash = hasher.HashPassword(admin, "Hx8@baseSegura!");
        db.Users.Add(admin);
        db.SaveChanges();

        var svc = new AuthService(db, new TokenService(Jwt), hasher, clock);
        return (svc, db, clock, admin);
    }

    [Fact]
    public async Task Login_com_credenciais_validas_emite_access_e_refresh()
    {
        var (svc, db, _, admin) = Build();

        var tokens = await svc.LoginAsync("Admin@Trino.Test", "Hx8@baseSegura!");

        Assert.NotNull(tokens);
        Assert.Equal(admin.Id, tokens!.User.Id);
        Assert.Equal(15 * 60, tokens.ExpiresInSeconds);
        Assert.Single(db.RefreshTokens);
        // o refresh token em claro nunca é persistido — apenas o hash
        Assert.DoesNotContain(db.RefreshTokens, t => t.TokenHash == tokens.RefreshToken);
    }

    [Fact]
    public async Task Login_com_senha_errada_ou_usuario_inexistente_retorna_null()
    {
        var (svc, _, _, _) = Build();

        Assert.Null(await svc.LoginAsync("admin@trino.test", "senha-errada"));
        Assert.Null(await svc.LoginAsync("nao-existe@trino.test", "Hx8@baseSegura!"));
    }

    [Fact]
    public async Task Access_token_contem_claims_e_validade_de_15_minutos()
    {
        var (svc, _, clock, admin) = Build();

        var tokens = await svc.LoginAsync("admin@trino.test", "Hx8@baseSegura!");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens!.AccessToken);

        Assert.Equal(admin.Id.ToString(), jwt.Subject);
        Assert.Contains(jwt.Claims, c => c.Type == "email" && c.Value == "admin@trino.test");
        Assert.Contains(jwt.Claims, c => c.Value == Roles.SystemAdministrator);
        Assert.Equal(clock.Now.UtcDateTime.AddMinutes(15), jwt.ValidTo, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Refresh_rotaciona_o_token_e_invalida_o_anterior()
    {
        var (svc, db, _, _) = Build();
        var first = await svc.LoginAsync("admin@trino.test", "Hx8@baseSegura!");

        var second = await svc.RefreshAsync(first!.RefreshToken);

        Assert.NotNull(second);
        Assert.NotEqual(first.RefreshToken, second!.RefreshToken);
        var oldStored = db.RefreshTokens.Single(t => t.TokenHash == TokenService.HashRefreshToken(first.RefreshToken));
        Assert.NotNull(oldStored.RevokedAt);
        Assert.NotNull(oldStored.ReplacedById);
    }

    [Fact]
    public async Task Reuso_de_refresh_rotacionado_revoga_a_cadeia_inteira()
    {
        var (svc, db, _, _) = Build();
        var first = await svc.LoginAsync("admin@trino.test", "Hx8@baseSegura!");
        var second = await svc.RefreshAsync(first!.RefreshToken);

        // reuso do token antigo (já rotacionado) — ataque de replay
        var replay = await svc.RefreshAsync(first.RefreshToken);

        Assert.Null(replay);
        Assert.All(db.RefreshTokens, t => Assert.NotNull(t.RevokedAt));
        // e o token "válido" da cadeia também morreu
        Assert.Null(await svc.RefreshAsync(second!.RefreshToken));
    }

    [Fact]
    public async Task Refresh_expirado_e_negado()
    {
        var (svc, _, clock, _) = Build();
        var tokens = await svc.LoginAsync("admin@trino.test", "Hx8@baseSegura!");

        clock.Now = clock.Now.AddDays(8); // além dos 7 dias

        Assert.Null(await svc.RefreshAsync(tokens!.RefreshToken));
    }

    [Fact]
    public async Task Logout_revoga_o_refresh_token()
    {
        var (svc, _, _, _) = Build();
        var tokens = await svc.LoginAsync("admin@trino.test", "Hx8@baseSegura!");

        await svc.LogoutAsync(tokens!.RefreshToken);

        Assert.Null(await svc.RefreshAsync(tokens.RefreshToken));
    }

    [Fact]
    public async Task Usuario_inativo_nao_loga()
    {
        var (svc, db, _, admin) = Build();
        admin.Active = false;
        await db.SaveChangesAsync();

        Assert.Null(await svc.LoginAsync("admin@trino.test", "Hx8@baseSegura!"));
    }
}
