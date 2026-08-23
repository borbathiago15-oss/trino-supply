using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;

namespace TrinoSupply.Foundation.Api.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("foundation");

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("app_user");
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).HasColumnName("id");
            e.Property(u => u.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
            e.Property(u => u.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(u => u.PasswordHash).HasColumnName("password_hash").IsRequired();
            e.Property(u => u.Role).HasColumnName("role").HasMaxLength(50).IsRequired();
            e.Property(u => u.Active).HasColumnName("active");
            e.Property(u => u.CreatedAt).HasColumnName("created_at");
            e.Property(u => u.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_token");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasColumnName("id");
            e.Property(t => t.UserId).HasColumnName("user_id");
            e.Property(t => t.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
            e.Property(t => t.ExpiresAt).HasColumnName("expires_at");
            e.Property(t => t.CreatedAt).HasColumnName("created_at");
            e.Property(t => t.RevokedAt).HasColumnName("revoked_at");
            e.Property(t => t.ReplacedById).HasColumnName("replaced_by_id");
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(t => t.UserId);
        });
    }
}
