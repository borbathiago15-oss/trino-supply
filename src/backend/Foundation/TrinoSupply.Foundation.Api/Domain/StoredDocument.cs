namespace TrinoSupply.Foundation.Api.Domain;

/// <summary>
/// Infraestrutura documental mínima (RFQ-001 §7): anexos de propostas e PDFs de OC.
/// Conteúdo em bytea (≤10 MB); download sempre autorizado por papel/vínculo.
/// </summary>
public class StoredDocument
{
    public const long MaxSizeBytes = 10 * 1024 * 1024;
    public static readonly string[] AllowedContentTypes =
    [
        "application/pdf", "image/png", "image/jpeg",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",   // .xlsx
        "application/vnd.ms-excel",                                            // .xls
        "text/csv", "application/csv",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    ];

    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public byte[] Content { get; set; } = [];
    public string EntityType { get; set; } = string.Empty;   // PROPOSAL | PURCHASE_ORDER_PDF
    public Guid EntityId { get; set; }
    public Guid? SupplierId { get; set; }                    // dono, quando anexado pelo portal
    public string UploadedByLabel { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; }
}

/// <summary>
/// Cadastro da empresa compradora — cabeçalho e política de pagamento do PDF da OC
/// (modelo oficial do Grupo Trino). Linha única, mantida pelo administrador.
/// </summary>
public class CompanyProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LegalName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;      // logradouro
    public string? District { get; set; }                    // bairro/distrito
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;        // UF
    public string Zip { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;        // CNPJ
    public string? StateRegistration { get; set; }           // Inscr. Est.
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? DeliveryAddress { get; set; }             // dados de entrega (linha completa)
    public string? DeliveryTaxId { get; set; }
    public string? StandardClauses { get; set; }             // cláusulas padrão da OC
    public string? PaymentPolicy { get; set; }               // política de pagamento (página 2 do modelo)
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedByLabel { get; set; } = string.Empty;
}
