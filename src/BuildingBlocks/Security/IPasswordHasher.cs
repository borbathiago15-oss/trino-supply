namespace TrinoSupply.BuildingBlocks.Security;

/// <summary>Hash de senha resistente a força bruta (SEC-003). Formato self-describing (algo/params embutidos).</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}
