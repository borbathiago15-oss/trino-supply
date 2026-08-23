using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Auth;

public static class AdminSeeder
{
    /// <summary>
    /// Cria o usuário administrador inicial a partir de ADMIN_EMAIL / ADMIN_PASSWORD (e ADMIN_NAME opcional).
    /// Roda apenas quando não existe nenhum usuário; nunca sobrescreve senha de usuário existente.
    /// </summary>
    public static async Task<bool> SeedAsync(AppDbContext db, IPasswordHasher<User> hasher, IConfiguration config, ILogger logger)
    {
        if (await db.Users.AnyAsync()) return true;

        var email = config["ADMIN_EMAIL"]?.Trim().ToLowerInvariant();
        var password = config["ADMIN_PASSWORD"];
        var name = config["ADMIN_NAME"] ?? "Administrador";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogCritical(
                "Nenhum usuário existe e ADMIN_EMAIL/ADMIN_PASSWORD não foram definidos. " +
                "Defina as variáveis de ambiente e reinicie para criar o acesso inicial. O login responderá 503 até lá.");
            return false;
        }

        if (password.Length < 12)
        {
            logger.LogCritical("ADMIN_PASSWORD precisa ter no mínimo 12 caracteres (SEC-003). Seed não executado.");
            return false;
        }

        var admin = new User { Email = email, Name = name, Role = Roles.SystemAdministrator };
        admin.PasswordHash = hasher.HashPassword(admin, password);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        logger.LogInformation("Usuário administrador inicial criado: {Email}", email);
        return true;
    }
}
