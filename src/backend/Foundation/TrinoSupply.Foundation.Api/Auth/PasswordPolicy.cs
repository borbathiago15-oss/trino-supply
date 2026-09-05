using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Auth;

/// <summary>
/// Política de senha (SEC-004). O objetivo não é medir entropia, é impedir os
/// padrões que aparecem quando alguém cadastra usuários em série: a mesma senha
/// para todo mundo, o nome do sistema, o e-mail da pessoa, sequências de teclado.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 12;

    /// <summary>Senhas recusadas na íntegra, sem depender de quem é o usuário.</summary>
    private static readonly string[] Proibidas =
    [
        "trinosupply", "trino supply", "grupotrino", "grupo trino",
        "senha", "password", "123456", "qwerty", "abcdef",
        "mudar123", "trocar123", "primeiroacesso", "acessoinicial",
    ];

    /// <summary>
    /// Valida a senha nova. `atual` é a senha em uso, quando conhecida — trocar
    /// uma senha por ela mesma não é troca.
    /// </summary>
    public static UserError? Validar(string? nova, User usuario, string? atual = null)
    {
        if (string.IsNullOrWhiteSpace(nova) || nova.Length < MinimumLength)
            return new("IAM-ERR-013", $"A senha precisa ter no mínimo {MinimumLength} caracteres.");
        if (nova.Length > 200)
            return new("IAM-ERR-013", "A senha passou do tamanho aceito (200 caracteres).");
        if (nova.Trim().Length != nova.Length)
            return new("IAM-ERR-013", "A senha não pode começar nem terminar com espaço.");

        if (atual is not null && string.Equals(nova, atual, StringComparison.Ordinal))
            return new("IAM-ERR-021", "A senha nova precisa ser diferente da atual.");

        var minuscula = nova.ToLowerInvariant();

        if (Proibidas.Any(p => minuscula.Contains(p, StringComparison.Ordinal)))
            return new("IAM-ERR-021", "Esta senha é previsível demais: evite o nome do sistema, da empresa ou palavras como \"senha\" e \"123456\".");

        var contaEmail = usuario.Email.Split('@')[0];
        if (contaEmail.Length >= 4 && minuscula.Contains(contaEmail.ToLowerInvariant(), StringComparison.Ordinal))
            return new("IAM-ERR-021", "A senha não pode conter o seu e-mail.");

        foreach (var parte in usuario.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (parte.Length >= 4 && minuscula.Contains(parte.ToLowerInvariant(), StringComparison.Ordinal))
                return new("IAM-ERR-021", "A senha não pode conter o seu nome.");

        if (UmCaractereSo(nova))
            return new("IAM-ERR-021", "A senha não pode ser um caractere repetido.");
        if (TemSequencia(minuscula, 6))
            return new("IAM-ERR-021", "A senha tem uma sequência longa demais (como \"123456\" ou \"abcdef\").");

        // três dos quatro tipos: minúscula, maiúscula, dígito e símbolo
        var tipos = new[]
        {
            nova.Any(char.IsLower), nova.Any(char.IsUpper), nova.Any(char.IsDigit),
            nova.Any(c => !char.IsLetterOrDigit(c)),
        }.Count(t => t);
        if (tipos < 3)
            return new("IAM-ERR-021", "Combine ao menos três entre: letra minúscula, maiúscula, número e símbolo.");

        return null;
    }

    private static bool UmCaractereSo(string senha) => senha.Distinct().Count() == 1;

    /// <summary>Sequência crescente ou decrescente de `tamanho` caracteres seguidos (123456, fedcba).</summary>
    private static bool TemSequencia(string senha, int tamanho)
    {
        if (senha.Length < tamanho) return false;
        var subindo = 1;
        var descendo = 1;
        for (var i = 1; i < senha.Length; i++)
        {
            var passo = senha[i] - senha[i - 1];
            subindo = passo == 1 ? subindo + 1 : 1;
            descendo = passo == -1 ? descendo + 1 : 1;
            if (subindo >= tamanho || descendo >= tamanho) return true;
        }
        return false;
    }
}
