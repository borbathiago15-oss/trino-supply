using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;

namespace TrinoSupply.Foundation.Api.Procurement;

public partial class QuotationService
{
    /// <summary>
    /// O que a tela do processo precisa além do próprio processo: quem pediu (SCs de origem)
    /// e quem aprova no centro de custo. Duas consultas pequenas, só no detalhe — a lista
    /// não paga por isso.
    /// </summary>
    public async Task<IReadOnlyList<EtapaDoCaminho>> CaminhoAsync(Quotation q, CancellationToken ct = default)
    {
        var ids = q.SourcePrIds;
        var scs = await db.Requisitions.Where(r => ids.Contains(r.Id))
            .Select(r => new { r.RequesterLabel, r.SubmittedAt, r.CreatedAt })
            .ToListAsync(ct);
        var solicitantes = scs.Select(x => x.RequesterLabel).ToList();
        var pedidaEm = scs.Count == 0 ? (DateTimeOffset?)null : scs.Min(x => x.SubmittedAt ?? x.CreatedAt);

        // a mesma consulta da Torre, para um centro só
        var centro = q.CostCenter.Trim().ToUpperInvariant();
        var alcada = centro.Length == 0 ? null : await db.CostCenters
            .Where(c => c.Code.ToUpper() == centro)
            .Select(c => new
            {
                N1 = c.Approvers.Where(a => a.Level == ApprovalLevels.Level1).Select(a => a.UserName).ToList(),
                N2 = c.Approvers.Where(a => a.Level == ApprovalLevels.Level2).Select(a => a.UserName).ToList(),
            })
            .FirstOrDefaultAsync(ct);

        return CaminhoDoProcesso.De(q, solicitantes, pedidaEm,
            alcada is null ? AlcadasDoCentro.Nenhuma : new AlcadasDoCentro(alcada.N1, alcada.N2));
    }
}
