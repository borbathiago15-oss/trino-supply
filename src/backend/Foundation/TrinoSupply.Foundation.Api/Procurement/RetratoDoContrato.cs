using System.Globalization;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// O contrato de parceria num instante — número, vigência, teto e o preço de cada produto —, e a
/// frase que diz o que mudou entre dois retratos. O contrato é regravado inteiro a cada edição,
/// então é comparando o antes e o depois que a linha do tempo sabe dizer "o preço da luva foi de
/// R$ 1,80 para R$ 1,95" em vez de só "o contrato foi alterado".
/// </summary>
public sealed record RetratoDoContrato(
    string? Numero, DateOnly? Inicio, DateOnly? Fim, decimal? Teto,
    IReadOnlyDictionary<string, (string Descricao, decimal Preco)> Produtos)
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    public bool Existe => Produtos.Count > 0;

    public static RetratoDoContrato De(Supplier s, IEnumerable<SupplierContractItem>? itens = null) =>
        new(s.ContractNumber, s.ContractValidFrom, s.ContractValidUntil, s.ContractValueLimit,
            (itens ?? s.ContractItems)
                .GroupBy(Chave)
                .ToDictionary(g => g.Key, g => (g.First().Description, g.First().UnitPrice)));

    /// <summary>O produto é o mesmo pelo catálogo; o item digitado, pela descrição.</summary>
    private static string Chave(SupplierContractItem i) =>
        i.CatalogItemId?.ToString() ?? "desc:" + i.Description.Trim().ToUpperInvariant();

    /// <summary>
    /// O que mudou, ou nulo quando nada mudou — salvar o contrato sem mexer em nada não é um
    /// acontecimento, e registrá-lo encheria a linha do tempo de ruído.
    /// </summary>
    public static (string Kind, string Summary)? Mudanca(RetratoDoContrato antes, RetratoDoContrato depois)
    {
        if (!antes.Existe && !depois.Existe) return null;
        if (!antes.Existe)
            return (EventosDoContrato.Criado,
                Frase([
                    depois.Numero is null ? "Contrato cadastrado" : $"Contrato {depois.Numero} cadastrado",
                    $"vigência {Vigencia(depois)}",
                    $"{depois.Produtos.Count} produto(s)",
                    depois.Teto is null ? "sem teto" : $"teto de {Moeda(depois.Teto.Value)}",
                ]));
        if (!depois.Existe)
            return (EventosDoContrato.Encerrado,
                $"Contrato encerrado: os {antes.Produtos.Count} produto(s) saíram e o fornecedor volta a ser cotado normalmente.");

        var partes = new List<string>();
        if (antes.Numero != depois.Numero)
            partes.Add($"número {antes.Numero ?? "—"} → {depois.Numero ?? "—"}");
        if (antes.Inicio != depois.Inicio || antes.Fim != depois.Fim)
            partes.Add($"vigência {Vigencia(antes)} → {Vigencia(depois)}");
        if (antes.Teto != depois.Teto)
            partes.Add($"teto {TetoOuSem(antes.Teto)} → {TetoOuSem(depois.Teto)}");
        foreach (var (k, novo) in depois.Produtos.OrderBy(p => p.Value.Descricao, StringComparer.Create(Br, true)))
        {
            if (!antes.Produtos.TryGetValue(k, out var velho))
                partes.Add($"incluído {novo.Descricao} a {Moeda(novo.Preco)}");
            else if (velho.Preco != novo.Preco)
                partes.Add($"preço de {novo.Descricao} {Moeda(velho.Preco)} → {Moeda(novo.Preco)}");
        }
        foreach (var (k, velho) in antes.Produtos.OrderBy(p => p.Value.Descricao, StringComparer.Create(Br, true)))
            if (!depois.Produtos.ContainsKey(k))
                partes.Add($"retirado {velho.Descricao}");

        return partes.Count == 0 ? null : (EventosDoContrato.Alterado, "Contrato alterado: " + string.Join("; ", partes) + ".");
    }

    private static string Frase(IEnumerable<string> partes) => string.Join(", ", partes) + ".";
    private static string Vigencia(RetratoDoContrato r) => $"{Data(r.Inicio)} a {Data(r.Fim)}";
    private static string Data(DateOnly? d) => d?.ToString("dd/MM/yyyy", Br) ?? "sem data";
    private static string TetoOuSem(decimal? v) => v is null ? "sem teto" : Moeda(v.Value);
    private static string Moeda(decimal v) => v.ToString("C", Br);
}
