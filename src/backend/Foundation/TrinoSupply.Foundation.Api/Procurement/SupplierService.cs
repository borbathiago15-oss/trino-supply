using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>Serviço de fornecedores (SUP-001 MVP).</summary>
public class SupplierService(AppDbContext db, TimeProvider clock)
{
    public static bool CanMaintain(string role) =>
        role is Roles.PurchasingOfficer or Roles.SupplyManager or Roles.SystemAdministrator;

    public static bool CanView(string role) => CanMaintain(role) || role == Roles.Auditor;

    public async Task<List<Supplier>> ListAsync(bool includeInactive, CancellationToken ct = default) =>
        (await BuscarAsync(includeInactive, null, 500, ct: ct)).Itens;

    /// <summary>
    /// Uma página de fornecedores.
    /// </summary>
    /// <param name="Itens">Os fornecedores da página.</param>
    /// <param name="Total">
    /// Quantos existem no recorte inteiro — a tela usa para distinguir "não existe" de
    /// "não veio nesta página" (PO-BR-012).
    /// </param>
    /// <param name="Duplicados">
    /// Por fornecedor da página, quantos cadastros <b>ativos</b> dividem a razão social dele.
    /// Dois ou mais é duplicata. Conta só os ativos de propósito: inativar o registro repetido
    /// é o que resolve o caso (ele não é convidado nem vence), e contar o inativo deixaria a
    /// marca acesa depois do trabalho feito — limpeza sem fim visível ninguém termina.
    /// </param>
    /// <param name="TotalDuplicados">
    /// Quantos fornecedores ativos do cadastro <b>inteiro</b> estão em alguma duplicata. Vem
    /// de fora da página porque o aviso do topo promete um número: contá-lo só do que coube
    /// na página diria "2" onde há sete.
    /// </param>
    public record PaginaDeFornecedores(
        List<Supplier> Itens, int Total, IReadOnlyDictionary<Guid, int> Duplicados, int TotalDuplicados);

    /// <summary>
    /// Página de fornecedores, com a busca feita no banco.
    /// </summary>
    /// <param name="somenteDuplicados">
    /// Recorta a lista nos cadastros repetidos. Sem ele, achar sete duplicatas entre trezentas
    /// linhas é caçada — e o aviso que não abre a lista dele é só um número.
    /// </param>
    public async Task<PaginaDeFornecedores> BuscarAsync(
        bool includeInactive, string? busca = null, int tamanho = 100, bool somenteDuplicados = false,
        CancellationToken ct = default)
    {
        // a razão social repetida do cadastro que existia antes do SUP-ERR-015; a chave se
        // compara em memória (acento e pontuação não se comparam em SQL sem extensão), e o
        // custo é uma projeção de duas colunas sobre uma tabela de fornecedores
        var ativos = await db.Suppliers.Where(s => s.Active)
            .Select(s => new { s.Id, s.LegalName }).ToListAsync(ct);
        var repetidos = ativos.GroupBy(s => ChaveDoNome(s.LegalName))
            .Where(g => g.Key.Length > 0 && g.Count() > 1)
            .SelectMany(g => g.Select(s => new { s.Id, Quantos = g.Count() }))
            .ToDictionary(x => x.Id, x => x.Quantos);

        var termo = busca?.Trim();
        var q = db.Suppliers.Include(s => s.ContractItems).Include(s => s.Documents).AsQueryable();
        if (!includeInactive) q = q.Where(s => s.Active);
        if (somenteDuplicados)
        {
            var ids = repetidos.Keys.ToList();
            q = q.Where(s => ids.Contains(s.Id));
        }
        if (!string.IsNullOrEmpty(termo))
        {
            // o CNPJ é gravado só com dígitos, então "12.345" tem que achar "12345"
            var digitos = new string(termo.Where(char.IsDigit).ToArray());
            q = q.Where(s =>
                EF.Functions.ILike(s.LegalName, $"%{termo}%")
                || (s.TradeName != null && EF.Functions.ILike(s.TradeName, $"%{termo}%"))
                || (s.Email != null && EF.Functions.ILike(s.Email, $"%{termo}%"))
                || (s.Phone != null && EF.Functions.ILike(s.Phone, $"%{termo}%"))
                || (s.TaxId != null && EF.Functions.ILike(s.TaxId, $"%{termo}%"))
                || (digitos.Length > 0 && s.TaxId != null && EF.Functions.ILike(s.TaxId, $"%{digitos}%")));
        }

        var total = await q.CountAsync(ct);
        var list = await q.OrderBy(s => s.LegalName).Take(Math.Clamp(tamanho, 1, 500)).ToListAsync(ct);

        await PreencherConsumoAsync(list, ct);
        return new(list, total, repetidos, repetidos.Count);
    }

    /// <summary>
    /// O consumo do contrato (teto − O.C.s na vigência), calculado em uma consulta só para a
    /// lista inteira. A ficha usa a mesma conta: o saldo da linha e o da ficha não podem discordar.
    /// </summary>
    private async Task PreencherConsumoAsync(List<Supplier> list, CancellationToken ct)
    {
        var comTeto = list.Where(s => s.ContractValueLimit is not null && s.ContractItems.Count > 0).ToList();
        if (comTeto.Count == 0) return;
        var ids = comTeto.Select(s => s.Id).ToArray();
        var somas = (await db.PurchaseOrders
                .Where(o => ids.Contains(o.SupplierId) && o.Status != PurchaseOrderStatus.Cancelled)
                .Select(o => new { o.SupplierId, o.TotalValue, o.CreatedAt }).ToListAsync(ct))
            .GroupBy(o => o.SupplierId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var s in comTeto)
            s.ContractConsumed = somas.TryGetValue(s.Id, out var os)
                ? os.Where(o => NaVigencia(s, o.CreatedAt)).Sum(o => o.TotalValue)
                : 0m;
    }

    /// <summary>A O.C. conta no contrato quando foi emitida dentro da vigência dele.</summary>
    public static bool NaVigencia(Supplier s, DateTimeOffset emitidaEm)
    {
        var i0 = s.ContractValidFrom?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;
        var f0 = s.ContractValidUntil?.ToDateTime(TimeOnly.MaxValue) ?? DateTime.MaxValue;
        return emitidaEm.UtcDateTime >= i0 && emitidaEm.UtcDateTime <= f0;
    }

    // ==== ficha do contrato: documentos, compras e história num lugar só ============

    public sealed record PedidoDoContrato(Guid Id, string Number, string? ErpNumber, DateTimeOffset CreatedAt,
        decimal Total, PurchaseOrderStatus Status, string? QuotationNumber, string? SourcePrNumber, bool NaVigencia);

    /// <summary>Um acontecimento da linha do tempo: alteração, documento ou reajuste.</summary>
    public sealed record AcontecimentoDoContrato(DateTimeOffset Quando, string Tipo, string Texto, string Quem);

    public sealed record FichaDoContrato(Supplier Fornecedor, IReadOnlyList<PedidoDoContrato> Pedidos,
        IReadOnlyList<AcontecimentoDoContrato> LinhaDoTempo, decimal CustoEvitado, bool HistoricoCompleto);

    /// <summary>
    /// Tudo o que se sabe do contrato de um fornecedor. As compras são <b>todas</b> as dele, e
    /// não só as da vigência: a O.C. de antes do contrato é justamente o que mostra quanto se
    /// pagava sem ele — cada linha diz se conta no saldo. A linha do tempo junta as alterações
    /// do contrato, os documentos dele e os reajustes, do mais novo para o mais antigo.
    /// </summary>
    public async Task<FichaDoContrato?> FichaAsync(Guid supplierId, CancellationToken ct = default)
    {
        var s = await db.Suppliers.Include(x => x.ContractItems).Include(x => x.Documents)
            .SingleOrDefaultAsync(x => x.Id == supplierId, ct);
        if (s is null) return null;
        await PreencherConsumoAsync([s], ct);

        var pedidos = (await db.PurchaseOrders.Where(o => o.SupplierId == supplierId)
                .OrderByDescending(o => o.CreatedAt).Take(200)
                .Select(o => new { o.Id, o.Number, o.ErpNumber, o.CreatedAt, o.TotalValue, o.Status, o.QuotationNumber, o.SourcePrNumber })
                .ToListAsync(ct))
            .Select(o => new PedidoDoContrato(o.Id, o.Number, o.ErpNumber, o.CreatedAt, o.TotalValue, o.Status,
                o.QuotationNumber, o.SourcePrNumber,
                s.ContractItems.Count > 0 && o.Status != PurchaseOrderStatus.Cancelled && NaVigencia(s, o.CreatedAt)))
            .ToList();

        var eventos = await db.SupplierContractEvents.Where(e => e.SupplierId == supplierId)
            .OrderByDescending(e => e.CreatedAt).Take(200).ToListAsync(ct);
        var reajustes = await db.ContractAdjustments.Where(a => a.SupplierId == supplierId)
            .OrderByDescending(a => a.CreatedAt).Take(200).ToListAsync(ct);
        var br = CultureInfo.GetCultureInfo("pt-BR");
        var linha = eventos.Select(e => new AcontecimentoDoContrato(e.CreatedAt, e.Kind, e.Summary, e.CreatedByLabel))
            .Concat(reajustes.Select(a => new AcontecimentoDoContrato(a.CreatedAt, "REAJUSTE",
                $"Reajuste pleiteado de {a.RequestedPercent.ToString("0.##", br)}%, fechado em {a.AgreedPercent.ToString("0.##", br)}% — "
                + $"custo evitado de {a.CostAvoidance.ToString("C", br)} sobre {a.BaseValue.ToString("C", br)} comprados em 12 meses"
                + (a.AppliedToPrices ? "; preços do contrato reajustados" : "") + (a.Notes is null ? "." : $". {a.Notes}"),
                a.CreatedByLabel)))
            .OrderByDescending(x => x.Quando).ToList();

        // o registro das alterações começou com a ficha: o contrato cadastrado antes dela não tem
        // o "cadastrado" na linha do tempo, e a tela diz isso em vez de deixar parecer que a
        // história começa na primeira alteração registrada
        var completo = eventos.Any(e => e.Kind == EventosDoContrato.Criado);
        return new FichaDoContrato(s, pedidos, linha, reajustes.Sum(a => a.CostAvoidance), completo);
    }

    /// <summary>Só os dígitos de um documento/telefone — nulo quando não sobra nenhum.</summary>
    private static string? SoDigitos(string? valor)
    {
        var d = new string((valor ?? "").Where(char.IsDigit).ToArray());
        return d.Length == 0 ? null : d;
    }

    /// <summary>
    /// Chave de comparação da razão social: maiúsculas, sem acento, só letras e dígitos.
    /// "Pontes Tour", "PONTES  TOUR." e "Pontes Tóur" viram a mesma coisa — que é o que
    /// se quer, porque são a mesma empresa digitada por pessoas diferentes em dias
    /// diferentes. O que ela <b>não</b> faz é remover "LTDA", "ME" ou "EIRELI": recusar
    /// "Alfa Ltda" porque existe "Alfa ME" barraria cadastro legítimo, e um bloqueio que
    /// atrapalha o trabalho certo acaba contornado por fora.
    /// </summary>
    public static string ChaveDoNome(string? nome)
    {
        var texto = (nome ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// O fornecedor que já ocupa esta razão social, ou nulo. A comparação é da chave, e
    /// por isso é feita em memória: acentuação e pontuação não se comparam em SQL sem
    /// extensão, e a alternativa — guardar uma coluna normalizada — não caberia num
    /// índice único enquanto as duplicatas de hoje existirem. Cadastrar fornecedor é
    /// operação rara e a projeção são três colunas, então o custo é honesto.
    /// </summary>
    public async Task<Supplier?> PorRazaoSocialAsync(string? legalName, CancellationToken ct = default)
    {
        var chave = ChaveDoNome(legalName);
        if (chave.Length == 0) return null;
        var candidatos = await db.Suppliers.Select(s => new { s.Id, s.LegalName }).ToListAsync(ct);
        var achado = candidatos.FirstOrDefault(s => ChaveDoNome(s.LegalName) == chave);
        return achado is null ? null : await db.Suppliers.SingleOrDefaultAsync(s => s.Id == achado.Id, ct);
    }

    /// <summary>
    /// Cadastro de fornecedor. O mínimo é **razão social + telefone**: é com isso
    /// que o comprador pede preço antes de existir cadastro nenhum (§7). O CPF/CNPJ
    /// é opcional aqui e obrigatório para homologar — cotar sem ele pode, vencer não.
    ///
    /// <para>Por ser opcional, o CNPJ não pode ser o único guarda contra duplicata
    /// (<c>SUP-ERR-010</c>): o pré-cadastro nasce sem ele, e cadastrar de novo o mesmo
    /// fornecedor "agora com CNPJ" criava um segundo registro — o primeiro seguia
    /// PROSPECT, preso à cotação que o convidou, enquanto o segundo era homologado.
    /// A razão social passa a fechar essa porta (<c>SUP-ERR-015</c>), e o erro diz qual
    /// cadastro já existe para o caminho ser completar aquele, não criar outro.</para>
    /// </summary>
    public async Task<(Supplier? supplier, UserError? error)> CreateAsync(
        Guid actorId, string legalName, string? tradeName, string? taxId, string? email, string? phone,
        CancellationToken ct = default)
    {
        legalName = (legalName ?? "").Trim();
        if (legalName.Length < 3) return (null, new("SUP-ERR-012", "Informe a razão social (mín. 3 caracteres)."));
        if ((SoDigitos(phone)?.Length ?? 0) < 10)
            return (null, new("SUP-ERR-014", "Informe o telefone com DDD (mín. 10 dígitos)."));

        var digits = SoDigitos(taxId);
        if (digits is not null)
        {
            if (digits.Length is not (11 or 14))
                return (null, new("SUP-ERR-011", "CPF/CNPJ inválido: informe 11 ou 14 dígitos."));
            if (await db.Suppliers.AnyAsync(s => s.TaxId == digits, ct))
                return (null, new("SUP-ERR-010", "Já existe um fornecedor com este CPF/CNPJ."));
        }

        // o CNPJ já foi conferido acima; sobrou o cadastro repetido pelo nome, que é o
        // que o pré-cadastro sem documento deixava passar
        var homonimo = await PorRazaoSocialAsync(legalName, ct);
        if (homonimo is not null)
            return (null, new("SUP-ERR-015", $"Já existe um fornecedor com esta razão social: {homonimo.LegalName}"
                + (!homonimo.Active
                    ? ", hoje inativo. Reative o cadastro existente em vez de criar outro."
                    : homonimo.TaxId is null
                        ? ", ainda como pré-cadastro. Edite-o para informar o CPF/CNPJ em vez de criar outro."
                        : ". Edite o cadastro existente em vez de criar outro.")));

        var now = clock.GetUtcNow();
        var supplier = new Supplier
        {
            LegalName = legalName,
            TradeName = string.IsNullOrWhiteSpace(tradeName) ? null : tradeName.Trim(),
            TaxId = digits,
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            Phone = phone!.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = actorId,
        };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(ct);
        return (supplier, null);
    }

    public async Task<(Supplier? supplier, UserError? error)> UpdateAsync(
        Guid id, string? tradeName, string? email, string? phone, bool? active,
        string? taxId = null, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return (null, new("SUP-ERR-404", "Fornecedor não encontrado."));

        // o CNPJ só entra uma vez: o pré-cadastro nasce sem ele e a edição completa
        // o registro. Já gravado, ele é identidade — trocar viraria outro fornecedor.
        var novoTaxId = SoDigitos(taxId);
        if (novoTaxId is not null && supplier.TaxId is null)
        {
            if (novoTaxId.Length is not (11 or 14))
                return (null, new("SUP-ERR-011", "CPF/CNPJ inválido: informe 11 ou 14 dígitos."));
            if (await db.Suppliers.AnyAsync(s => s.TaxId == novoTaxId, ct))
                return (null, new("SUP-ERR-010", "Já existe um fornecedor com este CPF/CNPJ."));
            supplier.TaxId = novoTaxId;
        }

        if (tradeName is not null) supplier.TradeName = string.IsNullOrWhiteSpace(tradeName) ? null : tradeName.Trim();
        if (email is not null) supplier.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (phone is not null) supplier.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (active is not null) supplier.Active = active.Value;
        supplier.UpdatedAt = clock.GetUtcNow();
        supplier.Version += 1;
        await db.SaveChangesAsync(ct);
        return (supplier, null);
    }

    // ---- homologação e certidões (V2-P2) ------------------------------------
    /// <summary>Homologação é decisão de gestão: gestor de suprimentos ou administrador.</summary>
    public static bool CanHomologate(string role) =>
        role is Roles.SupplyManager or Roles.SystemAdministrator;

    public async Task<(Supplier? supplier, UserError? error)> SetHomologationAsync(
        Guid id, string? status, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.Include(s => s.Documents).Include(s => s.ContractItems)
            .SingleOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return (null, new("SUP-ERR-404", "Fornecedor não encontrado."));
        var clean = (status ?? "").Trim().ToUpperInvariant();
        if (!SupplierHomologation.All.Contains(clean))
            return (null, new("SUP-ERR-031", "Situação de homologação inválida."));
        // pré-cadastro cota; homologado fecha compra. O CNPJ é a fronteira entre os dois:
        // sem ele não há O.C., nota nem retenção — então não há homologação (§7).
        if (clean == SupplierHomologation.Homologado && supplier.TaxId is null)
            return (null, new("SUP-ERR-013",
                "Fornecedor sem CPF/CNPJ: complete o cadastro antes de homologar."));
        supplier.HomologationStatus = clean;
        supplier.UpdatedAt = clock.GetUtcNow();
        supplier.Version += 1;
        await db.SaveChangesAsync(ct);
        return (supplier, null);
    }

    public static readonly string[] DocumentTypes = SupplierDocumentTypes.All;

    public async Task<(SupplierDocument? doc, UserError? error)> AddDocumentAsync(
        Guid supplierId, string? type, string? label, DateOnly? validUntil,
        Guid storedDocumentId, string fileName, string uploadedByLabel, CancellationToken ct = default)
    {
        if (!await db.Suppliers.AnyAsync(s => s.Id == supplierId, ct))
            return (null, new("SUP-ERR-404", "Fornecedor não encontrado."));
        var t = (type ?? "OUTRO").Trim().ToUpperInvariant();
        if (!DocumentTypes.Contains(t))
            return (null, new("SUP-ERR-032", "Tipo de documento inválido (CND_FEDERAL, FGTS, CNDT, CONTRATO_SOCIAL, CONTRATO, ADITIVO ou OUTRO)."));
        var doc = new SupplierDocument
        {
            SupplierId = supplierId, Type = t,
            Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
            DocumentId = storedDocumentId, FileName = fileName,
            ValidUntil = validUntil, UploadedByLabel = uploadedByLabel,
            CreatedAt = clock.GetUtcNow(),
        };
        db.SupplierDocuments.Add(doc);
        if (!doc.IsCertificate)
            Registrar(supplierId, EventosDoContrato.DocumentoAnexado,
                $"{NomeDoDocumento(doc)} anexado: {doc.FileName}.", null, uploadedByLabel);
        await db.SaveChangesAsync(ct);
        return (doc, null);
    }

    private static string NomeDoDocumento(SupplierDocument d) =>
        (d.Type == SupplierDocumentTypes.Aditivo ? "Aditivo" : "Contrato assinado")
        + (d.Label is null ? "" : $" ({d.Label})");

    public async Task<UserError?> RemoveDocumentAsync(Guid supplierId, Guid docId, CancellationToken ct = default,
        Actor? actor = null)
    {
        var doc = await db.SupplierDocuments.SingleOrDefaultAsync(d => d.Id == docId && d.SupplierId == supplierId, ct);
        if (doc is null) return new("SUP-ERR-404", "Documento não encontrado.");
        db.SupplierDocuments.Remove(doc);
        // o papel do contrato sai da lista, mas não da história: sem isto, "quem tirou o aditivo?"
        // não teria resposta
        if (!doc.IsCertificate)
            Registrar(supplierId, EventosDoContrato.DocumentoRemovido,
                $"{NomeDoDocumento(doc)} removido: {doc.FileName}.", actor?.Id, actor?.Label ?? "Cadastro");
        await db.SaveChangesAsync(ct);
        return null;
    }

    // ---- contrato de parceria (produtos com preço e prazos fixos) -----------
    public record ContractItemInput(Guid? CatalogItemId, string? Description, string? CatalogCode,
        string? UnitOfMeasure, decimal UnitPrice, string? PaymentTerms, int? PaymentDays, int? DeliveryDays,
        string? Notes);

    /// <summary>
    /// Regrava o contrato do fornecedor: vigência e a lista de produtos contratados.
    /// Passar a lista vazia encerra o contrato (o fornecedor volta a ser cotado normalmente).
    /// </summary>
    public async Task<(Supplier? supplier, UserError? error)> SaveContractAsync(
        Guid id, string? number, DateOnly? validFrom, DateOnly? validUntil, string? notes,
        IReadOnlyList<ContractItemInput>? items, decimal? valueLimit = null, CancellationToken ct = default,
        Actor? actor = null)
    {
        var supplier = await db.Suppliers.Include(s => s.ContractItems).SingleOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return (null, new("SUP-ERR-404", "Fornecedor não encontrado."));
        var antes = RetratoDoContrato.De(supplier);
        if (validFrom is not null && validUntil is not null && validUntil < validFrom)
            return (null, new("SUP-ERR-020", "A vigência do contrato termina antes de começar."));

        if (valueLimit is < 0) return (null, new("CT-ERR-011", "O teto financeiro do contrato não pode ser negativo."));
        supplier.ContractNumber = Clean(number);
        supplier.ContractValueLimit = valueLimit;
        supplier.ContractValidFrom = validFrom;
        supplier.ContractValidUntil = validUntil;
        supplier.ContractNotes = Clean(notes);

        IReadOnlyList<SupplierContractItem> itensDepois = supplier.ContractItems.ToList();
        if (items is not null)
        {
            var catalogIds = items.Where(i => i.CatalogItemId is not null).Select(i => i.CatalogItemId!.Value).Distinct().ToList();
            var catalog = await db.CatalogItems.Where(c => catalogIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
            var novos = new List<SupplierContractItem>();
            var now = clock.GetUtcNow();
            foreach (var input in items)
            {
                var doCatalogo = input.CatalogItemId is not null && catalog.TryGetValue(input.CatalogItemId.Value, out var c) ? c : null;
                if (input.CatalogItemId is not null && doCatalogo is null)
                    return (null, new("SUP-ERR-021", "Produto do contrato não existe no catálogo."));
                var descricao = Clean(input.Description) ?? doCatalogo?.Description;
                if (string.IsNullOrWhiteSpace(descricao))
                    return (null, new("SUP-ERR-021", "Informe o produto de cada linha do contrato."));
                if (input.UnitPrice <= 0)
                    return (null, new("SUP-ERR-022", $"O preço contratado de '{descricao}' precisa ser maior que zero."));
                if (input.PaymentDays is < 0 or > 365 || input.DeliveryDays is < 0 or > 365)
                    return (null, new("SUP-ERR-023", "Os prazos do contrato vão de 0 a 365 dias."));
                novos.Add(new SupplierContractItem
                {
                    SupplierId = supplier.Id,
                    CatalogItemId = input.CatalogItemId,
                    Description = descricao,
                    CatalogCode = Clean(input.CatalogCode) ?? doCatalogo?.Code,
                    UnitOfMeasure = Clean(input.UnitOfMeasure) ?? doCatalogo?.UnitOfMeasure ?? "UN",
                    UnitPrice = input.UnitPrice,
                    PaymentTerms = Clean(input.PaymentTerms),
                    PaymentDays = input.PaymentDays,
                    DeliveryDays = input.DeliveryDays,
                    Notes = Clean(input.Notes),
                    CreatedAt = now,
                });
            }
            if (supplier.ContractItems.Count > 0)
            {
                db.SupplierContractItems.RemoveRange(supplier.ContractItems);
                supplier.ContractItems.Clear();
            }
            db.SupplierContractItems.AddRange(novos);
            itensDepois = novos;
        }

        // os itens novos entram pelo DbSet (a navegação se preenche pelo fixup), então o retrato
        // de depois lê a lista que acabou de ser montada, e não a navegação
        var depois = RetratoDoContrato.De(supplier, itensDepois);
        if (RetratoDoContrato.Mudanca(antes, depois) is { } mudanca)
            Registrar(supplier.Id, mudanca.Kind, mudanca.Summary, actor?.Id, actor?.Label ?? "Cadastro");

        supplier.UpdatedAt = clock.GetUtcNow();
        supplier.Version += 1;
        await db.SaveChangesAsync(ct);
        return (await db.Suppliers.Include(s => s.ContractItems).SingleAsync(s => s.Id == id, ct), null);
    }

    // ==== pleito de reajuste do contrato (V2-P4 — Cost Avoidance) =================

    /// <summary>
    /// Registra o pleito de reajuste: o fornecedor pediu X%, fechou em Y%. O custo evitado
    /// ((X−Y)% sobre o consumo dos últimos 12 meses) é CONGELADO no registro — imutável.
    /// Opcionalmente aplica o % aceito aos preços dos produtos do contrato.
    /// </summary>
    public async Task<(ContractAdjustment? adjustment, UserError? error)> RegisterContractAdjustmentAsync(
        Actor actor, Guid supplierId, decimal requestedPercent, decimal agreedPercent,
        string? notes, bool applyToPrices, CancellationToken ct = default)
    {
        if (!CanMaintain(actor.Role))
            return (null, new("CT-ERR-900", "Seu papel não registra reajustes de contrato."));
        if (requestedPercent <= 0 || requestedPercent > 500)
            return (null, new("CT-ERR-020", "Informe o percentual pleiteado pelo fornecedor (maior que zero)."));
        if (agreedPercent < 0 || agreedPercent > requestedPercent)
            return (null, new("CT-ERR-021", "O percentual aceito vai de 0 até o pleiteado — acima disso não há custo evitado."));

        var supplier = await db.Suppliers.Include(s => s.ContractItems)
            .SingleOrDefaultAsync(s => s.Id == supplierId, ct);
        if (supplier is null) return (null, new("SUP-ERR-404", "Fornecedor não encontrado."));
        if (supplier.ContractItems.Count == 0)
            return (null, new("CT-ERR-022", "Cadastre o contrato de parceria (com produtos) antes de registrar reajuste."));

        var now = clock.GetUtcNow();
        var base12m = await db.PurchaseOrders
            .Where(o => o.SupplierId == supplierId && o.Status != PurchaseOrderStatus.Cancelled
                        && o.CreatedAt >= now.AddMonths(-12))
            .SumAsync(o => o.TotalValue, ct);
        var adjustment = new ContractAdjustment
        {
            SupplierId = supplierId,
            RequestedPercent = requestedPercent,
            AgreedPercent = agreedPercent,
            BaseValue = base12m,
            CostAvoidance = Math.Round((requestedPercent - agreedPercent) / 100m * base12m, 2),
            AppliedToPrices = applyToPrices && agreedPercent > 0,
            Notes = Clean(notes),
            CreatedBy = actor.Id,
            CreatedByLabel = actor.Label,
            CreatedAt = now,
        };
        db.ContractAdjustments.Add(adjustment);
        if (adjustment.AppliedToPrices)
            foreach (var item in supplier.ContractItems)
                item.UnitPrice = Math.Round(item.UnitPrice * (1 + agreedPercent / 100m), 4);
        supplier.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return (adjustment, null);
    }

    public Task<List<ContractAdjustment>> ContractAdjustmentsAsync(Guid supplierId, CancellationToken ct = default) =>
        db.ContractAdjustments.Where(a => a.SupplierId == supplierId)
            .OrderByDescending(a => a.CreatedAt).Take(50).ToListAsync(ct);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void Registrar(Guid supplierId, string kind, string summary, Guid? por, string porLabel) =>
        db.SupplierContractEvents.Add(new SupplierContractEvent
        {
            SupplierId = supplierId, Kind = kind,
            // um contrato de centenas de produtos renegociados de uma vez não cabe na coluna inteiro
            Summary = summary.Length <= 2000 ? summary : summary[..1997] + "…",
            CreatedBy = por, CreatedByLabel = porLabel, CreatedAt = clock.GetUtcNow(),
        });

    // ---- Portal do Fornecedor (RFQ-001 §5) ----------------------------------
    /// <summary>Gera nova chave de acesso ao portal; retorna a chave em claro UMA vez (persistido só o hash).</summary>
    public async Task<(string? key, UserError? error)> GeneratePortalKeyAsync(Guid id, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return (null, new("SUP-ERR-404", "Fornecedor não encontrado."));
        var key = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        supplier.PortalKeyHash = Auth.TokenService.HashRefreshToken(key);
        supplier.UpdatedAt = clock.GetUtcNow();
        supplier.Version += 1;
        await db.SaveChangesAsync(ct);
        return (key, null);
    }

    /// <summary>Login do portal: CNPJ/CPF + chave. Só fornecedores ativos com chave gerada.</summary>
    public async Task<Supplier?> PortalLoginAsync(string taxId, string accessKey, CancellationToken ct = default)
    {
        var digits = new string((taxId ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length is not (11 or 14) || string.IsNullOrWhiteSpace(accessKey)) return null;
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.TaxId == digits && s.Active, ct);
        if (supplier?.PortalKeyHash is null) return null;
        var hash = Auth.TokenService.HashRefreshToken(accessKey.Trim().ToUpperInvariant());
        return string.Equals(hash, supplier.PortalKeyHash, StringComparison.OrdinalIgnoreCase) ? supplier : null;
    }
}
