using TrinoSupply.Foundation.Api.Domain;

namespace TrinoSupply.Foundation.Api.Auth;

/// <summary>
/// Bloqueio do login por conta (auditoria A4). O limite por IP barra a força bruta de um
/// endereço só; este barra o ataque distribuído — muitos endereços, uma conta —, que o
/// limite por IP não enxerga.
///
/// <para>
/// A régua: cinco senhas erradas seguidas fecham a conta por quinze minutos, e cada cinco a
/// mais dobram a espera, até um dia. A senha certa zera o contador. A senha errada durante o
/// bloqueio continua recebendo "e-mail ou senha inválidos" — dizer "bloqueada" a quem não tem
/// a senha confirmaria que a conta existe; a senha <b>certa</b> durante o bloqueio recebe o
/// aviso com o tempo que falta, porque quem a tem é o dono, e deixá-lo tentando "a senha
/// errada" que ele sabe estar certa seria pior que dizer a verdade.
/// </para>
///
/// <para>
/// Desbloqueia o tempo, a redefinição de senha pelo administrador e o desbloqueio explícito
/// na tela de Usuários. Nada aqui bloqueia a conta do Portal do Fornecedor, que tem credencial
/// e limite próprios.
/// </para>
/// </summary>
public static class BloqueioPorConta
{
    public const int FalhasParaBloquear = 5;
    public static readonly TimeSpan BloqueioInicial = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan BloqueioMaximo = TimeSpan.FromHours(24);

    /// <summary>Quanto tempo a conta fecha depois de <paramref name="falhas"/> erros seguidos; nulo = ainda não fecha.</summary>
    public static TimeSpan? Duracao(int falhas)
    {
        if (falhas < FalhasParaBloquear) return null;
        var degraus = falhas / FalhasParaBloquear - 1;          // 5 → 0, 10 → 1, 15 → 2…
        var minutos = BloqueioInicial.TotalMinutes * Math.Pow(2, Math.Min(degraus, 10));
        return TimeSpan.FromMinutes(Math.Min(minutos, BloqueioMaximo.TotalMinutes));
    }

    public static bool Bloqueada(User u, DateTimeOffset agora) => u.LockedUntil is { } ate && ate > agora;

    /// <summary>Mais uma senha errada: conta, e fecha a conta se chegou ao degrau.</summary>
    public static void RegistrarFalha(User u, DateTimeOffset agora)
    {
        u.FailedLogins++;
        if (Duracao(u.FailedLogins) is { } d) u.LockedUntil = agora + d;
    }

    /// <summary>A senha certa entrou, ou alguém com autoridade abriu a conta: o contador volta ao zero.</summary>
    public static void Desbloquear(User u)
    {
        u.FailedLogins = 0;
        u.LockedUntil = null;
    }

    /// <summary>A frase da recusa para quem tem a senha certa, com o tempo que falta em minutos cheios.</summary>
    public static string Mensagem(DateTimeOffset ate, DateTimeOffset agora)
    {
        var minutos = Math.Max(1, (int)Math.Ceiling((ate - agora).TotalMinutes));
        return $"Conta bloqueada por tentativas erradas de senha. Tente de novo em {minutos} min, " +
               "ou peça ao administrador para desbloquear.";
    }
}
