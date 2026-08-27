using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Catalog;

/// <summary>Opções da importação, aplicadas a todas as linhas da planilha.</summary>
public record ImportOptions(
    string Family, string? ProductType, bool StockControlled, bool Purchasable,
    decimal? MinimumQty, string? Unit, IReadOnlyList<string>? Sizes);

public record ImportRow(int Line, string Code, string Description, string? Size, string Status, string? Message);

public record ImportResult(
    int TotalLines, int ToCreate, int Duplicates, int Errors, bool Committed,
    IReadOnlyList<ImportRow> Rows, IReadOnlyList<string> Warnings);

/// <summary>
/// Importação de produtos por planilha (MMS-002): a planilha traz código e descrição;
/// família, tipo e uso vêm da tela e valem para o arquivo inteiro. Itens com grade de
/// tamanhos (EPI e Fardamento) geram uma variante por tamanho — o saldo do almoxarifado
/// é por tamanho, então cada um é um item com código próprio (12003-P, 12003-M…).
/// </summary>
public class CatalogImportService(AppDbContext db, TimeProvider clock)
{
    /// <summary>Grades sugeridas na tela; qualquer lista informada é aceita.</summary>
    public static readonly string[] LetterSizes = ["PP", "P", "M", "G", "GG", "XG", "XXG"];
    public static readonly string[] NumberSizes =
        ["34", "35", "36", "37", "38", "39", "40", "41", "42", "43", "44", "45", "46"];

    private static readonly string[] CodeHeaders = ["PRODUTO", "CODIGO", "CÓDIGO", "COD", "SKU", "ITEM"];
    private static readonly string[] DescriptionHeaders = ["DESCRICAO", "DESCRIÇÃO", "DESCRICAO DO PRODUTO", "NOME", "PRODUTO"];
    private static readonly string[] SizeHeaders = ["TAMANHO", "TAMANHOS", "GRADE"];
    private static readonly string[] UnitHeaders = ["UNIDADE", "UN", "UNID", "UND"];
    private static readonly string[] PriceHeaders = ["PRECO", "PREÇO", "VALOR", "PRECO REF", "PREÇO REF"];

    public async Task<(ImportResult? result, UserError? error)> ImportAsync(
        Guid actorId, List<string[]> rows, ImportOptions options, bool commit, CancellationToken ct = default)
    {
        if (rows.Count == 0) return (null, new("IMP-ERR-010", "A planilha está vazia."));

        var family = options.Family.Trim().ToUpperInvariant();
        if (family.Length < 3) return (null, new("IMP-ERR-011", "Escolha a família dos produtos da planilha."));
        if (await db.ProductFamilies.AnyAsync(ct)
            && !await db.ProductFamilies.AnyAsync(f => f.Name == family && f.Active, ct))
            return (null, new("IC-ERR-022", $"Família \"{family}\" não cadastrada — cadastre em Cadastros → Famílias de Produtos."));

        var type = string.IsNullOrWhiteSpace(options.ProductType) ? null : options.ProductType.Trim().ToUpperInvariant();
        if (type is not null && !ProductTypes.IsValid(type))
            return (null, new("IC-ERR-025", "Tipo de produto inválido."));

        var map = DetectColumns(rows[0], out var hasHeader);
        if (map.Code < 0 || map.Description < 0)
            return (null, new("IMP-ERR-012",
                "Não encontrei as colunas de código e descrição. A planilha deve ter as colunas 'Produto' e 'Descrição'."));

        var defaultSizes = (options.Sizes ?? [])
            .Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0).Distinct().ToList();
        var existing = await db.CatalogItems.Select(i => i.Code).ToListAsync(ct);
        var known = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var result = new List<ImportRow>();
        var warnings = new List<string>();
        var toCreate = new List<CatalogItem>();
        var now = clock.GetUtcNow();

        for (var index = hasHeader ? 1 : 0; index < rows.Count; index++)
        {
            var line = index + 1;
            var cells = rows[index];
            var code = Cell(cells, map.Code);
            var description = Cell(cells, map.Description);
            if (code.Length == 0 && description.Length == 0) continue;   // linha em branco

            if (code.Length == 0) { result.Add(new(line, code, description, null, "ERRO", "Linha sem código do produto.")); continue; }
            if (description.Length < 3) { result.Add(new(line, code, description, null, "ERRO", "Descrição ausente ou muito curta.")); continue; }

            var rowSizes = SplitSizes(Cell(cells, map.Size));
            var sizes = rowSizes.Count > 0 ? rowSizes : defaultSizes;
            var unit = Cell(cells, map.Unit);
            if (unit.Length == 0) unit = string.IsNullOrWhiteSpace(options.Unit) ? "UN" : options.Unit.Trim();
            decimal? price = decimal.TryParse(Cell(cells, map.Price).Replace("R$", "").Trim(),
                System.Globalization.NumberStyles.Any, new System.Globalization.CultureInfo("pt-BR"), out var p) ? p : null;

            foreach (var variant in sizes.Count > 0 ? sizes : [null])
            {
                var finalCode = (variant is null ? code : $"{code}-{variant}").ToUpperInvariant();
                var finalDescription = variant is null ? description : $"{description} — Tam. {variant}";
                if (!known.Add(finalCode))
                {
                    result.Add(new(line, finalCode, finalDescription, variant, "DUPLICADO", "Já existe um produto com este código."));
                    continue;
                }
                result.Add(new(line, finalCode, finalDescription, variant, "NOVO", null));
                toCreate.Add(new CatalogItem
                {
                    Code = finalCode,
                    Description = finalDescription,
                    Family = family,
                    UnitOfMeasure = unit.ToUpperInvariant(),
                    ReferencePrice = price,
                    StockControlled = true,     // uso deixou de ser marcado na tela (revisão 2026-08-26)
                    Purchasable = true,
                    MinimumQty = options.MinimumQty,
                    ProductType = type,
                    BaseCode = variant is null ? null : code.ToUpperInvariant(),
                    Size = variant,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedBy = actorId,
                });
            }
        }

        if (ProductTypes.RequiresCa(type) && toCreate.Count > 0)
            warnings.Add("EPI/EPC exigem o C.A.: os itens entram com pendência e só poderão ser solicitados depois que o C.A. for informado no fornecedor do produto — o mesmo produto tem um C.A. por fornecedor.");
        if (defaultSizes.Count > 0)
            warnings.Add($"Grade aplicada: {string.Join(", ", defaultSizes)} — cada tamanho vira um item com código próprio (ex.: {result.FirstOrDefault(r => r.Status == "NOVO")?.Code ?? "12003-P"}).");

        if (commit && toCreate.Count > 0)
        {
            db.CatalogItems.AddRange(toCreate);
            await db.SaveChangesAsync(ct);
        }

        return (new ImportResult(
            TotalLines: rows.Count - (hasHeader ? 1 : 0),
            ToCreate: toCreate.Count,
            Duplicates: result.Count(r => r.Status == "DUPLICADO"),
            Errors: result.Count(r => r.Status == "ERRO"),
            Committed: commit,
            Rows: result,
            Warnings: warnings), null);
    }

    private record ColumnMap(int Code, int Description, int Size, int Unit, int Price);

    /// <summary>Descobre as colunas pelo cabeçalho. Sem um cabeçalho reconhecido a importação é recusada,
    /// para não transformar a primeira linha da planilha em produto.</summary>
    private static ColumnMap DetectColumns(string[] header, out bool hasHeader)
    {
        int Find(string[] names, int skip = -1)
        {
            for (var i = 0; i < header.Length; i++)
            {
                if (i == skip) continue;
                var value = Normalize(header[i]);
                if (names.Any(n => Normalize(n) == value)) return i;
            }
            return -1;
        }

        var code = Find(CodeHeaders);
        var description = Find(DescriptionHeaders, skip: code);
        hasHeader = code >= 0 || description >= 0;
        if (!hasHeader) return new(-1, -1, -1, -1, -1);
        if (code < 0) code = 0;
        if (description < 0) description = code == 0 ? 1 : 0;
        return new(code, description, Find(SizeHeaders), Find(UnitHeaders), Find(PriceHeaders));
    }

    private static string Cell(string[] cells, int index) =>
        index >= 0 && index < cells.Length ? (cells[index] ?? "").Trim() : "";

    private static List<string> SplitSizes(string raw) =>
        raw.Split([',', ';', '/', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant()).Distinct().ToList();

    private static string Normalize(string s) =>
        new string(s.Trim().ToUpperInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
}
