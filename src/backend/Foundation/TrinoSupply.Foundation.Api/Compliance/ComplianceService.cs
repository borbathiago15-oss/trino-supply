using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Api.Compliance;

/// <summary>Uma penalidade do processo, com a evidência que a sustenta (V2 §14).</summary>
public record CompliancePenalty(string Code, string Label, int Points, string Evidence);

public record ComplianceRow(
    Guid QuotationId, string Number, string Kind, QuotationStatus Status, string CostCenter,
    string BuyerLabel, DateTimeOffset OpenedAt, bool Concluded, int Score,
    IReadOnlyList<CompliancePenalty> Penalties);

public record ComplianceGroup(string Label, int Count, double AverageScore);

public record ComplianceReport(
    int Evaluated, int Concluded, double? AverageScore, int FullCompliance,
    IReadOnlyList<ComplianceGroup> ByBuyer, IReadOnlyList<ComplianceGroup> ByCostCenter,
    IReadOnlyList<ComplianceRow> Items);

/// <summary>
/// Compliance Score (V2-P2 §14): todo processo recebe 100 − penalizações. O score é DERIVADO
/// dos fatos imutáveis já gravados no processo (propostas, prioridade, datas, homologação) —
/// nada é persistido em paralelo e nenhum processo é bloqueado por ele: mede e expõe.
/// Componentes desta fase: sem cotação competitiva −25 · emergencial −20 ·
/// fornecedor não homologado −30 · SC criada após a data da necessidade −20.
/// (Categoria tabelada −15 e limite de alçada −10 entram na P3, com seus cadastros.)
/// </summary>
public class ComplianceService(AppDbContext db, TimeProvider clock)
{
    public static bool CanView(string role) =>
        role is Roles.Auditor or Roles.SupplyManager or Roles.Director or Roles.SystemAdministrator;

    public async Task<ComplianceReport> ReportAsync(Guid? quotationId = null, CancellationToken ct = default)
    {
        var query = db.Quotations.Include(q => q.Items).Include(q => q.Proposals)
            .Where(q => q.Status != QuotationStatus.Cancelled && q.Status != QuotationStatus.Rejected);
        if (quotationId is { } qid) query = query.Where(q => q.Id == qid);
        var quotes = await query.OrderByDescending(q => q.CreatedAt).Take(300).ToListAsync(ct);

        var prIds = quotes.SelectMany(q => q.SourcePrIds).Distinct().ToList();
        var prs = await db.Requisitions.Where(r => prIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Number, r.Priority, r.NeededBy, r.CreatedAt })
            .ToDictionaryAsync(r => r.Id, ct);

        var winnerIds = quotes.Where(q => q.WinnerSupplierId != null)
            .Select(q => q.WinnerSupplierId!.Value).Distinct().ToList();
        var winners = await db.Suppliers.Include(s => s.Documents)
            .Where(s => winnerIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var rows = new List<ComplianceRow>();
        foreach (var q in quotes)
        {
            var penalties = new List<CompliancePenalty>();
            var sources = q.SourcePrIds.Where(prs.ContainsKey).Select(id => prs[id]).ToList();

            // CP-01 — sem cotação competitiva: só conta depois da escolha do vencedor,
            // quando o número de concorrentes ficou definitivo (evidência: fornecedores que propuseram)
            if (q.WinnerProposalId is not null)
            {
                var concorrentes = q.Proposals.Select(p => p.SupplierId).Distinct().Count();
                if (concorrentes <= 1)
                    penalties.Add(new("CP-01", "Sem cotação competitiva", 25,
                        $"Apenas {concorrentes} fornecedor apresentou proposta."));
            }

            // CP-02 — compra emergencial (a justificativa obrigatória da urgência é a evidência)
            var urgentes = sources.Where(s => s.Priority == "URGENT").ToList();
            if (urgentes.Count > 0)
                penalties.Add(new("CP-02", "Compra emergencial", 20,
                    $"SC urgente: {string.Join(", ", urgentes.Select(s => s.Number))}."));

            // CP-03 — fornecedor não homologado (estado atual do cadastro; a seleção nova já bloqueia,
            // então isto expõe processos antigos e regressões do cadastro depois da escolha)
            if (q.WinnerSupplierId is { } wid && winners.TryGetValue(wid, out var vencedor))
            {
                var situacao = vencedor.EffectiveHomologation(today);
                if (situacao != SupplierHomologation.Homologado)
                    penalties.Add(new("CP-03", "Fornecedor não homologado", 30,
                        $"{vencedor.TradeName ?? vencedor.LegalName} está {situacao}."));
            }

            // CP-04 — demanda atendida depois da data da necessidade: a submissão já recusa
            // neededBy no passado (PR-ERR-050), então aqui o que se mede é o processo de
            // cotação abrindo DEPOIS da data em que o material já era necessário
            var abertura = DateOnly.FromDateTime(q.CreatedAt.UtcDateTime);
            var atrasadas = sources.Where(s => s.NeededBy is { } n && abertura > n).ToList();
            if (atrasadas.Count > 0)
                penalties.Add(new("CP-04", "Atendida após a necessidade", 20,
                    string.Join("; ", atrasadas.Select(s =>
                        $"{s.Number} precisava do material até {s.NeededBy:dd/MM/yyyy}; o processo só abriu em {abertura:dd/MM/yyyy}"))));

            var score = Math.Max(0, 100 - penalties.Sum(p => p.Points));
            rows.Add(new ComplianceRow(q.Id, q.Number, q.Kind.ToString().ToUpperInvariant(), q.Status,
                q.CostCenter, q.CreatedByLabel, q.CreatedAt,
                q.Status == QuotationStatus.PoIssued, score, penalties));
        }

        static List<ComplianceGroup> Agrupa(IEnumerable<ComplianceRow> rs, Func<ComplianceRow, string> key) =>
            rs.GroupBy(key)
              .Select(g => new ComplianceGroup(g.Key, g.Count(), Math.Round(g.Average(r => r.Score), 1)))
              .OrderBy(g => g.AverageScore).ToList();

        return new ComplianceReport(
            rows.Count, rows.Count(r => r.Concluded),
            rows.Count > 0 ? Math.Round(rows.Average(r => r.Score), 1) : null,
            rows.Count(r => r.Score == 100),
            Agrupa(rows, r => r.BuyerLabel), Agrupa(rows, r => r.CostCenter), rows);
    }
}
