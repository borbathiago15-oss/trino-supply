using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Acoes;

public record FiltroDePlanos(
    string? Busca = null, string? Situacao = null, string? Prioridade = null,
    string? CentroCusto = null, string? Area = null, Guid? ResponsavelId = null,
    bool? Encerrados = null, Guid? CicloId = null);

public record DadosDoPlano(
    string? Title, string? Description, string? CostCenter,
    IReadOnlyList<string>? Areas, string? OtherArea, string? Priority,
    DateOnly? StartDate, DateOnly? DueDate, int? Completion,
    string? Problem, string? BusinessReason, string? Category, string? Sponsor,
    string? ManagerName, string? OperationalImpact, string? FinancialImpact,
    string? KpiAffected, string? TargetGoal, string? Criticality, string? Complexity,
    decimal? RoiExpected, decimal? SavingExpected, decimal? SavingRealized,
    decimal? InvestmentPlanned, decimal? InvestmentActual,
    IReadOnlyList<Guid>? ResponsibleIds = null, Guid? CycleId = null,
    bool? Cancelled = null, string? CancelReason = null);

public record DadosDaAcao(
    string Title, Guid ResponsibleId, DateOnly? StartDate, DateOnly? DueDate,
    string? Reason, string? Area, string? ExpectedResult, string? Kpi,
    decimal? ExpectedGain, string? CostCenter,
    string? RootCauseRef = null, string? SupportArea = null,
    string? Complexity = null, string? RiskLevel = null, string? Dependencies = null,
    string? Evidence = null, string? Comments = null, int? Seq = null);

public record PaginaDePlanos(
    IReadOnlyList<ActionPlan> Itens, PlacarDosPlanos Placar,
    IReadOnlyList<OpcaoDeResponsavel> Responsaveis, IReadOnlyList<string> CentrosCusto);

public record OpcaoDeResponsavel(Guid Id, string Label);

/// <summary>
/// O plano de ação — o <b>projeto</b>, com as ações dentro dele.
///
/// <para>
/// A ação solta não contava a história: "trocar o filme do palete" não diz qual problema
/// resolve, quanto custa nem o que se aprendeu. O plano é onde isso vive, e as ações são o
/// <b>como</b>. É o desenho que a operação já conhece do Trino Intelligence.
/// </para>
///
/// <para>
/// Duas regras estruturam o resto: <b>toda ação tem plano</b>, e <b>plano encerrado não se
/// edita</b> — encerrar é uma decisão com dono e data, e deixá-lo aceitar edição em silêncio
/// faria o registro do encerramento mentir sobre o que estava fechado.
/// </para>
/// </summary>
public class PlanoDeAcaoService(AppDbContext db, TimeProvider clock)
{
    public const int TamanhoMaximo = 200;

    /// <summary>Quem mantém planos. O módulo é a régua: o administrador concede no cadastro.</summary>
    public static bool CanManage(string role) =>
        role is not (Roles.Auditor or Roles.WarehouseOperator);

    /// <summary>Reabrir um plano encerrado é do administrador — como no Trino Intelligence.</summary>
    public static bool CanReopen(string role) =>
        role is Roles.SystemAdministrator or Roles.SupplyManager;

    // ---- leitura -------------------------------------------------------------

    public async Task<PaginaDePlanos> ListarAsync(FiltroDePlanos f, CancellationToken ct = default)
    {
        var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        // filtro na entidade; a situação depende de data E progresso, então sai depois — mas
        // sobre o recorte já cortado no banco, e não sobre a base inteira
        var consulta = db.ActionPlans.AsQueryable();
        if (!string.IsNullOrWhiteSpace(f.Prioridade))
        {
            var p = f.Prioridade.Trim().ToUpperInvariant();
            consulta = consulta.Where(x => x.Priority == p);
        }
        if (!string.IsNullOrWhiteSpace(f.CentroCusto))
        {
            var cc = f.CentroCusto.Trim().ToUpperInvariant();
            consulta = consulta.Where(x => x.CostCenter == cc);
        }
        if (!string.IsNullOrWhiteSpace(f.Area))
        {
            var area = f.Area.Trim();
            consulta = consulta.Where(x => x.Areas != null && x.Areas.Contains(area));
        }
        if (f.CicloId is { } ciclo) consulta = consulta.Where(x => x.CycleId == ciclo);
        if (f.Encerrados is { } encerrados)
            consulta = consulta.Where(x =>
                encerrados ? x.Life == VidaDoPlano.Encerrado : x.Life == VidaDoPlano.Ativo);
        if (f.ResponsavelId is { } dono)
        {
            var ids = await db.ActionPlanResponsibles.Where(r => r.UserId == dono)
                .Select(r => r.PlanId).Distinct().ToArrayAsync(ct);
            consulta = consulta.Where(x => ids.Contains(x.Id));
        }
        if (!string.IsNullOrWhiteSpace(f.Busca))
        {
            var termo = f.Busca.Trim().ToLowerInvariant();
            consulta = consulta.Where(x =>
                x.Code.ToLower().Contains(termo) || x.Title.ToLower().Contains(termo));
        }

        var todos = await consulta
            .Include(x => x.Responsibles).Include(x => x.Items)
            .OrderBy(x => x.DueDate == null).ThenBy(x => x.DueDate)
            .ThenByDescending(x => x.CreatedAt)
            .Take(TamanhoMaximo).ToListAsync(ct);

        var itens = string.IsNullOrWhiteSpace(f.Situacao)
            ? todos
            : todos.Where(x => PlanoDoPlano.Situacao(x, hoje) == f.Situacao!.Trim().ToUpperInvariant()).ToList();

        return new PaginaDePlanos(itens, Placar(todos, hoje),
            await ResponsaveisAsync(ct), await CentrosAsync(ct));
    }

    /// <summary>
    /// Os números do topo, sobre o <b>mesmo</b> recorte que a lista mostra. Card que conta uma
    /// base e abre outra é card que mente — e o primeiro que notar para de confiar nos outros.
    /// </summary>
    public static PlacarDosPlanos Placar(IReadOnlyList<ActionPlan> planos, DateOnly hoje) => new(
        planos.Count,
        planos.Count(p => PlanoDoPlano.Situacao(p, hoje) == SituacaoDoPlano.Pendente),
        planos.Count(p => PlanoDoPlano.Situacao(p, hoje) == SituacaoDoPlano.EmAndamento),
        planos.Count(p => PlanoDoPlano.Situacao(p, hoje) == SituacaoDoPlano.Atrasado),
        planos.Count(p => PlanoDoPlano.Situacao(p, hoje) == SituacaoDoPlano.Concluido),
        planos.Count(p => PlanoDoPlano.Situacao(p, hoje) == SituacaoDoPlano.Cancelado),
        planos.Count(p => p.Life == VidaDoPlano.Encerrado),
        planos.Count(PlanoDoPlano.EncerradoComPendencia),
        planos.Sum(PlanoDoPlano.SavingEsperado),
        planos.Sum(PlanoDoPlano.SavingRealizado));

    public Task<ActionPlan?> AbrirAsync(Guid id, CancellationToken ct = default) =>
        db.ActionPlans
            .Include(p => p.Responsibles)
            .Include(p => p.Items)
            .Include(p => p.Risks)
            .Include(p => p.RootCauses)
            .Include(p => p.Lessons)
            .SingleOrDefaultAsync(p => p.Id == id, ct);

    private async Task<List<OpcaoDeResponsavel>> ResponsaveisAsync(CancellationToken ct)
    {
        var donos = await db.Users.Where(u => u.Active)
            .OrderBy(u => u.Name).Take(300)
            .Select(u => new { u.Id, u.Name }).ToListAsync(ct);
        return [.. donos.Select(x => new OpcaoDeResponsavel(x.Id, x.Name))];
    }

    private Task<List<string>> CentrosAsync(CancellationToken ct) =>
        db.ActionPlans.Where(p => p.CostCenter != null && p.CostCenter != "")
            .Select(p => p.CostCenter!).Distinct().OrderBy(c => c).Take(300).ToListAsync(ct);

    // ---- escrita do plano ----------------------------------------------------

    public async Task<(ActionPlan? plano, UserError? erro)> CriarPlanoAsync(
        Actor ator, DadosDoPlano d, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(d.Title) || d.Title.Trim().Length < 5)
            return (null, new("AP-ERR-010", "Diga o que o plano vai resolver, em pelo menos 5 caracteres."));
        if (d.StartDate is { } inicio && d.DueDate is { } prazo && prazo < inicio)
            return (null, new("AP-ERR-012", "O prazo não pode ser anterior ao início."));

        var agora = clock.GetUtcNow();
        var plano = new ActionPlan
        {
            Code = await ProximoCodigoAsync(agora, ct),
            Title = d.Title.Trim(),
            CreatedBy = ator.Id, CreatedByLabel = ator.Label,
            CreatedAt = agora, UpdatedAt = agora,
            StartDate = d.StartDate ?? DateOnly.FromDateTime(agora.UtcDateTime),
        };
        Aplicar(plano, d);
        if (await TrocarResponsaveisAsync(plano, d.ResponsibleIds, ct) is { } erro) return (null, erro);

        db.ActionPlans.Add(plano);
        await db.SaveChangesAsync(ct);
        return (plano, null);
    }

    /// <summary>
    /// Edita o plano. <b>Plano encerrado é recusado</b> (AP-ERR-020): encerrar é uma decisão
    /// com dono e data, e deixá-lo aceitar edição depois faria o registro do encerramento
    /// mentir sobre o que estava fechado. Para mexer, reabra — e a reabertura fica registrada.
    /// </summary>
    public async Task<(ActionPlan? plano, UserError? erro)> AtualizarPlanoAsync(
        Guid id, DadosDoPlano d, CancellationToken ct = default)
    {
        var plano = await db.ActionPlans.Include(p => p.Responsibles)
            .SingleOrDefaultAsync(p => p.Id == id, ct);
        if (plano is null) return (null, new("AP-ERR-404", "Plano não encontrado."));
        if (plano.Life == VidaDoPlano.Encerrado)
            return (null, new("AP-ERR-020",
                "Plano encerrado não se edita. Reabra-o primeiro — a reabertura fica registrada."));

        var inicio = d.StartDate ?? plano.StartDate;
        var prazo = d.DueDate ?? plano.DueDate;
        if (inicio is { } i && prazo is { } p && p < i)
            return (null, new("AP-ERR-012", "O prazo não pode ser anterior ao início."));

        if (!string.IsNullOrWhiteSpace(d.Title) && d.Title.Trim().Length >= 5) plano.Title = d.Title.Trim();
        Aplicar(plano, d);
        if (d.ResponsibleIds is not null
            && await TrocarResponsaveisAsync(plano, d.ResponsibleIds, ct) is { } erro) return (null, erro);

        plano.UpdatedAt = clock.GetUtcNow();
        plano.Version += 1;
        await db.SaveChangesAsync(ct);
        return (plano, null);
    }

    private static void Aplicar(ActionPlan p, DadosDoPlano d)
    {
        p.Description = Limpo(d.Description) ?? p.Description;
        if (Limpo(d.CostCenter) is { } cc) p.CostCenter = cc.ToUpperInvariant();
        if (d.Areas is not null)
        {
            var areas = d.Areas.Select(a => a?.Trim()).Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a!).Distinct().ToArray();
            p.Areas = areas.Length > 0 ? string.Join(',', areas) : null;
        }
        p.OtherArea = Limpo(d.OtherArea) ?? p.OtherArea;
        if (Grau(d.Priority, VocabularioDoPlano.Prioridades) is { } prio) p.Priority = prio;
        p.StartDate = d.StartDate ?? p.StartDate;
        p.DueDate = d.DueDate ?? p.DueDate;
        if (d.Completion is { } c) p.Completion = Math.Clamp(c, 0, 100);

        p.Problem = Limpo(d.Problem) ?? p.Problem;
        p.BusinessReason = Limpo(d.BusinessReason) ?? p.BusinessReason;
        p.Category = Limpo(d.Category) ?? p.Category;
        p.Sponsor = Limpo(d.Sponsor) ?? p.Sponsor;
        p.ManagerName = Limpo(d.ManagerName) ?? p.ManagerName;
        p.OperationalImpact = Limpo(d.OperationalImpact) ?? p.OperationalImpact;
        p.FinancialImpact = Limpo(d.FinancialImpact) ?? p.FinancialImpact;
        p.KpiAffected = Limpo(d.KpiAffected) ?? p.KpiAffected;
        p.TargetGoal = Limpo(d.TargetGoal) ?? p.TargetGoal;
        if (Grau(d.Criticality, VocabularioDoPlano.Graus) is { } crit) p.Criticality = crit;
        if (Grau(d.Complexity, VocabularioDoPlano.Graus) is { } comp) p.Complexity = comp;

        if (d.RoiExpected is not null) p.RoiExpected = d.RoiExpected.Value;
        if (d.SavingExpected is not null) p.SavingExpected = d.SavingExpected.Value;
        if (d.SavingRealized is not null) p.SavingRealized = d.SavingRealized.Value;
        if (d.InvestmentPlanned is not null) p.InvestmentPlanned = d.InvestmentPlanned.Value;
        if (d.InvestmentActual is not null) p.InvestmentActual = d.InvestmentActual.Value;

        if (d.CycleId is not null) p.CycleId = d.CycleId;
        // cancelar exige motivo, como suspender uma ação: parar o trabalho de alguém sem
        // dizer por quê é o que faz o plano ficar meses parado sem dono da decisão
        if (d.Cancelled is { } cancelado)
        {
            p.Cancelled = cancelado;
            p.CancelReason = cancelado ? Limpo(d.CancelReason) : null;
        }
    }

    private async Task<UserError?> TrocarResponsaveisAsync(
        ActionPlan plano, IReadOnlyList<Guid>? ids, CancellationToken ct)
    {
        if (ids is null) return null;
        var distintos = ids.Distinct().ToArray();
        var gente = await db.Users.Where(u => distintos.Contains(u.Id) && u.Active)
            .Select(u => new { u.Id, u.Name }).ToListAsync(ct);
        if (gente.Count != distintos.Length)
            return new("AP-ERR-011", "Responsável inválido: escolha usuários ativos.");

        // a inclusão passa pelo DbSet: a entidade nasce com Id, e o EF leria chave não vazia
        // em filho anexado pela navegação como "já existe"
        db.ActionPlanResponsibles.RemoveRange(plano.Responsibles);
        plano.Responsibles.Clear();
        foreach (var g in gente)
        {
            var r = new ActionPlanResponsible { PlanId = plano.Id, UserId = g.Id, UserLabel = g.Name };
            if (db.Entry(plano).State == EntityState.Detached
                || db.Entry(plano).State == EntityState.Added) plano.Responsibles.Add(r);
            else db.ActionPlanResponsibles.Add(r);
        }
        return null;
    }

    // ---- encerramento e reabertura -------------------------------------------

    /// <summary>
    /// Encerra o plano. <b>Progresso não encerra nada</b>: um plano a 100% continua ativo até
    /// alguém dizer que acabou, e um plano a 40% pode fechar se a decisão for essa — com quem
    /// decidiu, quando e a evidência do que ficou.
    /// </summary>
    public async Task<(ActionPlan? plano, UserError? erro)> EncerrarAsync(
        Actor ator, Guid id, string? evidencia, CancellationToken ct = default)
    {
        var plano = await db.ActionPlans.Include(p => p.Items)
            .SingleOrDefaultAsync(p => p.Id == id, ct);
        if (plano is null) return (null, new("AP-ERR-404", "Plano não encontrado."));
        if (plano.Life == VidaDoPlano.Encerrado)
            return (null, new("AP-ERR-021", "Este plano já está encerrado."));

        var agora = clock.GetUtcNow();
        plano.Life = VidaDoPlano.Encerrado;
        plano.ClosedById = ator.Id;
        plano.ClosedByLabel = ator.Label;
        plano.ClosedAt = agora;
        plano.EvidenceNote = Limpo(evidencia) ?? plano.EvidenceNote;
        plano.UpdatedAt = agora;
        plano.Version += 1;
        await db.SaveChangesAsync(ct);
        return (plano, null);
    }

    /// <summary>Reabre. Quem encerrou e quando somem: eles não valem mais para o plano em curso.</summary>
    public async Task<(ActionPlan? plano, UserError? erro)> ReabrirAsync(
        Guid id, CancellationToken ct = default)
    {
        var plano = await db.ActionPlans.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (plano is null) return (null, new("AP-ERR-404", "Plano não encontrado."));
        if (plano.Life != VidaDoPlano.Encerrado)
            return (null, new("AP-ERR-022", "Este plano não está encerrado."));

        plano.Life = VidaDoPlano.Ativo;
        plano.ClosedById = null;
        plano.ClosedByLabel = null;
        plano.ClosedAt = null;
        plano.UpdatedAt = clock.GetUtcNow();
        plano.Version += 1;
        await db.SaveChangesAsync(ct);
        return (plano, null);
    }

    // ---- as ações do plano ---------------------------------------------------

    public async Task<(ActionItem? acao, UserError? erro)> CriarAcaoAsync(
        Actor ator, Guid planoId, DadosDaAcao d, CancellationToken ct = default)
    {
        var plano = await db.ActionPlans.Include(p => p.Items)
            .SingleOrDefaultAsync(p => p.Id == planoId, ct);
        if (plano is null) return (null, new("AP-ERR-404", "Plano não encontrado."));
        if (plano.Life == VidaDoPlano.Encerrado)
            return (null, new("AP-ERR-020", "Plano encerrado não recebe ação nova. Reabra-o primeiro."));

        if (string.IsNullOrWhiteSpace(d.Title) || d.Title.Trim().Length < 5)
            return (null, new("AC-ERR-010", "Diga o que precisa ser feito, em pelo menos 5 caracteres."));
        var dono = await db.Users.SingleOrDefaultAsync(u => u.Id == d.ResponsibleId && u.Active, ct);
        if (dono is null)
            return (null, new("AC-ERR-011", "Responsável inválido: escolha um usuário ativo."));
        if (d.StartDate is { } inicio && d.DueDate is { } prazo && prazo < inicio)
            return (null, new("AC-ERR-012", "O prazo não pode ser anterior ao início."));

        var agora = clock.GetUtcNow();
        var acao = new ActionItem
        {
            Number = await ProximoNumeroAsync(agora, ct),
            PlanId = plano.Id,
            Seq = d.Seq ?? (plano.Items.Count == 0 ? 1 : plano.Items.Max(i => i.Seq) + 1),
            Title = d.Title.Trim(),
            ResponsibleId = dono.Id, ResponsibleLabel = dono.Name,
            StartDate = d.StartDate, DueDate = d.DueDate,
            Reason = Limpo(d.Reason), Area = Limpo(d.Area),
            ExpectedResult = Limpo(d.ExpectedResult), Kpi = Limpo(d.Kpi),
            ExpectedGain = d.ExpectedGain,
            CostCenter = Limpo(d.CostCenter)?.ToUpperInvariant() ?? plano.CostCenter,
            RootCauseRef = Limpo(d.RootCauseRef), SupportArea = Limpo(d.SupportArea),
            Dependencies = Limpo(d.Dependencies), Evidence = Limpo(d.Evidence),
            Comments = Limpo(d.Comments),
            CreatedBy = ator.Id, CreatedByLabel = ator.Label,
            CreatedAt = agora, UpdatedAt = agora,
        };
        if (Grau(d.Complexity, VocabularioDoPlano.Graus) is { } comp) acao.Complexity = comp;
        if (Grau(d.RiskLevel, SeveridadeDoRisco.Graus) is { } risco) acao.RiskLevel = risco;

        db.ActionItems.Add(acao);
        await db.SaveChangesAsync(ct);
        return (acao, null);
    }

    public async Task<(ActionItem? acao, UserError? erro)> AtualizarAcaoAsync(
        Guid id, DadosDaAcao d, decimal? ganhoRealizado, CancellationToken ct = default)
    {
        var acao = await db.ActionItems.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (acao is null) return (null, new("AC-ERR-404", "Ação não encontrada."));
        if (await PlanoEncerradoAsync(acao.PlanId, ct))
            return (null, new("AP-ERR-020", "Plano encerrado não se edita. Reabra-o primeiro."));

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
        acao.RootCauseRef = Limpo(d.RootCauseRef) ?? acao.RootCauseRef;
        acao.SupportArea = Limpo(d.SupportArea) ?? acao.SupportArea;
        acao.Dependencies = Limpo(d.Dependencies) ?? acao.Dependencies;
        acao.Evidence = Limpo(d.Evidence) ?? acao.Evidence;
        acao.Comments = Limpo(d.Comments) ?? acao.Comments;
        if (Grau(d.Complexity, VocabularioDoPlano.Graus) is { } comp) acao.Complexity = comp;
        if (Grau(d.RiskLevel, SeveridadeDoRisco.Graus) is { } risco) acao.RiskLevel = risco;
        if (d.Seq is { } seq) acao.Seq = seq;
        acao.UpdatedAt = clock.GetUtcNow();
        acao.Version += 1;
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
        if (await PlanoEncerradoAsync(acao.PlanId, ct))
            return (null, new("AP-ERR-020", "Plano encerrado não se edita. Reabra-o primeiro."));

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

    // ---- riscos, causa raiz e lições -----------------------------------------

    public async Task<(PlanRisk? risco, UserError? erro)> SalvarRiscoAsync(
        Guid planoId, Guid? riscoId, string descricao, string? probabilidade, string? impacto,
        string? mitigacao, Guid? responsavelId, string? situacao, CancellationToken ct = default)
    {
        if (await PlanoEncerradoAsync(planoId, ct))
            return (null, new("AP-ERR-020", "Plano encerrado não se edita. Reabra-o primeiro."));
        if (string.IsNullOrWhiteSpace(descricao) || descricao.Trim().Length < 5)
            return (null, new("AP-ERR-030", "Diga qual é o risco, em pelo menos 5 caracteres."));

        var risco = riscoId is { } rid
            ? await db.PlanRisks.SingleOrDefaultAsync(r => r.Id == rid && r.PlanId == planoId, ct)
            : null;
        if (riscoId is not null && risco is null)
            return (null, new("AP-ERR-404", "Risco não encontrado."));

        if (risco is null)
        {
            risco = new PlanRisk { PlanId = planoId, CreatedAt = clock.GetUtcNow() };
            db.PlanRisks.Add(risco);
        }
        risco.Description = descricao.Trim();
        if (Grau(probabilidade, SeveridadeDoRisco.Graus) is { } p) risco.Probability = p;
        if (Grau(impacto, SeveridadeDoRisco.Graus) is { } i) risco.Impact = i;
        risco.Mitigation = Limpo(mitigacao) ?? risco.Mitigation;
        if (responsavelId is { } dono)
        {
            risco.ResponsibleId = dono;
            risco.ResponsibleLabel = await db.Users.Where(u => u.Id == dono)
                .Select(u => u.Name).SingleOrDefaultAsync(ct);
        }
        if (Limpo(situacao) is { } s) risco.Status = s.ToUpperInvariant();
        await db.SaveChangesAsync(ct);
        return (risco, null);
    }

    public async Task<UserError?> ApagarRiscoAsync(Guid planoId, Guid riscoId, CancellationToken ct = default)
    {
        if (await PlanoEncerradoAsync(planoId, ct))
            return new("AP-ERR-020", "Plano encerrado não se edita. Reabra-o primeiro.");
        var risco = await db.PlanRisks.SingleOrDefaultAsync(r => r.Id == riscoId && r.PlanId == planoId, ct);
        if (risco is null) return new("AP-ERR-404", "Risco não encontrado.");
        db.PlanRisks.Remove(risco);
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>
    /// A causa raiz do plano. Ela convive com a do ciclo de melhoria e não a substitui: nem
    /// todo plano nasce de um ciclo, e o que nasce de uma reunião também precisa dizer que
    /// problema ataca.
    /// </summary>
    public async Task<(PlanRootCause? causa, UserError? erro)> SalvarCausaRaizAsync(
        Guid planoId, string? metodo, string? conteudo, string? causaPrincipal,
        CancellationToken ct = default)
    {
        if (await PlanoEncerradoAsync(planoId, ct))
            return (null, new("AP-ERR-020", "Plano encerrado não se edita. Reabra-o primeiro."));
        var agora = clock.GetUtcNow();
        var causa = await db.PlanRootCauses.FirstOrDefaultAsync(r => r.PlanId == planoId, ct);
        if (causa is null)
        {
            causa = new PlanRootCause { PlanId = planoId, CreatedAt = agora };
            db.PlanRootCauses.Add(causa);
        }
        if (Limpo(metodo) is { } m) causa.Method = m.ToUpperInvariant();
        if (conteudo is not null) causa.ContentJson = Limpo(conteudo);
        causa.MainCause = Limpo(causaPrincipal) ?? causa.MainCause;
        causa.UpdatedAt = agora;
        await db.SaveChangesAsync(ct);
        return (causa, null);
    }

    public async Task<(PlanLesson? licao, UserError? erro)> SalvarLicoesAsync(
        Guid planoId, string? funcionou, string? falhou, string? licoes,
        string? boaPratica, string? proximosPassos, string? recomendacao,
        CancellationToken ct = default)
    {
        var licao = await db.PlanLessons.FirstOrDefaultAsync(l => l.PlanId == planoId, ct);
        if (licao is null)
        {
            licao = new PlanLesson { PlanId = planoId, CreatedAt = clock.GetUtcNow() };
            db.PlanLessons.Add(licao);
        }
        licao.WhatWorked = Limpo(funcionou) ?? licao.WhatWorked;
        licao.WhatFailed = Limpo(falhou) ?? licao.WhatFailed;
        licao.Lessons = Limpo(licoes) ?? licao.Lessons;
        licao.BestPractice = Limpo(boaPratica) ?? licao.BestPractice;
        licao.NextSteps = Limpo(proximosPassos) ?? licao.NextSteps;
        licao.Recommendation = Limpo(recomendacao) ?? licao.Recommendation;
        await db.SaveChangesAsync(ct);
        return (licao, null);
    }

    // ---- apoios --------------------------------------------------------------

    private Task<bool> PlanoEncerradoAsync(Guid planoId, CancellationToken ct) =>
        db.ActionPlans.AnyAsync(p => p.Id == planoId && p.Life == VidaDoPlano.Encerrado, ct);

    private static string? Grau(string? valor, string[] aceitos)
    {
        var v = Limpo(valor)?.ToUpperInvariant();
        return v is not null && aceitos.Contains(v) ? v : null;
    }

    private async Task<string> ProximoCodigoAsync(DateTimeOffset agora, CancellationToken ct)
    {
        var prefixo = $"AP-{agora.Year}-";
        var usados = await db.ActionPlans.Where(p => p.Code.StartsWith(prefixo))
            .Select(p => p.Code).ToListAsync(ct);
        var maior = usados.Select(n => int.TryParse(n[prefixo.Length..], out var v) ? v : 0)
            .DefaultIfEmpty(0).Max();
        return $"{prefixo}{maior + 1:000}";
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
