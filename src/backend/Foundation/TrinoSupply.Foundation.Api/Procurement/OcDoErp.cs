using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>Quanto de um item do pedido a O.C. que está sendo registrada cobre.</summary>
public record ErpCoverageLine(Guid ItemId, decimal Quantity);

/// <summary>
/// O registro da O.C. do ERP num pedido — a regra num lugar só, chamada pela tela do pedido
/// e, por compatibilidade, pela rota antiga do processo de cotação.
///
/// <para>
/// A regra é a O.C. do SENIOR: sem ela o pedido não fecha (PO-BR-011). A única exceção é a
/// observação dizendo por que a O.C. não foi gerada. Uma O.C. pode cobrir <b>parte</b> do
/// pedido — o ERP fecha 600 das 1.000 luvas e o resto vem noutra O.C. —, por isso cada
/// registro diz quais itens e quantidades cobre; sem dizer, cobre tudo o que ainda falta.
/// A soma das O.C.s nunca passa do pedido (PO-ERR-059).
/// </para>
/// </summary>
public static class OcDoErp
{
    public const int MotivoMinimo = 10;

    /// <summary>Aplica o registro ao pedido carregado (itens e O.C.s incluídos). Não grava.</summary>
    public static async Task<(PurchaseOrderErpDocument? documento, UserError? error)> AplicarAsync(
        AppDbContext db, TimeProvider clock, PurchaseOrder order, Actor actor,
        string? erpNumber, DateOnly? issuedOn, string? noErpReason,
        IReadOnlyList<ErpCoverageLine>? lines, string? overLimitJustification, string codigoDeErro,
        CancellationToken ct)
    {
        var agora = clock.GetUtcNow();
        var hoje = DateOnly.FromDateTime(agora.UtcDateTime);
        var numero = erpNumber?.Trim();

        if (string.IsNullOrWhiteSpace(numero))
        {
            var motivo = noErpReason?.Trim();
            if (string.IsNullOrWhiteSpace(motivo) || motivo.Length < MotivoMinimo)
                return (null, new(codigoDeErro,
                    "A O.C. é gerada no ERP e sem ela o pedido não fecha. Para fechar assim mesmo, "
                    + "informe na observação, em pelo menos 10 caracteres, por que a O.C. não foi gerada."));
            if (motivo.Length > 500)
                return (null, new(codigoDeErro, "A observação tem no máximo 500 caracteres."));
            // a exceção vale para o que ficou sem O.C.: as O.C.s já registradas continuam
            order.NoErpReason = motivo;
            order.ErpIssuedOn ??= issuedOn ?? hoje;
            return (null, null);
        }

        if (numero.Length > 30)
            return (null, new(codigoDeErro.Replace("054", "050").Replace("043", "041"), "O número da OC tem no máximo 30 caracteres."));
        // a mesma O.C. de novo no mesmo pedido é correção de data, não repetição
        if (order.ErpDocuments.FirstOrDefault(d => d.Number == numero) is { } existente)
        {
            if (issuedOn is { } corrigida)
            {
                existente.IssuedOn = corrigida;
                if (order.ErpNumber == numero) order.ErpIssuedOn = corrigida;
            }
            return (existente, null);
        }
        // a O.C. do SENIOR é única no sistema: nem outro pedido, nem outra O.C. deste
        var repetida = await db.PurchaseOrders.AnyAsync(o => o.Id != order.Id && (o.Number == numero || o.ErpNumber == numero), ct)
                       || await db.PurchaseOrderErpDocuments.AnyAsync(d => d.Number == numero, ct);
        if (repetida)
            return (null, new(codigoDeErro.Replace("054", "050").Replace("043", "041"), $"A OC {numero} já está registrada em outro pedido."));

        // cobertura: o que a O.C. traz de cada item; sem linhas, tudo o que ainda falta
        var restante = order.Items.ToDictionary(i => i.Id, i => i.Quantity - order.ErpCovered(i.Id));
        var cobertura = new List<PurchaseOrderErpDocumentItem>();
        if (lines is null || lines.Count == 0)
        {
            foreach (var (itemId, falta) in restante.Where(kv => kv.Value > 0))
                cobertura.Add(new PurchaseOrderErpDocumentItem { OrderItemId = itemId, Quantity = falta });
        }
        else
        {
            foreach (var l in lines)
            {
                if (!restante.TryGetValue(l.ItemId, out var falta))
                    return (null, new("PO-ERR-059", "Item informado não pertence a este pedido."));
                if (l.Quantity < 0)
                    return (null, new("PO-ERR-059", "A quantidade coberta pela O.C. não pode ser negativa."));
                if (l.Quantity == 0) continue;
                var item = order.Items.Single(i => i.Id == l.ItemId);
                if (l.Quantity > falta)
                    return (null, new("PO-ERR-059",
                        $"{item.Description}: a O.C. cobre {l.Quantity:0.##}, mas só faltam {falta:0.##} sem O.C. neste pedido."));
                cobertura.Add(new PurchaseOrderErpDocumentItem { OrderItemId = l.ItemId, Quantity = l.Quantity });
            }
        }
        if (cobertura.Count == 0)
            return (null, new("PO-ERR-059", "Todo o pedido já está coberto por O.C. do ERP: não há saldo para esta O.C."));

        // teto do contrato de parceria (V2-P2): exceder exige justificativa — mede, não trava cego
        var supplier = await db.Suppliers.Include(s => s.ContractItems).SingleOrDefaultAsync(s => s.Id == order.SupplierId, ct);
        if (supplier is not null && supplier.ContractValueLimit is { } teto && supplier.ContractIsCurrent(hoje)
            && !order.ErpDocuments.Any())
        {
            var consumido = await ConsumoDoContratoAsync(db, supplier, order.Id, ct);
            if (consumido + order.TotalValue > teto)
            {
                if (string.IsNullOrWhiteSpace(overLimitJustification))
                    return (null, new("CT-ERR-010",
                        $"Esta O.C. ({order.TotalValue:0.00}) ultrapassa o saldo do contrato {supplier.ContractNumber} " +
                        $"(teto {teto:0.00}, consumido {consumido:0.00}). Informe a justificativa para prosseguir."));
                if (order.QuotationId is { } qid)
                    db.ProcessEvents.Add(new ProcessEvent
                    {
                        QuotationId = qid, EventType = "CONTRATO_TETO_EXCEDIDO",
                        Description = $"O.C. {numero} excede o teto do contrato {supplier.ContractNumber} " +
                                      $"({consumido + order.TotalValue:0.00} de {teto:0.00}).",
                        ActorId = actor.Id == Guid.Empty ? null : actor.Id, ActorLabel = actor.Label,
                        Note = overLimitJustification!.Trim(), OccurredAt = agora,
                    });
            }
        }

        var documento = new PurchaseOrderErpDocument
        {
            OrderId = order.Id, Number = numero, IssuedOn = issuedOn ?? hoje,
            CreatedBy = actor.Id, CreatedByLabel = actor.Label, CreatedAt = agora, Items = cobertura,
        };
        order.ErpDocuments.Add(documento);
        db.PurchaseOrderErpDocuments.Add(documento);

        // a primeira O.C. é a do cabeçalho: é o que todo mundo que já lia "tem O.C.?" continua lendo
        if (order.ErpNumber is null)
        {
            order.ErpNumber = numero;
            order.ErpIssuedOn = documento.IssuedOn;
            // congela a data prometida para o OTIF: data da O.C. + prazo de entrega da proposta
            order.PromisedDate ??= order.DeliveryDays is { } prazo ? documento.IssuedOn.AddDays(prazo) : null;
        }
        // chegou a O.C. de verdade: a observação da exceção sai de cena
        order.NoErpReason = null;
        return (documento, null);
    }

    /// <summary>O que já foi comprado do fornecedor na vigência do contrato, sem contar este pedido.</summary>
    public static async Task<decimal> ConsumoDoContratoAsync(AppDbContext db, Supplier supplier, Guid? excetoPedido, CancellationToken ct)
    {
        var inicio = supplier.ContractValidFrom?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;
        var fim = supplier.ContractValidUntil?.ToDateTime(TimeOnly.MaxValue) ?? DateTime.MaxValue;
        var i0 = new DateTimeOffset(inicio, TimeSpan.Zero);
        var f0 = new DateTimeOffset(fim, TimeSpan.Zero);
        return await db.PurchaseOrders
            .Where(o => o.SupplierId == supplier.Id && o.Status != PurchaseOrderStatus.Cancelled
                        && o.Id != excetoPedido && o.CreatedAt >= i0 && o.CreatedAt <= f0)
            .SumAsync(o => (decimal?)o.TotalValue, ct) ?? 0m;
    }

    /// <summary>
    /// O processo de cotação fecha ("O.C. emitida") quando todos os pedidos dele têm O.C. do
    /// ERP ou a observação da exceção. É o mesmo ponto em que o fluxo antigo fechava — só que
    /// agora o registro acontece na tela do pedido.
    /// </summary>
    public static async Task SincronizarProcessoAsync(AppDbContext db, TimeProvider clock, PurchaseOrder order, Actor actor, CancellationToken ct)
    {
        if (order.QuotationId is not { } qid) return;
        var q = await db.Quotations.SingleOrDefaultAsync(x => x.Id == qid, ct);
        if (q is null || q.Status != QuotationStatus.ApprovedForIssue) return;
        var pedidos = await db.PurchaseOrders.Where(o => o.QuotationId == qid && o.Status != PurchaseOrderStatus.Cancelled).ToListAsync(ct);
        // o pedido em mãos ainda não foi gravado: o que está na memória é o que vale
        var pendentes = pedidos.Where(o => o.Id != order.Id && o.ErpNumber is null && o.NoErpReason is null)
            .Select(o => o.SupplierName).Distinct().ToList();
        var esteFechou = order.ErpNumber is not null || order.NoErpReason is not null;
        var referencia = order.ErpNumber ?? order.Number;
        if (esteFechou && pendentes.Count == 0)
        {
            var from = q.Status;
            q.Status = QuotationStatus.PoIssued;
            q.UpdatedAt = clock.GetUtcNow();
            q.Version += 1;
            db.ProcessEvents.Add(new ProcessEvent
            {
                QuotationId = qid, EventType = "OC_REGISTRADA",
                Description = order.ErpNumber is not null
                    ? $"OC {referencia} do SENIOR registrada para {order.SupplierName} — total {order.TotalValue:0.00}."
                    : $"Pedido {referencia} fechado sem O.C. do ERP, com a observação na auditoria — total {order.TotalValue:0.00}.",
                FromStatus = from, ToStatus = q.Status,
                ActorId = actor.Id == Guid.Empty ? null : actor.Id, ActorLabel = actor.Label, OccurredAt = clock.GetUtcNow(),
            });
        }
        else if (esteFechou)
        {
            db.ProcessEvents.Add(new ProcessEvent
            {
                QuotationId = qid, EventType = "OC_PARCIAL_REGISTRADA",
                Description = $"OC {referencia} registrada para {order.SupplierName} — total {order.TotalValue:0.00}. " +
                              $"Falta registrar a O.C. de: {string.Join(", ", pendentes)}.",
                ActorId = actor.Id == Guid.Empty ? null : actor.Id, ActorLabel = actor.Label, OccurredAt = clock.GetUtcNow(),
            });
        }
    }
}
