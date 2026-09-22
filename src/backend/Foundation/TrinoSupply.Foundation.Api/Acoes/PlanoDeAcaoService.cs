using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Acoes;

public record FiltroDeAcoes(
    string? Busca = null, string? Status = null, Guid? ResponsavelId = null,
    string? CentroCusto = null, bool? Atrasadas = null,
    DateOnly? De = null, DateOnly? Ate = null, Guid? CicloId = null);

public record DadosDaAcao(
    string Title, Guid ResponsibleId, DateOnly? StartDate, DateOnly? DueDate,
    string? Reason, string? Area, string? ExpectedResult, string? Kpi,
    decimal? ExpectedGain, string? CostCenter,
    Guid? CycleId = null, string? RootCauseRef = null);

public record PaginaDeAcoes(
    IReadOnlyList<ActionItem> Itens, PlacarDasAcoes Placar,
    IReadOnlyList<OpcaoDeResponsavel> Responsaveis, IReadOnlyList<string> CentrosCusto);

public record OpcaoDeResponsavel(Guid Id, string Label);

/// <summary>
/// O plano de ação: uma tarefa com dono, prazo e 5W2H.
///
/// <para>
/// Ele é um módulo por si, e não parte de outro. A ação que nasce de um ciclo de melhoria é
/// a mesma coisa que a ação que nasce de uma reunião: duplicar o modelo criaria dois lugares
/// para olhar o mesmo trabalho, e quem procura "o que está pendente comigo" teria de olhar
/// os dois e somar de cabeça.
/// </para>
/// </summary>
public class PlanoDeAcaoService(AppDbContext db, TimeProvider clock)
{
    public const int TamanhoMaximo = 300;

    /// <summary>Quem mantém ações. O módulo é a régua: o administrador concede no cadastro.</summary>
    public static bool CanManage(string role) =>
        role is not (Roles.Auditor or Roles.WarehouseOperator);

    public async Task<PaginaDeAcoes> ListarAsync(FiltroDeAcoes f, CancellationToken ct = default)
    {
        var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        // filtro na entidade; "atrasada" depende do status E da data, então sai depois —
        // mas sobre o recorte já cortado no banco, e não sobre a base inteira
        var consulta = db.ActionItems.AsQueryable();
        if (!string.IsNullOrWhiteSpace(f.Status)) consulta = consulta.Where(a => a.Status == f.Status);
        if (f.ResponsavelId is { } dono) consulta = consulta.Where(a => a.ResponsibleId == dono);
        if (!string.IsNullOrWhiteSpace(f.CentroCusto))
        {
            var cc = f.CentroCusto.Trim();
            consulta = consulta.Where(a => a.CostCenter == cc);
        }
        if (f.CicloId is { } ciclo) consulta = consulta.Where(a => a.CycleId == ciclo);
        if (f.De is { } de) consulta = consulta.Where(a => a.DueDate != null && a.DueDate >= de);
        if (f.Ate is { } ate) consulta = consulta.Where(a => a.DueDate != null && a.DueDate <= ate);
        if (!string.IsNullOrWhiteSpace(f.Busca))
        {
            var termo = f.Busca.Trim().ToLowerInvariant();
            consulta = consulta.Where(a =>
                a.Number.ToLower().Contains(termo)
                || a.Title.ToLower().Contains(termo)
                || a.ResponsibleLabel.ToLower().Contains(termo));
        }

        var todas = await consulta.OrderBy(a => a.DueDate == null)
            .ThenBy(a => a.DueDate).ThenByDescending(a => a.CreatedAt)
            .Take(TamanhoMaximo).ToListAsync(ct);

        var itens = f.Atrasadas == true
            ? todas.Where(a => PlanoDeAcao.Atrasada(a, hoje)).ToList()
            : todas;

        return new PaginaDeAcoes(itens, Placar(todas, hoje),
            await ResponsaveisAsync(ct), await CentrosAsync(ct));
    }

    /// <summary>
    /// Os números do topo, sobre o mesmo recorte que a lista mostra. Card que conta uma base
    /// e abre outra é card que mente — e o primeiro que notar para de confiar nos outros.
    /// </summary>
    public static PlacarDasAcoes Placar(IReadOnlyList<ActionItem> acoes, DateOnly hoje) => new(
        acoes.Count,
        acoes.Count(a => a.Status == StatusDaAcao.Pendente),
        acoes.Count(a => a.Status == StatusDaAcao.EmAndamento),
        acoes.Count(a => a.Status == StatusDaAcao.Concluida),
        acoes.Count(a => a.Status == StatusDaAcao.Suspensa),
        acoes.Count(a => a.Status == StatusDaAcao.Cancelada),
        acoes.Count(a => PlanoDeAcao.Atrasada(a, hoje)),
        acoes.Sum(a => a.ExpectedGain ?? 0),
        acoes.Sum(a => a.RealizedGain ?? 0));

    private async Task<List<OpcaoDeResponsavel>> ResponsaveisAsync(CancellationToken ct)
    {
        // quem de fato tem ação: oferecer o cadastro inteiro encheria a caixa de gente
        // que nunca recebeu nada, e quem escolhesse receberia lista vazia
        var donos = await db.ActionItems
            .Select(a => new { a.ResponsibleId, a.ResponsibleLabel })
            .Distinct().OrderBy(x => x.ResponsibleLabel).Take(300).ToListAsync(ct);
        return [.. donos.Select(x => new OpcaoDeResponsavel(x.ResponsibleId, x.ResponsibleLabel))];
    }

    private Task<List<string>> CentrosAsync(CancellationToken ct) =>
        db.ActionItems.Where(a => a.CostCenter != null && a.CostCenter != "")
            .Select(a => a.CostCenter!).Distinct().OrderBy(c => c).Take(300).ToListAsync(ct);

    public async Task<(ActionItem? acao, UserError? erro)> CriarAsync(
        Actor ator, DadosDaAcao d, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(d.Title) || d.Title.Trim().Length < 5)
            return (null, new("AC-ERR-010", "Diga o que precisa ser feito, em pelo menos 5 caracteres."));

        var dono = await db.Users.SingleOrDefaultAsync(u => u.Id == d.ResponsibleId && u.Active, ct);
        if (dono is null)
            return (null, new("AC-ERR-011", "Responsável inválido: escolha um usuário ativo."));
        // prazo antes do começo não é prazo apertado, é dado trocado
        if (d.StartDate is { } inicio && d.DueDate is { } prazo && prazo < inicio)
            return (null, new("AC-ERR-012", "O prazo não pode ser anterior ao início."));

        var agora = clock.GetUtcNow();
        var acao = new ActionItem
        {
            Number = await ProximoNumeroAsync(agora, ct),
            Title = d.Title.Trim(),
            ResponsibleId = dono.Id, ResponsibleLabel = dono.Name,
            StartDate = d.StartDate, DueDate = d.DueDate,
            Reason = Limpo(d.Reason), Area = Limpo(d.Area),
            ExpectedResult = Limpo(d.ExpectedResult), Kpi = Limpo(d.Kpi),
            ExpectedGain = d.ExpectedGain, CostCenter = Limpo(d.CostCenter)?.ToUpperInvariant(),
            CycleId = d.CycleId, RootCauseRef = Limpo(d.RootCauseRef),
            CreatedBy = ator.Id, CreatedByLabel = ator.Label,
            CreatedAt = agora, UpdatedAt = agora,
        };
        db.ActionItems.Add(acao);
        await db.SaveChangesAsync(ct);
        return (acao, null);
    }

    /// <summary>
    /// Muda a situação. Suspender e cancelar <b>exigem motivo</b>: parar o trabalho de alguém
    /// sem dizer por quê é o que faz a ação ficar parada meses sem ninguém saber de quem foi
    /// a decisão.
    /// </summary>
    public async Task<(ActionItem? acao, UserError? erro)> MudarStatusAsync(
        Guid id, string status, string? motivo, int? progresso, CancellationToken ct = default)
    {
        var acao = await db.ActionItems.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (acao is null) return (null, new("AC-ERR-404", "Ação não encontrada."));

        status = (status ?? "").Trim().ToUpperInvariant();
        if (!StatusDaAcao.Todos.Contains(status))
            return (null, new("AC-ERR-013", "Situação inválida."));

        if (status is StatusDaAcao.Suspensa or StatusDaAcao.Cancelada
            && string.IsNullOrWhiteSpace(motivo))
            return (null, new("AC-ERR-014",
                status == StatusDaAcao.Suspensa
                    ? "Diga por que a ação está sendo suspensa."
                    : "Diga por que a ação está sendo cancelada."));

        var agora = clock.GetUtcNow();
        acao.Status = status;
        acao.StatusReason = Limpo(motivo);
        if (progresso is { } p) acao.Progress = Math.Clamp(p, 0, 100);
        if (status == StatusDaAcao.Concluida) { acao.Progress = 100; acao.CompletedAt = agora; }
        else acao.CompletedAt = null;
        acao.UpdatedAt = agora;
        acao.Version += 1;
        await db.SaveChangesAsync(ct);
        return (acao, null);
    }

    public async Task<(ActionItem? acao, UserError? erro)> AtualizarAsync(
        Guid id, DadosDaAcao d, decimal? ganhoRealizado, CancellationToken ct = default)
    {
        var acao = await db.ActionItems.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (acao is null) return (null, new("AC-ERR-404", "Ação não encontrada."));
        if (!string.IsNullOrWhiteSpace(d.Title) && d.Title.Trim().Length >= 5) acao.Title = d.Title.Trim();
        if (d.ResponsibleId != Guid.Empty && d.ResponsibleId != acao.ResponsibleId)
        {
            var dono = await db.Users.SingleOrDefaultAsync(u => u.Id == d.ResponsibleId && u.Active, ct);
            if (dono is null) return (null, new("AC-ERR-011", "Responsável inválido: escolha um usuário ativo."));
            acao.ResponsibleId = dono.Id; acao.ResponsibleLabel = dono.Name;
        }
        var inicio = d.StartDate ?? acao.StartDate;
        var prazo = d.DueDate ?? acao.DueDate;
        if (inicio is { } i && prazo is { } p && p < i)
            return (null, new("AC-ERR-012", "O prazo não pode ser anterior ao início."));
        acao.StartDate = inicio; acao.DueDate = prazo;
        acao.Reason = Limpo(d.Reason) ?? acao.Reason;
        acao.Area = Limpo(d.Area) ?? acao.Area;
        acao.ExpectedResult = Limpo(d.ExpectedResult) ?? acao.ExpectedResult;
        acao.Kpi = Limpo(d.Kpi) ?? acao.Kpi;
        if (d.ExpectedGain is not null) acao.ExpectedGain = d.ExpectedGain;
        if (ganhoRealizado is not null) acao.RealizedGain = ganhoRealizado;
        if (Limpo(d.CostCenter) is { } cc) acao.CostCenter = cc.ToUpperInvariant();
        if (d.CycleId is not null) acao.CycleId = d.CycleId;
        if (Limpo(d.RootCauseRef) is { } causa) acao.RootCauseRef = causa;
        acao.UpdatedAt = clock.GetUtcNow();
        acao.Version += 1;
        await db.SaveChangesAsync(ct);
        return (acao, null);
    }

    private async Task<string> ProximoNumeroAsync(DateTimeOffset agora, CancellationToken ct)
    {
        var prefixo = $"AC-{agora.Year}-";
        var usados = await db.ActionItems.Where(a => a.Number.StartsWith(prefixo))
            .Select(a => a.Number).ToListAsync(ct);
        var maior = usados
            .Select(n => int.TryParse(n[prefixo.Length..], out var v) ? v : 0)
            .DefaultIfEmpty(0).Max();
        return $"{prefixo}{maior + 1:000000}";
    }

    private static string? Limpo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
