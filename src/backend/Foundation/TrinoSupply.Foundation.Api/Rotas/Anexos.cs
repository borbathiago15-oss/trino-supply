using System.Security.Claims;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// Gravação de anexo, compartilhada por todo mundo que aceita arquivo: O.C. do
/// ERP, nota fiscal, solicitação de compra, proposta e documento de fornecedor.
///
/// É um lugar só de propósito — o limite de tamanho e a lista de tipos aceitos
/// valem igual para todos, e um caminho de upload que escapasse daqui nasceria
/// sem eles.
/// </summary>
public static class Anexos
{
    public static async Task<(StoredDocument? doc, UserError? error)> StoreUploadAsync(
        HttpRequest request, AppDbContext db, TimeProvider clock, ClaimsPrincipal p,
        string entityType, Guid entityId)
    {
        if (!request.HasFormContentType) return (null, new("DOC-ERR-001", "Envie o arquivo como multipart/form-data."));
        var form = await request.ReadFormAsync();
        var file = form.Files.FirstOrDefault();
        if (file is null || file.Length == 0) return (null, new("DOC-ERR-001", "Nenhum arquivo enviado."));
        if (file.Length > StoredDocument.MaxSizeBytes) return (null, new("DOC-ERR-002", "Arquivo acima de 10 MB."));
        if (!StoredDocument.AllowedContentTypes.Contains(file.ContentType))
            return (null, new("DOC-ERR-003", "Formato não permitido: envie PDF, planilha (XLSX/XLS/CSV), imagem ou DOCX."));

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var conteudo = ms.ToArray();
        // o Content-Type é escrito por quem envia: a lista acima só barra quem é honesto.
        // A palavra final é dos primeiros bytes do arquivo (DOC-ERR-004).
        if (!AssinaturaDeArquivo.Confere(file.ContentType, conteudo))
            return (null, new("DOC-ERR-004",
                "O conteúdo do arquivo não corresponde ao formato declarado. Envie o arquivo original."));

        var doc = new StoredDocument
        {
            FileName = Path.GetFileName(file.FileName), ContentType = file.ContentType, SizeBytes = file.Length,
            Content = conteudo, EntityType = entityType, EntityId = entityId,
            UploadedByLabel = p.FindFirstValue("name") ?? "Suprimentos", UploadedAt = clock.GetUtcNow(),
        };
        db.StoredDocuments.Add(doc);
        return (doc, null);
    }
}
