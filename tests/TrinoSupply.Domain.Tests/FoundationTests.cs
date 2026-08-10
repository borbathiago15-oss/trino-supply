using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.Foundation.Domain.Iam;
using TrinoSupply.Foundation.Infrastructure.Security;
using Xunit;

namespace TrinoSupply.Domain.Tests;

public class RoleTests
{
    private static readonly CompanyId Company = CompanyId.New();

    [Fact]
    public void Create_com_nome_vazio_falha()
    {
        var result = Role.Create(Company, "  ");
        Assert.True(result.IsFailure);
        Assert.Equal("iam.role.name_required", result.Error.Code);
    }

    [Fact]
    public void Grant_permissao_conhecida_concede()
    {
        var role = Role.Create(Company, "Admin").Value;
        var granted = role.Grant(PermissionCatalog.UsersRead);

        Assert.True(granted.IsSuccess);
        Assert.True(role.Has(PermissionCatalog.UsersRead));
    }

    [Fact]
    public void Grant_permissao_desconhecida_falha()
    {
        var role = Role.Create(Company, "Admin").Value;
        var granted = role.Grant("permissao.inexistente");

        Assert.True(granted.IsFailure);
        Assert.Equal("iam.permission.unknown", granted.Error.Code);
        Assert.False(role.Has("permissao.inexistente"));
    }

    [Fact]
    public void Revoke_remove_a_permissao()
    {
        var role = Role.Create(Company, "Admin").Value;
        role.Grant(PermissionCatalog.UsersManage);
        role.Revoke(PermissionCatalog.UsersManage);

        Assert.False(role.Has(PermissionCatalog.UsersManage));
    }
}

public class UserTests
{
    private static readonly CompanyId Company = CompanyId.New();

    [Fact]
    public void Register_normaliza_email_e_fica_ativo()
    {
        var user = User.Register(Company, "sub-1", "  ADMIN@Trino.com ", "Admin").Value;

        Assert.Equal("admin@trino.com", user.Email);
        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void Register_sem_subject_falha()
    {
        var result = User.Register(Company, "", "a@a.com", "A");
        Assert.True(result.IsFailure);
        Assert.Equal("iam.user.subject_required", result.Error.Code);
    }

    [Fact]
    public void AssignRole_nao_duplica()
    {
        var user = User.Register(Company, "sub-1", "a@a.com", "A").Value;
        var roleId = RoleId.New();
        user.AssignRole(roleId);
        user.AssignRole(roleId);

        Assert.Single(user.RoleIds);
    }
}

public class PasswordHasherTests
{
    [Fact]
    public void Hash_e_Verify_fazem_roundtrip()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var hash = hasher.Hash("senhaForte123");

        Assert.True(hasher.Verify(hash, "senhaForte123"));
        Assert.False(hasher.Verify(hash, "senhaErrada"));
    }

    [Fact]
    public void Hash_gera_saidas_diferentes_por_salt()
    {
        var hasher = new Pbkdf2PasswordHasher();
        Assert.NotEqual(hasher.Hash("x12345678"), hasher.Hash("x12345678"));
    }

    [Fact]
    public void Verify_hash_malformado_retorna_false()
    {
        var hasher = new Pbkdf2PasswordHasher();
        Assert.False(hasher.Verify("nao-e-um-hash", "qualquer"));
    }
}
