namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// <b>Forma</b> de pagamento: por onde o dinheiro sai — boleto, Pix, cartão, depósito,
/// dinheiro. É o "como", e não o "quando".
///
/// <para>
/// Existe como cadastro, e não como texto livre na proposta, por um motivo prático: o
/// comprador digitava "Boleto" no campo de <em>condição</em> e "boleto bancario" no de
/// outro processo. Dois nomes para a mesma coisa não somam em relatório nenhum, e a
/// comparação entre propostas fica a cargo de quem lê.
/// </para>
/// </summary>
public class PaymentMethod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// <b>Condição</b> de pagamento: quando se paga — à vista, 30/60/90, 14/28.
///
/// <para>
/// O nome carrega o cronograma como o comprador o diz ("Parcelado 30/60/90"), e
/// <see cref="Installments"/> guarda em quantas parcelas ele se divide. Os dois juntos
/// respondem as duas perguntas que a proposta precisa: em quantas vezes, e em que datas.
/// </para>
///
/// <para>
/// <see cref="FirstDueDays"/> é o prazo até a primeira parcela. É ele que preenche
/// sozinho o "prazo para pagamento" da proposta — o campo que hoje o comprador digita à
/// mão a cada cotação, e que é o mesmo número toda vez para a mesma condição.
/// </para>
/// </summary>
public class PaymentTermOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    /// <summary>Em quantas parcelas — 1 é à vista.</summary>
    public int Installments { get; set; } = 1;
    /// <summary>Dias até a primeira parcela; nulo quando a condição não define.</summary>
    public int? FirstDueDays { get; set; }
    /// <summary>A condição sugerida quando o comprador não escolhe nenhuma.</summary>
    public bool IsDefault { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
