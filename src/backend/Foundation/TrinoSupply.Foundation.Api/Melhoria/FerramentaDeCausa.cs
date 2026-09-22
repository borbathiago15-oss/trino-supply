using System.Text.Json;

namespace TrinoSupply.Foundation.Api.Melhoria;

/// <summary>Uma causa que a ferramenta apontou, já com a conta feita.</summary>
/// <param name="Rotulo">O texto da causa.</param>
/// <param name="Valor">Pareto: o valor informado. GUT: o produto G×U×T.</param>
/// <param name="Percentual">Pareto: quanto esta causa representa do total.</param>
/// <param name="Acumulado">Pareto: o acumulado <b>incluindo</b> esta causa.</param>
/// <param name="Vital">Se a ferramenta a elegeu como vital — a régua é de cada ferramenta.</param>
/// <param name="Detalhe">O que explica o número: "G 5 · U 4 · T 3".</param>
public record CausaDaAnalise(
    string Rotulo, decimal? Valor = null, decimal? Percentual = null, decimal? Acumulado = null,
    bool Vital = false, string? Detalhe = null);

/// <summary>Um degrau dos 5 Porquês — a escada que a tela desenha.</summary>
public record DegrauDoPorque(int Numero, string Pergunta, string Resposta);

/// <summary>Um dos 6M do Ishikawa, com o que foi listado nele.</summary>
public record GrupoDeCausas(string Chave, string Rotulo, IReadOnlyList<string> Itens);

/// <summary>
/// A ferramenta de causa já normalizada: ordenada, somada e com o vital marcado.
/// </summary>
public record AnaliseDeCausa(
    string Chave, string Nome,
    IReadOnlyList<CausaDaAnalise> Causas,
    IReadOnlyList<DegrauDoPorque> Degraus,
    IReadOnlyList<GrupoDeCausas> Grupos,
    IReadOnlyList<string> Ideias,
    string? Problema = null, string? Efeito = null, string? CausaRaiz = null,
    string? Aviso = null)
{
    public IReadOnlyList<CausaDaAnalise> Vitais => [.. Causas.Where(c => c.Vital)];
}

/// <summary>
/// A ferramenta de análise de causa, preenchida no sistema e normalizada <b>num lugar só</b>.
///
/// <para>
/// A regra que sustenta o módulo: <b>uma função normaliza, e todas as saídas leem dela</b> —
/// tela, leitura automática e qualquer relatório. Duas leituras do mesmo JSON dariam dois
/// números para a mesma análise, e a tela que mostrasse o número errado seria a que o usuário
/// acreditou.
/// </para>
///
/// <para>
/// Pareto e GUT <b>ordenam e calculam aqui</b>, no servidor, e não na tela. É o mesmo motivo:
/// quem ordena decide qual é a causa vital, e essa decisão não pode depender de em qual tela
/// o usuário está olhando.
/// </para>
/// </summary>
public static class FerramentaDeCausa
{
    public const string CincoPorques = "CINCO_PORQUES";
    public const string Ishikawa = "ISHIKAWA";
    public const string Pareto = "PARETO";
    public const string Gut = "GUT";
    public const string Brainstorming = "BRAINSTORMING";

    public static readonly string[] Todas = [CincoPorques, Ishikawa, Pareto, Gut, Brainstorming];

    /// <summary>A fatia do acumulado que separa o vital do trivial (Pareto).</summary>
    public const decimal LimitePareto = 80m;

    /// <summary>
    /// Abaixo deste produto o GUT <b>não elege ninguém</b>. Um problema G2·U2·T2 é o maior de
    /// uma lista fraca, não uma prioridade — marcá-lo como vital mandaria a equipe atacar o
    /// que a própria ferramenta diz que pode esperar.
    /// </summary>
    public const int LimiteGut = 27;

    public static string Rotulo(string chave) => chave switch
    {
        CincoPorques => "5 Porquês",
        Ishikawa => "Ishikawa (6M)",
        Pareto => "Pareto",
        Gut => "Matriz GUT",
        Brainstorming => "Brainstorming",
        _ => chave,
    };

    private static readonly (string Chave, string Rotulo)[] SeisEmes =
    [
        ("maquina", "Máquina"), ("metodo", "Método"), ("mao_de_obra", "Mão de obra"),
        ("material", "Material"), ("medicao", "Medição"), ("meio_ambiente", "Meio ambiente"),
    ];

    /// <summary>
    /// Normaliza a ferramenta gravada. JSON ilegível <b>não explode</b>: volta como aviso, para
    /// a tela dizer que a análise não pôde ser lida em vez de derrubar o ciclo inteiro.
    /// </summary>
    public static AnaliseDeCausa? Normalizar(string? nome, string? json)
    {
        if (string.IsNullOrWhiteSpace(nome)) return null;
        var chave = nome.Trim().ToUpperInvariant();
        if (!Todas.Contains(chave)) return null;

        JsonElement dados;
        try
        {
            dados = string.IsNullOrWhiteSpace(json)
                ? default
                : JsonDocument.Parse(json).RootElement;
        }
        catch (JsonException)
        {
            return Vazia(chave, "A análise gravada não pôde ser lida.");
        }
        if (dados.ValueKind != JsonValueKind.Object) return Vazia(chave, null);

        return chave switch
        {
            CincoPorques => DosPorques(dados),
            Ishikawa => DoIshikawa(dados),
            Pareto => DoPareto(dados),
            Gut => DoGut(dados),
            _ => DoBrainstorming(dados),
        };
    }

    private static AnaliseDeCausa Vazia(string chave, string? aviso) =>
        new(chave, Rotulo(chave), [], [], [], [], Aviso: aviso);

    // ---- 5 Porquês -----------------------------------------------------------

    /// <summary>A causa raiz é vital <b>sempre</b>: é o que a ferramenta existe para achar.</summary>
    private static AnaliseDeCausa DosPorques(JsonElement d)
    {
        var degraus = new List<DegrauDoPorque>();
        for (var i = 1; i <= 5; i++)
        {
            var pergunta = Texto(d, $"porque{i}") ?? Texto(d, $"w{i}_why");
            var resposta = Texto(d, $"resposta{i}") ?? Texto(d, $"w{i}_ans");
            if (pergunta is null && resposta is null) continue;
            degraus.Add(new(i, pergunta ?? $"Por quê? ({i})", resposta ?? ""));
        }
        var raiz = Texto(d, "causa_raiz") ?? Texto(d, "root_cause");
        List<CausaDaAnalise> causas = raiz is null ? [] : [new(raiz, Vital: true)];
        return new(CincoPorques, Rotulo(CincoPorques), causas, degraus, [], [],
            Problema: Texto(d, "problema") ?? Texto(d, "problem"), CausaRaiz: raiz);
    }

    // ---- Ishikawa ------------------------------------------------------------

    /// <summary>Nenhuma causa é vital por si: o espinha-de-peixe levanta, não prioriza.</summary>
    private static AnaliseDeCausa DoIshikawa(JsonElement d)
    {
        var grupos = new List<GrupoDeCausas>();
        var causas = new List<CausaDaAnalise>();
        foreach (var (chave, rotulo) in SeisEmes)
        {
            var itens = Lista(d, chave);
            grupos.Add(new(chave, rotulo, itens));
            causas.AddRange(itens.Select(i => new CausaDaAnalise(i, Detalhe: rotulo)));
        }
        return new(Ishikawa, Rotulo(Ishikawa), causas, [], grupos, [],
            Efeito: Texto(d, "efeito") ?? Texto(d, "effect"));
    }

    // ---- Pareto --------------------------------------------------------------

    /// <summary>
    /// Regra 80/20. O vital é marcado pelo acumulado <b>anterior</b> ao próprio item
    /// (<c>acumulado - percentual &lt; 80</c>): usar o acumulado depois deixaria de fora
    /// justamente o item que <b>cruza</b> os 80%, que é o que fecha a conta.
    /// </summary>
    private static AnaliseDeCausa DoPareto(JsonElement d)
    {
        var brutos = new List<(string Rotulo, decimal Valor)>();
        foreach (var item in Itens(d))
        {
            var rotulo = Texto(item, "causa") ?? Texto(item, "problema");
            var valor = Numero(item, "valor");
            if (rotulo is null || valor is not { } v || v <= 0) continue;
            brutos.Add((rotulo, v));
        }
        var total = brutos.Sum(b => b.Valor);
        if (total <= 0) return Vazia(Pareto, null);

        var causas = new List<CausaDaAnalise>();
        decimal acumulado = 0;
        foreach (var (rotulo, valor) in brutos.OrderByDescending(b => b.Valor).ThenBy(b => b.Rotulo))
        {
            var pct = Math.Round(valor / total * 100m, 2);
            acumulado = Math.Round(acumulado + pct, 2);
            causas.Add(new(rotulo, valor, pct, acumulado, Vital: acumulado - pct < LimitePareto));
        }
        return new(Pareto, Rotulo(Pareto), causas, [], [], []);
    }

    // ---- GUT -----------------------------------------------------------------

    /// <summary>
    /// A de maior G×U×T, <b>desde que</b> o produto chegue a <see cref="LimiteGut"/>. Abaixo
    /// disso a matriz não elege ninguém: nenhum item da lista é urgente o bastante.
    /// </summary>
    private static AnaliseDeCausa DoGut(JsonElement d)
    {
        var brutos = new List<(string Rotulo, int G, int U, int T)>();
        foreach (var item in Itens(d))
        {
            var rotulo = Texto(item, "problema") ?? Texto(item, "causa");
            if (rotulo is null) continue;
            var g = Nota(item, "g"); var u = Nota(item, "u"); var t = Nota(item, "t");
            if (g is null || u is null || t is null) continue;
            brutos.Add((rotulo, g.Value, u.Value, t.Value));
        }
        if (brutos.Count == 0) return Vazia(Gut, null);

        var ordenados = brutos
            .OrderByDescending(b => b.G * b.U * b.T).ThenBy(b => b.Rotulo).ToList();
        var maior = ordenados[0].G * ordenados[0].U * ordenados[0].T;
        var causas = ordenados.Select((b, i) =>
        {
            var produto = b.G * b.U * b.T;
            return new CausaDaAnalise(b.Rotulo, produto, Vital: i == 0 && maior >= LimiteGut,
                Detalhe: $"G {b.G} · U {b.U} · T {b.T}");
        }).ToList();
        return new(Gut, Rotulo(Gut), causas, [], [], []);
    }

    // ---- Brainstorming -------------------------------------------------------

    private static AnaliseDeCausa DoBrainstorming(JsonElement d)
    {
        var ideias = Lista(d, "ideias").Concat(Lista(d, "ideas")).Distinct().ToList();
        return new(Brainstorming, Rotulo(Brainstorming),
            [.. ideias.Select(i => new CausaDaAnalise(i))], [], [], ideias);
    }

    // ---- leitura do JSON -----------------------------------------------------

    private static IEnumerable<JsonElement> Itens(JsonElement d) =>
        d.TryGetProperty("itens", out var i) && i.ValueKind == JsonValueKind.Array
            ? i.EnumerateArray()
            : d.TryGetProperty("items", out var j) && j.ValueKind == JsonValueKind.Array
                ? j.EnumerateArray()
                : [];

    private static string? Texto(JsonElement d, string campo) =>
        d.ValueKind == JsonValueKind.Object && d.TryGetProperty(campo, out var v)
        && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim() : null;

    private static decimal? Numero(JsonElement d, string campo)
    {
        if (d.ValueKind != JsonValueKind.Object || !d.TryGetProperty(campo, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var n)) return n;
        return v.ValueKind == JsonValueKind.String
            && decimal.TryParse(v.GetString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : null;
    }

    /// <summary>Nota de 1 a 5. Fora da faixa é dado errado, e dado errado não entra na conta.</summary>
    private static int? Nota(JsonElement d, string campo) =>
        Numero(d, campo) is { } n && n >= 1 && n <= 5 ? (int)n : null;

    private static List<string> Lista(JsonElement d, string campo) =>
        d.ValueKind == JsonValueKind.Object && d.TryGetProperty(campo, out var v)
        && v.ValueKind == JsonValueKind.Array
            ? [.. v.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim())]
            : [];
}
