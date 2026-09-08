namespace TrinoSupply.Foundation.Api.Domain;

/// <summary>
/// Comunicado do administrador: o recado que aparece para todo mundo ao abrir o
/// sistema, dentro de uma vigência, e que cada pessoa fecha quando já leu.
///
/// Não se confunde com o aviso da Central de Avisos. O aviso é **derivado** do
/// trabalho parado ("3 SCs aguardando você") e some sozinho quando o trabalho
/// anda; o comunicado é **escrito por uma pessoa** e só sai de cena quando a
/// vigência acaba ou quem leu o fecha. Misturar os dois faria o número do menu
/// contar recado, e recado não é fila.
/// </summary>
public class Announcement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    /// <summary>Texto do recado. Opcional: um comunicado pode ser só o cartaz.</summary>
    public string? Body { get; set; }
    /// <summary>Imagem do comunicado em `stored_document` (cartaz, aviso digitalizado).</summary>
    public Guid? ImageDocumentId { get; set; }
    public string? ImageFileName { get; set; }

    // ---- vigência ----------------------------------------------------------
    /// <summary>Primeiro dia em que aparece (inclusive).</summary>
    public DateOnly StartsOn { get; set; }
    /// <summary>Último dia em que aparece (inclusive) — comunicado não é permanente.</summary>
    public DateOnly EndsOn { get; set; }
    /// <summary>Desligar sem apagar: tira do ar sem perder o histórico de quem já leu.</summary>
    public bool Active { get; set; } = true;

    public Guid CreatedBy { get; set; }
    public string CreatedByLabel { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<AnnouncementDismissal> Dismissals { get; set; } = [];

    /// <summary>No ar hoje: ligado e dentro da vigência, com as duas pontas inclusivas.</summary>
    public bool IsCurrent(DateOnly today) => Active && today >= StartsOn && today <= EndsOn;
}

/// <summary>
/// "Já li este" — por pessoa, e no servidor.
///
/// Guardar no navegador seria mais simples e estaria errado: o comunicado voltaria
/// a aparecer quando a pessoa entrasse de outro computador ou limpasse o navegador,
/// e o administrador não teria como saber quantos leram. É registro de leitura,
/// não preferência de tela.
/// </summary>
public class AnnouncementDismissal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnnouncementId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset DismissedAt { get; set; }
}
