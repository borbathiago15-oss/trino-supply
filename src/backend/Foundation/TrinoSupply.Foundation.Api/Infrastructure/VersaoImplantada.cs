namespace TrinoSupply.Foundation.Api.Infrastructure;

/// <summary>
/// Qual commit está no ar. Sem isto, entrega de backend não é verificável de fora:
/// mudança só no servidor não muda o bundle do navegador, então não havia como
/// distinguir "o deploy subiu" de "o deploy falhou e o anterior continua rodando".
/// </summary>
public static class VersaoImplantada
{
    /// <summary>Variáveis consultadas, na ordem de preferência.</summary>
    public const string VariavelDoRailway = "RAILWAY_GIT_COMMIT_SHA";
    public const string VariavelManual = "GIT_SHA";

    /// <summary>
    /// O commit da implantação, ou nulo quando o ambiente não informa. O Railway injeta
    /// <see cref="VariavelDoRailway"/> em toda implantação vinda do GitHub;
    /// <see cref="VariavelManual"/> é a saída para quem sobe a imagem por fora.
    ///
    /// <para>
    /// Nulo é resposta: <c>/health</c> devolve o campo vazio e quem verifica sabe que não
    /// sabe. Inventar "desconhecido" ou cair para a versão do assembly seria pior — daria
    /// a impressão de resposta a quem está justamente conferindo se o deploy subiu.
    /// </para>
    /// </summary>
    public static string? Commit(Func<string, string?>? lerVariavel = null)
    {
        var ler = lerVariavel ?? Environment.GetEnvironmentVariable;
        var sha = Limpo(ler(VariavelDoRailway)) ?? Limpo(ler(VariavelManual));
        return sha;
    }

    /// <summary>Os 7 primeiros caracteres — o que se compara de olho com a lista do GitHub.</summary>
    public static string? Curto(string? commit) =>
        string.IsNullOrWhiteSpace(commit) ? null : commit[..Math.Min(7, commit.Length)];

    private static string? Limpo(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
