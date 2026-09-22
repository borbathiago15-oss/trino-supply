using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Acoes;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Melhoria;

public record FiltroDeCiclos(string? Busca = null, string? Fase = null, string? Escopo = null);

public record DadosDoCiclo(
    string Title, string Scope, string? Region, Guid? SectorId, string? Areas, string? Priority,
    Guid? OwnerId, DateOnly? StartDate, DateOnly? EndDate,
    string? Problem, string? CurrentSituation,
    string? CauseAnalysis, string? RootCause, string? GoalDescription, string? Indicator,
    decimal? Baseline, decimal? GoalValue, string? Unit, DateOnly? GoalDeadline,
    DateOnly? CheckedOn, decimal? ResultValue, string? CheckAnalysis,
    string? Standardization, string? Lessons, bool? NewCycle,
    string? Phase = null, IReadOnlyList<string>? CostCenters = null,
    string? Leader = null, string? Mentor = null, string? Participants = null,
    decimal? AnnualSaving = null);

/// <param name="MetaAtingida">Nulo é recusado: o veredito é dito, não deduzido.</param>
/// <param name="ConfirmaPendencias">O segundo passo, quando sobrou ação em aberto.</param>
public record PedidoDeEncerramento(bool? MetaAtingida, string? Motivo, bool ConfirmaPendencias);

/// <summary>O ciclo com tudo o que a tela de detalhe mostra, já lido.</summary>
public record CicloCompleto(
    ImprovementCycle Ciclo, IReadOnlyList<AnaliseDeCausa> Analises, IReadOnlyList<ActionItem> Acoes,
    LeituraDoCiclo Leitura, string? SetorNome);

public record PlacarDosCiclos(int Total, int Plan, int Do, int Check, int Act, int Encerrados,
    int EncerradosComPendencia);

/// <summary>
/// O ciclo de melhoria: onde se trata a causa.
///
/// <para>
/// Três coisas aqui não se afrouxam, e as três vieram de defeito: o <b>encerramento</b> exige
/// veredito, motivo e — havendo pendência — confirmação explícita (§4); mudar a fase para
/// encerrado pela edição comum é <b>recusado</b>; e a <b>visibilidade</b> é um predicado só,
/// que a lista e a abertura do ciclo consultam igual.
/// </para>
/// </summary>
public class CicloDeMelhoriaService(AppDbContext db, TimeProvider clock)
{
    public const int TamanhoMaximo = 200;
    /// <summary>Motivo curto demais é campo vazio com mais passos: em duas semanas vira um botão de limpar a tela.</summary>
    public const int MinimoDoMotivo = 20;

    // ---- visibilidade (§6) ---------------------------------------------------

    /// <summary>
    /// Os ciclos que esta pessoa enxerga. É <b>a</b> régua: a lista, a abertura e o painel
    /// perguntam à mesma função — uma lista que mostra o que a pessoa não pode abrir é uma
    /// lista que mente.
    ///
    /// <para>
    /// Enxerga quem criou, quem é dono, quem foi marcado em "quem mais acompanha", quem
    /// responde por alguma ação do ciclo, e quem é do setor do ciclo. O <b>Gestor de
    /// Suprimentos</b> soma a isso os centros vinculados a ele e o que a equipe dele abriu —
    /// a visibilidade <b>sobe</b>, não desce: o ciclo do gestor só aparece para quem ele marcou.
    /// </para>
    ///
    /// <para>
    /// Duas ausências são deliberadas. <b>Centro na lista do usuário não abre ciclo</b> para
    /// perfil operacional: o ciclo de um centro pode tratar de assunto que não é de todo mundo.
    /// E <b>"gestão" não é público</b> — o plano da casa (reestruturação, corte de custo)
    /// entrava na lista de qualquer perfil só por não ter centro vinculado.
    /// </para>
    /// </summary>
    public async Task<IQueryable<ImprovementCycle>> VisiveisAsync(User eu, CancellationToken ct = default)
    {
        if (eu.Role == Roles.SystemAdministrator) return db.ImprovementCycles;

        // cada elo vira um array de ids: filtro na entidade e Contains com array, que é o que
        // o Npgsql traduz — Contains sobre subconsulta projetada passa no InMemory e dá 500 lá
        var acompanha = await db.CycleWatchers.Where(w => w.UserId == eu.Id)
            .Select(w => w.CycleId).Distinct().ToArrayAsync(ct);
        // "responsável por alguma ação do ciclo" passa pelo plano: a ação vive dentro dele,
        // e o ponteiro do ciclo mora no plano. Quem responde pelo plano inteiro também entra
        var meusPlanos = await db.ActionItems.Where(a => a.ResponsibleId == eu.Id)
            .Select(a => a.PlanId).Distinct().ToArrayAsync(ct);
        var planosQueLidero = await db.ActionPlanResponsibles.Where(r => r.UserId == eu.Id)
            .Select(r => r.PlanId).Distinct().ToArrayAsync(ct);
        var porAcao = await db.ActionPlans
            .Where(x => x.CycleId != null
                && (meusPlanos.Contains(x.Id) || planosQueLidero.Contains(x.Id)))
            .Select(x => x.CycleId!.Value).Distinct().ToArrayAsync(ct);

        Guid[] porCentro = [];
        Guid[] equipe = [];
        if (eu.Role == Roles.SupplyManager)
        {
            var meusCentros = (eu.CostCenters ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (meusCentros.Length > 0)
                porCentro = await db.CycleCostCenters
                    .Where(x => meusCentros.Contains(x.CostCenter))
                    .Select(x => x.CycleId).Distinct().ToArrayAsync(ct);
            equipe = await db.Users.Where(u => u.SupplyManagerId == eu.Id)
                .Select(u => u.Id).ToArrayAsync(ct);
        }

        var setor = eu.SectorId;
        return db.ImprovementCycles.Where(c =>
            c.CreatedBy == eu.Id
            || c.OwnerId == eu.Id
            || acompanha.Contains(c.Id)
            || porAcao.Contains(c.Id)
            || porCentro.Contains(c.Id)
            || equipe.Contains(c.CreatedBy)
            || (setor != null && c.SectorId == setor));
    }

    /// <summary>Mudar o centro do ciclo é escrever dado de centro de custo, e só quem o mantém escreve.</summary>
    public static bool PodeMudarEscopo(string role) =>
        role is Roles.SupplyManager or Roles.SystemAdministrator;

    /// <summary>
    /// Quem conduz o ciclo: o dono, quem o criou e o administrador. Para quem não conduz, a
    /// lista de "quem mais acompanha" <b>é</b> o acesso — deixá-los editá-la seria não ter
    /// regra nenhuma.
    /// </summary>
    public static bool Conduz(ImprovementCycle c, Actor ator) =>
        ator.IsAdmin || c.OwnerId == ator.Id || c.CreatedBy == ator.Id;

    // ---- leitura -------------------------------------------------------------

    public async Task<(IReadOnlyList<ImprovementCycle> itens, PlacarDosCiclos placar)> ListarAsync(
        Actor ator, FiltroDeCiclos f, CancellationToken ct = default)
    {
        var eu = await EuAsync(ator, ct);
        var consulta = await VisiveisAsync(eu, ct);
        if (!string.IsNullOrWhiteSpace(f.Fase))
        {
            var fase = f.Fase.Trim().ToUpperInvariant();
            consulta = consulta.Where(c => c.Phase == fase);
        }
        if (!string.IsNullOrWhiteSpace(f.Escopo))
        {
            var escopo = f.Escopo.Trim().ToUpperInvariant();
            consulta = consulta.Where(c => c.Scope == escopo);
        }
        if (!string.IsNullOrWhiteSpace(f.Busca))
        {
            var termo = f.Busca.Trim().ToLowerInvariant();
            consulta = consulta.Where(c =>
                c.Code.ToLower().Contains(termo) || c.Title.ToLower().Contains(termo));
        }

        var itens = await consulta.OrderByDescending(c => c.CreatedAt)
            .Take(TamanhoMaximo).ToListAsync(ct);
        return (itens, await PlacarAsync(itens, ct));
    }

    /// <summary>
    /// O placar sai do <b>mesmo recorte</b> que a lista mostra. O número de encerrados com
    /// pendência é o que a especificação chama de contradição exposta: ele existe para ser
    /// visto, não para ser escondido.
    /// </summary>
    private async Task<PlacarDosCiclos> PlacarAsync(
        IReadOnlyList<ImprovementCycle> itens, CancellationToken ct)
    {
        var encerrados = itens.Where(c => c.Phase == FaseDoCiclo.Encerrado).Select(c => c.Id).ToArray();
        var comPendencia = 0;
        if (encerrados.Length > 0)
        {
            var planos = await db.ActionPlans
                .Where(x => x.CycleId != null && encerrados.Contains(x.CycleId!.Value))
                .Select(x => new { x.Id, x.CycleId }).ToListAsync(ct);
            var idsDosPlanos = planos.Select(x => x.Id).ToArray();
            var abertas = await db.ActionItems
                .Where(a => idsDosPlanos.Contains(a.PlanId))
                .Select(a => new { a.PlanId, a.Status }).ToListAsync(ct);
            var doCiclo = planos.ToDictionary(x => x.Id, x => x.CycleId);
            comPendencia = abertas
                .Where(a => a.Status != StatusDaAcao.Concluida && a.Status != StatusDaAcao.Cancelada)
                .Select(a => doCiclo[a.PlanId]).Distinct().Count();
        }
        return new(itens.Count,
            itens.Count(c => c.Phase == FaseDoCiclo.Plan),
            itens.Count(c => c.Phase == FaseDoCiclo.Do),
            itens.Count(c => c.Phase == FaseDoCiclo.Check),
            itens.Count(c => c.Phase == FaseDoCiclo.Act),
            encerrados.Length, comPendencia);
    }

    public async Task<CicloCompleto?> AbrirAsync(Actor ator, Guid id, CancellationToken ct = default)
    {
        var visiveis = await VisiveisAsync(await EuAsync(ator, ct), ct);
        var ciclo = await visiveis.Include(c => c.Watchers).Include(c => c.CostCenters)
            .Include(c => c.Tools)
            .SingleOrDefaultAsync(c => c.Id == id, ct);
        if (ciclo is null) return null;
        return await CompletarAsync(ciclo, ct);
    }

    private async Task<CicloCompleto> CompletarAsync(ImprovementCycle ciclo, CancellationToken ct)
    {
        var acoes = await AcoesDoCicloAsync(ciclo.Id, ct);
        var analises = Analises(ciclo);
        var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var setor = ciclo.SectorId is { } sid
            ? await db.Sectors.Where(s => s.Id == sid).Select(s => s.Name).SingleOrDefaultAsync(ct)
            : null;
        return new(ciclo, analises, acoes, MotorDeLeitura.Ler(ciclo, analises, acoes, hoje), setor);
    }

    // ---- escrita -------------------------------------------------------------

    public async Task<(ImprovementCycle? ciclo, UserError? erro)> CriarAsync(
        Actor ator, DadosDoCiclo d, CancellationToken ct = default)
    {
        var eu = await EuAsync(ator, ct);
        if (string.IsNullOrWhiteSpace(d.Title) || d.Title.Trim().Length < 10)
            return (null, new("PDCA-ERR-010",
                "O título é uma frase, não um rótulo: diga o que se quer mudar, em pelo menos 10 caracteres."));

        var escopo = (d.Scope ?? "").Trim().ToUpperInvariant();
        if (!EscopoDoCiclo.Todos.Contains(escopo))
            return (null, new("PDCA-ERR-011", "Escopo inválido."));

        var (setorId, setorErro) = await SetorDoCicloAsync(eu, d.SectorId, ct);
        if (setorErro is not null) return (null, setorErro);

        var agora = clock.GetUtcNow();
        var ciclo = new ImprovementCycle
        {
            Code = await ProximoCodigoAsync(agora, ct),
            Title = d.Title.Trim(),
            Scope = escopo,
            Region = Limpo(d.Region),
            SectorId = setorId,
            Areas = Limpo(d.Areas),
            Priority = Limpo(d.Priority)?.ToUpperInvariant() ?? "NORMAL",
            OwnerId = d.OwnerId ?? ator.Id,
            StartDate = d.StartDate ?? DateOnly.FromDateTime(agora.UtcDateTime),
            EndDate = d.EndDate,
            CreatedBy = ator.Id, CreatedByLabel = ator.Label,
            CreatedAt = agora, UpdatedAt = agora,
        };
        if (await DonoAsync(ciclo.OwnerId, ct) is { } dono) ciclo.OwnerLabel = dono;
        else return (null, new("PDCA-ERR-012", "Dono do ciclo inválido: escolha um usuário ativo."));

        Aplicar(ciclo, d);
        foreach (var cc in Centros(d.CostCenters))
            ciclo.CostCenters.Add(new() { CycleId = ciclo.Id, CostCenter = cc });

        db.ImprovementCycles.Add(ciclo);
        await db.SaveChangesAsync(ct);
        return (ciclo, null);
    }

    /// <summary>
    /// Edita o ciclo. A fase se escolhe aqui — <b>menos</b> "encerrado", que tem operação
    /// própria (<see cref="EncerrarAsync"/>). Era pela trilha de fases que o ciclo fechava sem
    /// registrar quem decidiu, e é dessa porta que vinha o "encerrado" com ação atrasada.
    /// </summary>
    public async Task<(ImprovementCycle? ciclo, UserError? erro)> AtualizarAsync(
        Actor ator, Guid id, DadosDoCiclo d, CancellationToken ct = default)
    {
        var eu = await EuAsync(ator, ct);
        var ciclo = await db.ImprovementCycles.Include(c => c.CostCenters).Include(c => c.Tools)
            .SingleOrDefaultAsync(c => c.Id == id, ct);
        if (ciclo is null) return (null, new("PDCA-ERR-404", "Ciclo não encontrado."));

        if (!string.IsNullOrWhiteSpace(d.Phase))
        {
            var fase = d.Phase.Trim().ToUpperInvariant();
            if (fase == FaseDoCiclo.Encerrado)
                return (null, new("PDCA-ERR-044",
                    "Encerrar o ciclo não é mudar a fase: use o encerramento, que registra o veredito e quem decidiu."));
            if (!FaseDoCiclo.EmAndamento.Contains(fase))
                return (null, new("PDCA-ERR-013", "Fase inválida."));
            ciclo.Phase = fase;
        }

        if (!string.IsNullOrWhiteSpace(d.Title) && d.Title.Trim().Length >= 10) ciclo.Title = d.Title.Trim();
        if (d.OwnerId is { } novoDono && novoDono != ciclo.OwnerId)
        {
            if (await DonoAsync(novoDono, ct) is not { } label)
                return (null, new("PDCA-ERR-012", "Dono do ciclo inválido: escolha um usuário ativo."));
            ciclo.OwnerId = novoDono; ciclo.OwnerLabel = label;
        }
        if (d.SectorId is not null || eu.SectorId is not null)
        {
            var (setorId, setorErro) = await SetorDoCicloAsync(eu, d.SectorId, ct);
            if (setorErro is not null) return (null, setorErro);
            ciclo.SectorId = setorId;
        }
        ciclo.Region = Limpo(d.Region) ?? ciclo.Region;
        ciclo.Areas = Limpo(d.Areas) ?? ciclo.Areas;
        if (Limpo(d.Priority) is { } p) ciclo.Priority = p.ToUpperInvariant();
        ciclo.StartDate = d.StartDate ?? ciclo.StartDate;
        ciclo.EndDate = d.EndDate ?? ciclo.EndDate;

        Aplicar(ciclo, d);
        ciclo.UpdatedAt = clock.GetUtcNow();
        ciclo.Version += 1;
        await db.SaveChangesAsync(ct);
        return (ciclo, null);
    }

    /// <summary>
    /// Os campos das quatro fases. A causa raiz dos 5 Porquês <b>preenche a do ciclo</b> se
    /// ninguém escreveu outra: é o mesmo dado, e digitá-lo duas vezes é convite a divergir.
    /// </summary>
    private static void Aplicar(ImprovementCycle c, DadosDoCiclo d)
    {
        c.Problem = Limpo(d.Problem) ?? c.Problem;
        c.CurrentSituation = Limpo(d.CurrentSituation) ?? c.CurrentSituation;
        c.CauseAnalysis = Limpo(d.CauseAnalysis) ?? c.CauseAnalysis;
        c.RootCause = Limpo(d.RootCause) ?? c.RootCause;
        c.GoalDescription = Limpo(d.GoalDescription) ?? c.GoalDescription;
        c.Indicator = Limpo(d.Indicator) ?? c.Indicator;
        if (d.Baseline is not null) c.Baseline = d.Baseline;
        if (d.GoalValue is not null) c.GoalValue = d.GoalValue;
        c.Unit = Limpo(d.Unit) ?? c.Unit;
        c.GoalDeadline = d.GoalDeadline ?? c.GoalDeadline;

        c.CheckedOn = d.CheckedOn ?? c.CheckedOn;
        if (d.ResultValue is not null) c.ResultValue = d.ResultValue;
        c.CheckAnalysis = Limpo(d.CheckAnalysis) ?? c.CheckAnalysis;

        c.Standardization = Limpo(d.Standardization) ?? c.Standardization;
        c.Lessons = Limpo(d.Lessons) ?? c.Lessons;
        if (d.NewCycle is { } n) c.NewCycle = n;

        c.Leader = Limpo(d.Leader) ?? c.Leader;
        c.Mentor = Limpo(d.Mentor) ?? c.Mentor;
        c.Participants = Limpo(d.Participants) ?? c.Participants;
        if (d.AnnualSaving is not null) c.AnnualSaving = d.AnnualSaving;

        PreencherCausaRaiz(c);
    }

    /// <summary>
    /// A causa raiz dos 5 Porquês preenche a do ciclo <b>se ninguém escreveu outra</b>. É o
    /// mesmo dado, e digitá-lo duas vezes é convite a divergir.
    /// </summary>
    private static void PreencherCausaRaiz(ImprovementCycle c)
    {
        if (!string.IsNullOrWhiteSpace(c.RootCause)) return;
        var porques = c.Tools.FirstOrDefault(t => t.ToolType == FerramentaDeCausa.CincoPorques);
        if (porques is null) return;
        if (FerramentaDeCausa.Normalizar(porques.ToolType, porques.ToolData)?.CausaRaiz is { } raiz)
            c.RootCause = raiz;
    }

    /// <summary>Todas as ferramentas do ciclo, já normalizadas e na ordem em que foram usadas.</summary>
    public static IReadOnlyList<AnaliseDeCausa> Analises(ImprovementCycle c) =>
        [.. c.Tools.OrderBy(t => t.Seq)
            .Select(t => FerramentaDeCausa.Normalizar(t.ToolType, t.ToolData))
            .Where(a => a is not null).Select(a => a!)];

    // ---- as ferramentas da folha ---------------------------------------------

    /// <summary>
    /// Guarda uma ferramenta preenchida. <b>Uma por tipo</b>: dois Paretos no mesmo ciclo
    /// dariam duas respostas para "qual é a causa vital", e a tela mostraria a que carregasse
    /// primeiro. Salvar de novo corrige a que existe.
    /// </summary>
    public async Task<(ImprovementCycle? ciclo, UserError? erro)> SalvarFerramentaAsync(
        Guid id, string tipo, string? dados, CancellationToken ct = default)
    {
        var ciclo = await db.ImprovementCycles.Include(c => c.Tools)
            .SingleOrDefaultAsync(c => c.Id == id, ct);
        if (ciclo is null) return (null, new("PDCA-ERR-404", "Ciclo não encontrado."));

        var chave = (tipo ?? "").Trim().ToUpperInvariant();
        if (!FerramentaDeCausa.Todas.Contains(chave))
            return (null, new("PDCA-ERR-015", "Ferramenta de análise inválida."));

        var agora = clock.GetUtcNow();
        var ferramenta = ciclo.Tools.FirstOrDefault(t => t.ToolType == chave);
        if (ferramenta is null)
        {
            ferramenta = new CycleTool
            {
                CycleId = ciclo.Id, ToolType = chave,
                Seq = ciclo.Tools.Count == 0 ? 1 : ciclo.Tools.Max(t => t.Seq) + 1,
            };
            // só pelo DbSet: o EF põe a ferramenta de volta na navegação do pai rastreado,
            // e adicioná-la aqui também a deixaria duas vezes na folha
            db.CycleTools.Add(ferramenta);
        }
        ferramenta.ToolData = Limpo(dados);
        ferramenta.UpdatedAt = agora;

        PreencherCausaRaiz(ciclo);
        ciclo.UpdatedAt = agora;
        await db.SaveChangesAsync(ct);
        return (ciclo, null);
    }

    public async Task<(ImprovementCycle? ciclo, UserError? erro)> RemoverFerramentaAsync(
        Guid id, string tipo, CancellationToken ct = default)
    {
        var ciclo = await db.ImprovementCycles.Include(c => c.Tools)
            .SingleOrDefaultAsync(c => c.Id == id, ct);
        if (ciclo is null) return (null, new("PDCA-ERR-404", "Ciclo não encontrado."));

        var chave = (tipo ?? "").Trim().ToUpperInvariant();
        var ferramenta = ciclo.Tools.FirstOrDefault(t => t.ToolType == chave);
        if (ferramenta is null) return (null, new("PDCA-ERR-016", "Este ciclo não usa essa ferramenta."));

        db.CycleTools.Remove(ferramenta);
        ciclo.Tools.Remove(ferramenta);
        ciclo.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return (ciclo, null);
    }

    // ---- encerramento (§4) ---------------------------------------------------

    /// <summary>
    /// Encerra o ciclo. <b>Prazo vencido não encerra nada</b> — quem encerra é uma pessoa, e as
    /// três coisas que ela precisa dizer são cobradas aqui: o veredito (a meta foi atingida?),
    /// o motivo em texto, e — se sobrou ação em aberto — a confirmação explícita, com a lista
    /// do que sobrou anexada ao motivo.
    ///
    /// <para>
    /// O ciclo <b>pode</b> fechar com ação em aberto: às vezes é a decisão certa. O que não pode
    /// é fechar sem ninguém assumir isso.
    /// </para>
    /// </summary>
    public async Task<(ImprovementCycle? ciclo, UserError? erro, IReadOnlyList<ActionItem> pendentes)>
        EncerrarAsync(Actor ator, Guid id, PedidoDeEncerramento p, CancellationToken ct = default)
    {
        var ciclo = await db.ImprovementCycles.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (ciclo is null) return (null, new("PDCA-ERR-404", "Ciclo não encontrado."), []);
        if (ciclo.Phase == FaseDoCiclo.Encerrado)
            return (null, new("PDCA-ERR-042", "Este ciclo já está encerrado."), []);

        if (p.MetaAtingida is not { } atingida)
            return (null, new("PDCA-ERR-043",
                "Diga se a meta foi atingida: o veredito é de quem conduziu, não do indicador."), []);

        var motivo = Limpo(p.Motivo);
        if (motivo is null || motivo.Length < MinimoDoMotivo)
            return (null, new("PDCA-ERR-040",
                $"Diga por que o ciclo está sendo encerrado, em pelo menos {MinimoDoMotivo} caracteres."), []);

        var acoes = await AcoesDoCicloAsync(id, ct);
        var pendentes = acoes.Where(PlanoDeAcao.Aberta).ToList();
        if (pendentes.Count > 0 && !p.ConfirmaPendencias)
            return (null, new("PDCA-ERR-041",
                $"Sobraram {pendentes.Count} ação(ões) em aberto. Confirme que o ciclo fecha assim."), pendentes);

        if (pendentes.Count > 0)
            motivo += "\n\nEncerrado com " + pendentes.Count + " ação(ões) em aberto: "
                + string.Join("; ", pendentes.Select(a => $"{a.Number} {a.Title}")) + ".";

        var agora = clock.GetUtcNow();
        ciclo.Phase = FaseDoCiclo.Encerrado;
        ciclo.GoalMet = atingida;
        ciclo.ClosedAt = agora;
        ciclo.ClosedById = ator.Id;
        ciclo.ClosedByLabel = ator.Label;
        ciclo.ClosedReason = motivo;
        ciclo.UpdatedAt = agora;
        ciclo.Version += 1;
        await db.SaveChangesAsync(ct);
        return (ciclo, null, pendentes);
    }

    /// <summary>
    /// Reabre o ciclo, e <b>apaga o veredito</b>: quem encerrou, quando e por quê não valem
    /// mais para o ciclo em curso. Guardá-los faria a tela mostrar um encerramento que já não
    /// é verdade, e a próxima leitura acreditaria nele.
    /// </summary>
    public async Task<(ImprovementCycle? ciclo, UserError? erro)> ReabrirAsync(
        Guid id, string? fase, CancellationToken ct = default)
    {
        var ciclo = await db.ImprovementCycles.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (ciclo is null) return (null, new("PDCA-ERR-404", "Ciclo não encontrado."));
        if (ciclo.Phase != FaseDoCiclo.Encerrado)
            return (null, new("PDCA-ERR-045", "Este ciclo não está encerrado."));

        var destino = (fase ?? FaseDoCiclo.Act).Trim().ToUpperInvariant();
        if (!FaseDoCiclo.EmAndamento.Contains(destino)) destino = FaseDoCiclo.Act;

        ciclo.Phase = destino;
        ciclo.GoalMet = null;
        ciclo.ClosedAt = null;
        ciclo.ClosedById = null;
        ciclo.ClosedByLabel = null;
        ciclo.ClosedReason = null;
        ciclo.UpdatedAt = clock.GetUtcNow();
        ciclo.Version += 1;
        await db.SaveChangesAsync(ct);
        return (ciclo, null);
    }

    // ---- mudança de escopo (§5) ----------------------------------------------

    /// <summary>
    /// Troca o escopo do ciclo e <b>leva as ações junto</b>. Não é editar um campo: as ações
    /// nasceram apontando para o centro antigo e ficariam lá, contando no painel do centro
    /// errado e invisíveis para quem olha o ciclo.
    ///
    /// <para>
    /// Só acontece quando o destino é <b>sem ambiguidade</b>: um centro só, ou um escopo que
    /// não tem centro nenhum. Com dois centros de destino não há para onde mandar cada ação, e
    /// escolher por ela seria inventar o dado.
    /// </para>
    /// </summary>
    public async Task<(ImprovementCycle? ciclo, int acoesMovidas, UserError? erro)> MudarEscopoAsync(
        Guid id, string escopo, IReadOnlyList<string>? centros, CancellationToken ct = default)
    {
        var ciclo = await db.ImprovementCycles.Include(c => c.CostCenters)
            .SingleOrDefaultAsync(c => c.Id == id, ct);
        if (ciclo is null) return (null, 0, new("PDCA-ERR-404", "Ciclo não encontrado."));

        var novo = (escopo ?? "").Trim().ToUpperInvariant();
        if (!EscopoDoCiclo.Todos.Contains(novo))
            return (null, 0, new("PDCA-ERR-011", "Escopo inválido."));

        var lista = Centros(centros);
        if (novo is EscopoDoCiclo.Setor or EscopoDoCiclo.Gestao) lista = [];
        if (novo == EscopoDoCiclo.Centro && lista.Count != 1)
            return (null, 0, new("PDCA-ERR-050",
                "Um ciclo de centro de custo tem exatamente um centro — é para ele que as ações vão."));
        if (novo is EscopoDoCiclo.Multi or EscopoDoCiclo.Regional && lista.Count == 0)
            return (null, 0, new("PDCA-ERR-051", "Informe os centros de custo do ciclo."));

        // destino sem ambiguidade: um centro só, ou nenhum
        var destino = lista.Count == 1 ? lista[0] : null;
        if (lista.Count > 1)
            return (null, 0, new("PDCA-ERR-052",
                "Com mais de um centro de destino não há para onde mandar cada ação: mova o ciclo para um centro só, ou ajuste as ações antes."));

        // o plano vai junto: ele é quem aponta para o ciclo, e deixá-lo no centro antigo
        // faria a ação mudar de dono sem o plano mudar
        var planos = await db.ActionPlans.Where(x => x.CycleId == id).ToListAsync(ct);
        foreach (var plano in planos) plano.CostCenter = destino;
        var acoes = await AcoesDoCicloAsync(id, ct);
        foreach (var a in acoes) a.CostCenter = destino;

        ciclo.Scope = novo;
        Trocar(db.CycleCostCenters, ciclo.CostCenters,
            [.. lista.Select(cc => new CycleCostCenter { CycleId = ciclo.Id, CostCenter = cc })]);
        ciclo.UpdatedAt = clock.GetUtcNow();
        ciclo.Version += 1;
        await db.SaveChangesAsync(ct);
        return (ciclo, acoes.Count, null);
    }

    // ---- quem mais acompanha -------------------------------------------------

    /// <summary>
    /// Define "quem mais acompanha". Só quem conduz: para quem não conduz, essa lista <b>é</b>
    /// o acesso ao ciclo, e deixá-los editá-la seria não ter regra nenhuma.
    /// </summary>
    public async Task<(ImprovementCycle? ciclo, UserError? erro)> DefinirAcompanhantesAsync(
        Actor ator, Guid id, IReadOnlyList<Guid> usuarios, CancellationToken ct = default)
    {
        var ciclo = await db.ImprovementCycles.Include(c => c.Watchers)
            .SingleOrDefaultAsync(c => c.Id == id, ct);
        if (ciclo is null) return (null, new("PDCA-ERR-404", "Ciclo não encontrado."));
        if (!Conduz(ciclo, ator))
            return (null, new("PDCA-ERR-061",
                "Quem marca o acompanhamento é quem conduz o ciclo — o dono ou quem o abriu."));

        var ids = usuarios.Distinct().ToArray();
        var gente = await db.Users.Where(u => ids.Contains(u.Id) && u.Active)
            .Select(u => new { u.Id, u.Name }).ToListAsync(ct);

        Trocar(db.CycleWatchers, ciclo.Watchers,
            [.. gente.Select(g => new CycleWatcher
            {
                CycleId = ciclo.Id, UserId = g.Id, UserLabel = g.Name,
            })]);
        ciclo.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return (ciclo, null);
    }

    // ---- apoios --------------------------------------------------------------

    /// <summary>
    /// Quem pertence a um setor <b>não escolhe</b> o setor do ciclo: é o dele. Deixar escolher
    /// abriria a porta de pendurar trabalho no setor alheio, e a visibilidade por setor
    /// deixaria de querer dizer alguma coisa.
    /// </summary>
    private async Task<(Guid? setor, UserError? erro)> SetorDoCicloAsync(
        User eu, Guid? pedido, CancellationToken ct)
    {
        if (eu.SectorId is { } meu) return (meu, null);
        if (pedido is not { } id) return (null, null);
        return await db.Sectors.AnyAsync(s => s.Id == id && s.Active, ct)
            ? (id, null)
            : (null, new("PDCA-ERR-014", "Setor inválido: escolha um setor ativo do cadastro."));
    }

    /// <summary>
    /// Troca o conteúdo de uma associação do ciclo.
    ///
    /// <para>
    /// A inclusão passa pelo <c>DbSet</c>, e não só pela coleção: a entidade nasce com
    /// <c>Id</c> preenchido, e o EF lê chave não vazia em filho anexado pela navegação como
    /// "já existe" — o registro ia para o banco como <i>update</i> de uma linha que nunca
    /// esteve lá.
    /// </para>
    /// </summary>
    private static void Trocar<T>(
        Microsoft.EntityFrameworkCore.DbSet<T> conjunto, List<T> atual, List<T> novos) where T : class
    {
        conjunto.RemoveRange(atual);
        atual.Clear();
        // só pelo DbSet: o EF põe o filho de volta na navegação do pai rastreado, e adicioná-lo
        // aqui também o deixaria duas vezes na lista que a resposta devolve
        foreach (var n in novos) conjunto.Add(n);
    }

    /// <summary>
    /// As ações do ciclo — as dos planos que apontam para ele. A ação não aponta mais para o
    /// ciclo: ela vive dentro de um plano, e é o plano que diz de qual ciclo nasceu.
    /// </summary>
    private async Task<List<ActionItem>> AcoesDoCicloAsync(Guid cicloId, CancellationToken ct)
    {
        var planos = await db.ActionPlans.Where(p => p.CycleId == cicloId)
            .Select(p => p.Id).ToArrayAsync(ct);
        if (planos.Length == 0) return [];
        return await db.ActionItems.Where(a => planos.Contains(a.PlanId))
            .OrderBy(a => a.DueDate == null).ThenBy(a => a.DueDate).ThenBy(a => a.Seq)
            .ToListAsync(ct);
    }

    /// <summary>
    /// O usuário por trás do ator. O papel vem do token, mas setor e centros vinculados vêm do
    /// cadastro — e a visibilidade depende deles, não do que o token carrega.
    /// </summary>
    private async Task<User> EuAsync(Actor ator, CancellationToken ct) =>
        await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == ator.Id, ct)
        ?? new User { Id = ator.Id, Name = ator.Label, Role = ator.Role };

    private Task<string?> DonoAsync(Guid? id, CancellationToken ct) =>
        id is { } dono
            ? db.Users.Where(u => u.Id == dono && u.Active).Select(u => u.Name).SingleOrDefaultAsync(ct)!
            : Task.FromResult<string?>(null);

    private static List<string> Centros(IReadOnlyList<string>? centros) =>
        [.. (centros ?? []).Select(c => c?.Trim().ToUpperInvariant())
            .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!).Distinct()];

    private async Task<string> ProximoCodigoAsync(DateTimeOffset agora, CancellationToken ct)
    {
        var prefixo = $"PDCA-{agora.Year}-";
        var usados = await db.ImprovementCycles.Where(c => c.Code.StartsWith(prefixo))
            .Select(c => c.Code).ToListAsync(ct);
        var maior = usados.Select(n => int.TryParse(n[prefixo.Length..], out var v) ? v : 0)
            .DefaultIfEmpty(0).Max();
        return $"{prefixo}{maior + 1:000}";
    }

    private static string? Limpo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
