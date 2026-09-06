using System.Reflection;
using System.Text.RegularExpressions;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// SEC-A — a autorização das rotas deixa de ser lembrança e vira rede.
///
/// A auditoria encontrou dez grupos de rota com filtro declarado e um sem
/// (<c>analytics</c>), que repetia a checagem dentro de cada handler. Todos
/// acertavam; nada obrigava o próximo a lembrar. Estes testes falham quando
/// uma rota <c>/api</c> nasce sem autorização declarada.
///
/// A conferência é sobre o texto do Program.cs, e não sobre a tabela de rotas
/// em execução: o app roda as migrations na inicialização, então montá-lo num
/// teste exigiria um Postgres no job de testes unitários, que hoje sobe em
/// segundos sem banco nenhum. Quando o Program.cs for recortado em rotas por
/// módulo (ARQ-A), a versão em execução fica barata e substitui esta.
/// </summary>
public class RotasProtegidasTests
{
    private static readonly string Fonte = LerPrograma();

    /// <summary>
    /// Grupos públicos por decisão, não por esquecimento: são as portas de
    /// entrada. As rotas autenticadas dentro deles declaram a exigência uma a
    /// uma. Um grupo público novo faz este teste falhar até ser justificado.
    /// </summary>
    private static readonly string[] GruposPublicos = ["/api/v1/auth", "/api/v1/portal"];

    [Fact]
    public void Todo_grupo_de_rota_da_api_exige_autenticacao()
    {
        var abertos = Declaracoes(@"^var \w+ = app\.MapGroup\(""(?<rota>/api[^""]*)""")
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
        var publicos = Declaracoes(@"^var \w+ = app\.MapGroup\(""(?<rota>/api[^""]*)""")
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
        var abertas = Declaracoes(@"^app\.Map(Get|Post|Put|Delete|Patch)\(\s*""(?<rota>/api[^""]*)""")
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
        var desprotegidos = Declaracoes(@"^var \w+ = app\.MapGroup\(""(?<rota>/api[^""]*)""")
            .Where(d => !GruposPublicos.Contains(d.rota))
            .Where(d => !d.corpo.Contains("RequireRole(") && !UsaFiltro(d.rota))
            .Select(d => d.rota)
            .ToList();

        Assert.True(desprotegidos.Count == 0,
            "Grupo interno sem RejectSupplierRole, RequireModules nem exigência de papel: "
            + string.Join(", ", desprotegidos));
    }

    [Fact]
    public void A_fonte_conferida_e_mesmo_o_program()
    {
        // se o recurso embutido sumir, os testes acima passariam sobre o vazio
        Assert.Contains("app.MapGroup(\"/api/v1/analytics\")", Fonte);
        Assert.True(Fonte.Length > 50_000, "Program.cs embutido parece truncado.");
    }

    /// <summary>Filtro declarado para o grupo, na linha do MapGroup ou logo abaixo.</summary>
    private static bool UsaFiltro(string rota)
    {
        var nome = Regex.Match(Fonte, @"^var (?<v>\w+) = app\.MapGroup\(""" + Regex.Escape(rota) + @"""",
            RegexOptions.Multiline).Groups["v"].Value;
        if (nome.Length == 0) return false;
        return Regex.IsMatch(Fonte,
            $@"^{Regex.Escape(nome)}\.AddEndpointFilter\((RejectSupplierRole|RequireModules)\(",
            RegexOptions.Multiline);
    }

    /// <summary>
    /// Cada declaração de rota com o seu corpo. O corpo vai da linha do
    /// <c>Map…</c> até a próxima linha que começa na coluna zero com letra ou
    /// comentário — ou seja, até a declaração seguinte. As continuações vêm
    /// indentadas ou fechando o lambda (<c>}).RequireAuthorization();</c>).
    /// </summary>
    private static List<(string rota, string corpo)> Declaracoes(string padrao)
    {
        var linhas = Fonte.Split('\n');
        var regex = new Regex(padrao);
        var achadas = new List<(string, string)>();

        for (var i = 0; i < linhas.Length; i++)
        {
            var m = regex.Match(linhas[i]);
            if (!m.Success) continue;
            var corpo = new List<string> { linhas[i] };
            for (var j = i + 1; j < linhas.Length; j++)
            {
                var l = linhas[j];
                if (l.Length > 0 && (char.IsLetter(l[0]) || l.StartsWith("//"))) break;
                corpo.Add(l);
            }
            achadas.Add((m.Groups["rota"].Value, string.Join('\n', corpo)));
        }
        return achadas;
    }

    private static string LerPrograma()
    {
        var asm = Assembly.GetExecutingAssembly();
        var nome = asm.GetManifestResourceNames().Single(n => n.EndsWith("Program.cs", StringComparison.Ordinal));
        using var fluxo = asm.GetManifestResourceStream(nome)!;
        return new StreamReader(fluxo).ReadToEnd().Replace("\r\n", "\n");
    }
}
