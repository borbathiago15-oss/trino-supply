using System.Reflection;
using System.Text.RegularExpressions;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// SEC-A — a autorização das rotas deixa de ser lembrança e vira rede.
///
/// A auditoria encontrou dez grupos de rota com filtro declarado e um sem
/// (<c>analytics</c>), que repetia a checagem dentro de cada handler. Todos
/// acertavam; nada obrigava o próximo a lembrar. Estes testes falham quando
/// uma rota <c>/api</c> nasce sem autorização declarada — e, desde o ARQ-A,
/// quando uma rota some no meio do recorte do Program.cs.
///
/// A conferência é sobre o texto da fonte, e não sobre a tabela de rotas em
/// execução: o app roda as migrations na inicialização, então montá-lo num
/// teste exigiria um Postgres no job de testes unitários, que hoje sobe em
/// segundos sem banco nenhum. Quando o recorte terminar e cada módulo expuser
/// o seu <c>Map…</c>, dá para montar a tabela sem banco e trocar isto pela
/// versão em execução.
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
    private const string PadraoRotaSolta = @"^\s*app\.Map(Get|Post|Put|Delete|Patch)\(\s*""(?<rota>/api[^""]*)""";

    [Fact]
    public void Todo_grupo_de_rota_da_api_exige_autenticacao()
    {
        var abertos = Declaracoes(PadraoGrupo)
            .Where(d => !d.corpo.Contains(".RequireAuthorization("))
            .Select(d => d.rota)
            .Except(GruposPublicos)
            .ToList();

        Assert.True(abertos.Count == 0,
            "Grupo de rota sem autorização declarada: " + string.Join(", ", abertos));
    }

    [Fact]
    public void Os_unicos_grupos_publicos_sao_as_portas_de_entrada()
    {
        // trava a lista: abrir um grupo novo passa a ser um ato deliberado
        var publicos = Declaracoes(PadraoGrupo)
            .Where(d => !d.corpo.Contains(".RequireAuthorization("))
            .Select(d => d.rota)
            .OrderBy(r => r)
            .ToList();

        Assert.Equal(GruposPublicos.OrderBy(r => r), publicos);
    }

    [Fact]
    public void Toda_rota_da_api_fora_de_grupo_exige_autenticacao()
    {
        // são as mais expostas: sem grupo, a exigência é escrita à mão em cada uma
        var abertas = Declaracoes(PadraoRotaSolta)
            .Where(d => !d.corpo.Contains(".RequireAuthorization("))
            .Select(d => d.rota)
            .ToList();

        Assert.True(abertas.Count == 0,
            "Rota /api sem RequireAuthorization: " + string.Join(", ", abertas));
    }

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
    /// O inventário das rotas, conferido contra um arquivo versionado.
    ///
    /// É a rede do recorte do Program.cs (ARQ-A): mover um bloco de arquivo não
    /// muda nada aqui, mas perder uma rota no caminho — ou ganhar uma sem
    /// querer — quebra o teste. Rota nova de verdade se registra atualizando
    /// `fixtures/rotas-da-api.txt`, que é um ato visível na revisão.
    /// </summary>
    [Fact]
    public void O_inventario_de_rotas_confere_com_o_arquivo_versionado()
    {
        var esperadas = File.ReadAllLines(Path.Combine("fixtures", "rotas-da-api.txt"))
            .Where(l => !string.IsNullOrWhiteSpace(l)).OrderBy(l => l, StringComparer.Ordinal).ToList();
        var encontradas = Inventario().OrderBy(l => l, StringComparer.Ordinal).ToList();

        var sumiram = esperadas.Except(encontradas).ToList();
        var novas = encontradas.Except(esperadas).ToList();
        Assert.True(sumiram.Count == 0, "Rota que existia e não foi encontrada: " + string.Join(", ", sumiram));
        Assert.True(novas.Count == 0,
            "Rota nova fora do inventário — atualize fixtures/rotas-da-api.txt: " + string.Join(", ", novas));
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

    /// <summary>Prefixo de cada grupo, pelo nome da variável que o guarda.</summary>
    private static Dictionary<string, string> Grupos() =>
        Declaracoes(PadraoGrupo, "var").Zip(Declaracoes(PadraoGrupo))
            .ToDictionary(x => x.First.rota, x => x.Second.rota);

    /// <summary>Todas as rotas <c>/api</c>, com o prefixo do grupo já resolvido.</summary>
    private static HashSet<string> Inventario()
    {
        var grupos = Grupos();
        var rotas = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (_, fonte) in Fontes)
        {
            foreach (Match m in Regex.Matches(fonte,
                @"^\s*(?<var>\w+)\.Map(?<verbo>Get|Post|Put|Delete|Patch)\(\s*\n?\s*""(?<rota>[^""]*)""",
                RegexOptions.Multiline))
            {
                var nome = m.Groups["var"].Value;
                var caminho = m.Groups["rota"].Value;
                var verbo = m.Groups["verbo"].Value.ToUpperInvariant();
                if (nome == "app")
                {
                    if (caminho.StartsWith("/api", StringComparison.Ordinal)) rotas.Add($"{verbo} {caminho}");
                }
                else if (grupos.TryGetValue(nome, out var prefixo))
                {
                    rotas.Add($"{verbo} {prefixo}{(caminho == "/" ? "" : caminho)}");
                }
            }
        }
        return rotas;
    }

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
    private static List<(string rota, string corpo)> Declaracoes(string padrao, string grupo = "rota")
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
                achadas.Add((m.Groups[grupo].Value, string.Join('\n', corpo)));
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
