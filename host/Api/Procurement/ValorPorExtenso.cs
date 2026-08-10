namespace TrinoSupply.Api.Procurement;

/// <summary>
/// Converte um valor monetário para extenso em português (reais e centavos). Usado no rodapé da OC
/// (ex.: "Um mil e seiscentos reais"). Suporta valores até bilhões — suficiente para pedidos de compra.
/// </summary>
public static class ValorPorExtenso
{
    private static readonly string[] Unidades =
        ["", "um", "dois", "três", "quatro", "cinco", "seis", "sete", "oito", "nove", "dez",
         "onze", "doze", "treze", "quatorze", "quinze", "dezesseis", "dezessete", "dezoito", "dezenove"];

    private static readonly string[] Dezenas =
        ["", "", "vinte", "trinta", "quarenta", "cinquenta", "sessenta", "setenta", "oitenta", "noventa"];

    private static readonly string[] Centenas =
        ["", "cento", "duzentos", "trezentos", "quatrocentos", "quinhentos", "seiscentos",
         "setecentos", "oitocentos", "novecentos"];

    public static string Converter(decimal valor)
    {
        if (valor < 0) return "menos " + Converter(-valor);

        var inteiro = (long)Math.Floor(valor);
        var centavos = (int)Math.Round((valor - inteiro) * 100m, MidpointRounding.AwayFromZero);
        if (centavos == 100) { inteiro += 1; centavos = 0; }

        var parteReais = inteiro == 0 ? "zero reais"
            : $"{PorExtenso(inteiro)} {(inteiro == 1 ? "real" : "reais")}";

        if (centavos == 0)
            return Capitalizar(parteReais);

        var parteCentavos = $"{PorExtenso(centavos)} {(centavos == 1 ? "centavo" : "centavos")}";
        return Capitalizar($"{parteReais} e {parteCentavos}");
    }

    private static string PorExtenso(long numero)
    {
        if (numero == 0) return "zero";

        var grupos = new List<int>();
        while (numero > 0)
        {
            grupos.Add((int)(numero % 1000));
            numero /= 1000;
        }

        string[] escalaSingular = ["", "mil", "milhão", "bilhão", "trilhão"];
        string[] escalaPlural = ["", "mil", "milhões", "bilhões", "trilhões"];

        var partes = new List<string>();
        for (var i = grupos.Count - 1; i >= 0; i--)
        {
            var grupo = grupos[i];
            if (grupo == 0) continue;

            var texto = TresDigitos(grupo);
            if (i > 0)
            {
                var escala = grupo == 1 ? escalaSingular[i] : escalaPlural[i];
                texto = i == 1 ? $"{(grupo == 1 ? string.Empty : texto + " ")}{escala}".Trim() : $"{texto} {escala}";
            }
            partes.Add(texto);
        }

        // Conjunção "e" conforme convenção pt-BR (entre centenas e o último grupo, quando cabível).
        return JuntarComE(partes, grupos);
    }

    private static string JuntarComE(List<string> partes, List<int> grupos)
    {
        if (partes.Count == 1) return partes[0];

        var sb = new System.Text.StringBuilder();
        for (var idx = 0; idx < partes.Count; idx++)
        {
            if (idx > 0)
            {
                var ultimoGrupo = grupos[0];
                var penultimoEhUltimo = idx == partes.Count - 1;
                // "e" antes do último grupo se ele for < 100 ou múltiplo exato de 100.
                var usaE = penultimoEhUltimo && (ultimoGrupo < 100 || ultimoGrupo % 100 == 0) && ultimoGrupo != 0;
                sb.Append(usaE ? " e " : ", ");
            }
            sb.Append(partes[idx]);
        }
        return sb.ToString();
    }

    private static string TresDigitos(int n)
    {
        if (n == 100) return "cem";
        var centena = n / 100;
        var resto = n % 100;

        var partes = new List<string>();
        if (centena > 0) partes.Add(Centenas[centena]);
        if (resto > 0)
        {
            if (resto < 20) partes.Add(Unidades[resto]);
            else
            {
                var dez = resto / 10;
                var uni = resto % 10;
                partes.Add(uni > 0 ? $"{Dezenas[dez]} e {Unidades[uni]}" : Dezenas[dez]);
            }
        }
        return string.Join(" e ", partes);
    }

    private static string Capitalizar(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
