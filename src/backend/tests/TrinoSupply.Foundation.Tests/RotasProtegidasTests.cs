using System.Reflection;
using System.Text.RegularExpressions;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// SEC-A — o que a tabela de rotas não mostra.
///
/// A autorização de cada rota e o inventário passaram para
/// <see cref="TabelaDeRotasTests"/>, que confere a tabela que o ASP.NET
/// registrou de verdade. Sobrou aqui uma invariante só: os filtros de grupo
/// (<c>RejectSupplierRole</c>, <c>RequireModules</c>) são embrulhados no
/// delegate e não viram metadado, então não há como enxergá-los na tabela —
/// a conferência é sobre o texto da fonte mesmo.
/// </summary>
public class RotasProtegidasTests
{
    /// <summary>Program.cs e cada arquivo de Rotas/, como o compilador os viu.</summary>
    private static readonly Dictionary<string, string> Fontes = LerFontes();

    private static readonly string TudoJunto = string.Join('\n', Fontes.Values);

    /// <summary>
    /// Grupos públicos por decisão, não por esquecimento: são as portas de
    /// entrada. As rotas autenticadas dentro deles declaram a exigência uma a
    /// uma. Um grupo público novo faz este teste falhar até ser justificado.
    /// </summary>
    private static readonly string[] GruposPublicos = ["/api/v1/auth", "/api/v1/portal"];

    private const string PadraoGrupo = @"^\s*var (?<var>\w+) = app\.MapGroup\(""(?<rota>/api[^""]*)""";

    [Fact]
    public void O_token_do_portal_nao_alcanca_grupo_interno()
    {
        // RFQ-001 §5: o papel Supplier vale no portal e em lugar nenhum além dele.
        // Vale como proteção qualquer coisa que o token do portal não satisfaça:
        // RejectSupplierRole, RequireModules, ou uma exigência de papel no próprio
        // grupo (é o caso de /users, que só o administrador alcança).
        var desprotegidos = Declaracoes(PadraoGrupo)
            .Where(d => !GruposPublicos.Contains(d.rota))
            .Where(d => !d.corpo.Contains("RequireRole(") && !UsaFiltro(d.rota))
            .Select(d => d.rota)
            .ToList();

        Assert.True(desprotegidos.Count == 0,
            "Grupo interno sem RejectSupplierRole, RequireModules nem exigência de papel: "
            + string.Join(", ", desprotegidos));
    }

    /// <summary>
    /// OPS-B: o tratador de falha existe e está registrado. `EnvelopeDeFalhaTests` prova
    /// o que ele faz, montando o próprio pipeline; aqui se confere que o app de verdade o
    /// usa — e antes de tudo, porque middleware registrado depois não vê o que quebrou
    /// antes dele.
    /// </summary>
    [Fact]
    public void O_app_registra_o_envelope_de_falha_antes_do_resto()
    {
        // por linha, e não por IndexOf: a chamada comentada continua no texto do arquivo,
        // e um `//` na frente satisfaria a busca sem registrar nada
        var linhas = Fontes["rotas.Program.cs"].Split('\n')
            .Select(l => l.Trim()).ToList();
        var envelope = linhas.FindIndex(l => l.StartsWith("app.UsarEnvelopeDeFalha()", StringComparison.Ordinal));
        Assert.True(envelope >= 0, "O Program.cs não registra o tratador de falha (OPS-B).");

        var estaticos = linhas.FindIndex(l => l.StartsWith("app.UseStaticFiles(", StringComparison.Ordinal));
        Assert.True(envelope < estaticos, "O tratador de falha precisa vir antes do resto do pipeline.");
    }

    [Fact]
    public void A_fonte_conferida_e_mesmo_a_do_app()
    {
        // se os recursos embutidos sumirem, os testes acima passariam sobre o vazio
        Assert.Contains("rotas.Program.cs", Fontes.Keys);
        Assert.Contains("app.MapGroup(\"/api/v1/analytics\")", TudoJunto);
        Assert.True(TudoJunto.Length > 50_000, "A fonte embutida parece truncada.");
    }

    // ---- leitura da fonte ----------------------------------------------------

    /// <summary>Filtro declarado para o grupo, na linha do MapGroup ou logo abaixo.</summary>
    private static bool UsaFiltro(string rota)
    {
        var nome = Regex.Match(TudoJunto, @"^\s*var (?<v>\w+) = app\.MapGroup\(""" + Regex.Escape(rota) + @"""",
            RegexOptions.Multiline).Groups["v"].Value;
        if (nome.Length == 0) return false;
        return Regex.IsMatch(TudoJunto,
            $@"^\s*{Regex.Escape(nome)}\.AddEndpointFilter\((RejectSupplierRole|RequireModules)\(",
            RegexOptions.Multiline);
    }

    /// <summary>
    /// Cada declaração de rota com o seu corpo. O corpo vai da linha do
    /// <c>Map…</c> até a próxima que abre uma declaração no mesmo recuo — as
    /// continuações vêm mais indentadas ou fechando o lambda
    /// (<c>}).RequireAuthorization();</c>).
    /// </summary>
    private static List<(string rota, string corpo)> Declaracoes(string padrao)
    {
        var regex = new Regex(padrao, RegexOptions.Multiline);
        var achadas = new List<(string, string)>();

        foreach (var (_, fonte) in Fontes)
        {
            var linhas = fonte.Split('\n');
            for (var i = 0; i < linhas.Length; i++)
            {
                var m = regex.Match(linhas[i]);
                if (!m.Success) continue;
                var recuo = linhas[i].Length - linhas[i].TrimStart().Length;
                var corpo = new List<string> { linhas[i] };
                for (var j = i + 1; j < linhas.Length; j++)
                {
                    var l = linhas[j];
                    var conteudo = l.TrimStart();
                    var recuoAtual = l.Length - conteudo.Length;
                    if (conteudo.Length > 0 && recuoAtual <= recuo
                        && (char.IsLetter(conteudo[0]) || conteudo.StartsWith("//", StringComparison.Ordinal)))
                        break;
                    corpo.Add(l);
                }
                achadas.Add((m.Groups["rota"].Value, string.Join('\n', corpo)));
            }
        }
        return achadas;
    }

    private static Dictionary<string, string> LerFontes()
    {
        var asm = Assembly.GetExecutingAssembly();
        return asm.GetManifestResourceNames()
            .Where(n => n.StartsWith("rotas.", StringComparison.Ordinal))
            .ToDictionary(n => n, n =>
            {
                using var fluxo = asm.GetManifestResourceStream(n)!;
                return new StreamReader(fluxo).ReadToEnd().Replace("\r\n", "\n");
            });
    }
}
