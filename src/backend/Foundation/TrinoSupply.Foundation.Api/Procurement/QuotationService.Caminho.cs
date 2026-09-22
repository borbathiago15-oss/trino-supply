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

        // a mesma regra da Torre e da decisão: alçadas por nível, senão o vínculo antigo
        var centro = q.CostCenter.Trim().ToUpperInvariant();
        var alcadas = (await AlcadasDoCentro.ResolverAsync(db, [centro], ct)).GetValueOrDefault(centro, AlcadasDoCentro.Nenhuma);

        // dado o Nível 1, o Nível 2 do vínculo antigo é o diretor de QUEM aprovou — é o que
        // DirectorDecisionAsync cobra (LinkedDirectorAsync), e o gerente do centro pode não ser ele
        if (alcadas.Nivel2.Count == 0 && q.ManagerApprovedBy is { } gerente)
        {
            var diretor = await db.Users.Where(u => u.Id == gerente && u.DirectorId != null)
                .Join(db.Users, u => u.DirectorId, d => d.Id, (u, d) => new { d.Id, d.Name })
                .FirstOrDefaultAsync(ct);
            if (diretor is not null) alcadas = alcadas with { Nivel2 = [diretor.Name], Ids2 = [diretor.Id] };
        }

        var rota = await AlcadaDoComprador.RotaAsync(db, q, q.ManagerApprovedBy, ct);
        return CaminhoDoProcesso.De(q, solicitantes, pedidaEm, alcadas, rota);
    }
}
