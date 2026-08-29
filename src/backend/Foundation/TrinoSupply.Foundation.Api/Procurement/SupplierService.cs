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

    public async Task<List<Supplier>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        var q = db.Suppliers.Include(s => s.ContractItems).Include(s => s.Documents).AsQueryable();
        if (!includeInactive) q = q.Where(s => s.Active);
        var list = await q.OrderBy(s => s.LegalName).Take(500).ToListAsync(ct);

        // consumo do contrato (teto − O.C.s na vigência), calculado em uma consulta só
        var comTeto = list.Where(s => s.ContractValueLimit is not null && s.ContractItems.Count > 0).ToList();
        if (comTeto.Count > 0)
        {
            var ids = comTeto.Select(s => s.Id).ToList();
            var somas = (await db.PurchaseOrders
                    .Where(o => ids.Contains(o.SupplierId) && o.Status != PurchaseOrderStatus.Cancelled)
                    .Select(o => new { o.SupplierId, o.TotalValue, o.CreatedAt }).ToListAsync(ct))
                .GroupBy(o => o.SupplierId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var s in comTeto)
            {
                var i0 = s.ContractValidFrom?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;
                var f0 = s.ContractValidUntil?.ToDateTime(TimeOnly.MaxValue) ?? DateTime.MaxValue;
                s.ContractConsumed = somas.TryGetValue(s.Id, out var os)
                    ? os.Where(o => o.CreatedAt.UtcDateTime >= i0 && o.CreatedAt.UtcDateTime <= f0).Sum(o => o.TotalValue)
                    : 0m;
            }
        }
        return list;
    }

    public async Task<(Supplier? supplier, UserError? error)> CreateAsync(
        Guid actorId, string legalName, string? tradeName, string taxId, string? email, string? phone,
        CancellationToken ct = default)
    {
        legalName = legalName.Trim();
        if (legalName.Length < 3) return (null, new("SUP-ERR-012", "Informe a razão social (mín. 3 caracteres)."));
        var digits = new string((taxId ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length is not (11 or 14))
            return (null, new("SUP-ERR-011", "CPF/CNPJ inválido: informe 11 ou 14 dígitos."));
        if (await db.Suppliers.AnyAsync(s => s.TaxId == digits, ct))
            return (null, new("SUP-ERR-010", "Já existe um fornecedor com este CPF/CNPJ."));

        var now = clock.GetUtcNow();
        var supplier = new Supplier
        {
            LegalName = legalName,
            TradeName = string.IsNullOrWhiteSpace(tradeName) ? null : tradeName.Trim(),
            TaxId = digits,
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = actorId,
        };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(ct);
        return (supplier, null);
    }

    public async Task<(Supplier? supplier, UserError? error)> UpdateAsync(
        Guid id, string? tradeName, string? email, string? phone, bool? active, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return (null, new("SUP-ERR-404", "Fornecedor não encontrado."));

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
        supplier.HomologationStatus = clean;
        supplier.UpdatedAt = clock.GetUtcNow();
        supplier.Version += 1;
        await db.SaveChangesAsync(ct);
        return (supplier, null);
    }

    public static readonly string[] DocumentTypes =
        ["CND_FEDERAL", "FGTS", "CNDT", "CONTRATO_SOCIAL", "OUTRO"];

    public async Task<(SupplierDocument? doc, UserError? error)> AddDocumentAsync(
        Guid supplierId, string? type, string? label, DateOnly? validUntil,
        Guid storedDocumentId, string fileName, string uploadedByLabel, CancellationToken ct = default)
    {
        if (!await db.Suppliers.AnyAsync(s => s.Id == supplierId, ct))
            return (null, new("SUP-ERR-404", "Fornecedor não encontrado."));
        var t = (type ?? "OUTRO").Trim().ToUpperInvariant();
        if (!DocumentTypes.Contains(t))
            return (null, new("SUP-ERR-032", "Tipo de documento inválido (CND_FEDERAL, FGTS, CNDT, CONTRATO_SOCIAL ou OUTRO)."));
        var doc = new SupplierDocument
        {
            SupplierId = supplierId, Type = t,
            Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
            DocumentId = storedDocumentId, FileName = fileName,
            ValidUntil = validUntil, UploadedByLabel = uploadedByLabel,
            CreatedAt = clock.GetUtcNow(),
        };
        db.SupplierDocuments.Add(doc);
        await db.SaveChangesAsync(ct);
        return (doc, null);
    }

    public async Task<UserError?> RemoveDocumentAsync(Guid supplierId, Guid docId, CancellationToken ct = default)
    {
        var doc = await db.SupplierDocuments.SingleOrDefaultAsync(d => d.Id == docId && d.SupplierId == supplierId, ct);
        if (doc is null) return new("SUP-ERR-404", "Documento não encontrado.");
        db.SupplierDocuments.Remove(doc);
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
        IReadOnlyList<ContractItemInput>? items, decimal? valueLimit = null, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.Include(s => s.ContractItems).SingleOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return (null, new("SUP-ERR-404", "Fornecedor não encontrado."));
        if (validFrom is not null && validUntil is not null && validUntil < validFrom)
            return (null, new("SUP-ERR-020", "A vigência do contrato termina antes de começar."));

        if (valueLimit is < 0) return (null, new("CT-ERR-011", "O teto financeiro do contrato não pode ser negativo."));
        supplier.ContractNumber = Clean(number);
        supplier.ContractValueLimit = valueLimit;
        supplier.ContractValidFrom = validFrom;
        supplier.ContractValidUntil = validUntil;
        supplier.ContractNotes = Clean(notes);

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
        }

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
