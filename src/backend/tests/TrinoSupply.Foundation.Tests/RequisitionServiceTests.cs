using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

public class RequisitionServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeNumbers : IPrNumberGenerator
    {
        private int _next;
        public Task<string> NextAsync(CancellationToken ct = default) =>
            Task.FromResult($"PR-2026-{++_next:000000}");
    }

    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana Solicitante", Roles.Requester);
    private static readonly Actor Bruno = new(Guid.NewGuid(), "Bruno Aprovador", Roles.Approver);
    private static readonly Actor Clara = new(Guid.NewGuid(), "Clara Auditora", Roles.Auditor);

    private static readonly ItemInput Notebook = new("Notebook 14\" i7", 10, "UN", 1523.06m, null);

    private static (RequisitionService svc, AppDbContext db, FixedTimeProvider clock) Build()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        return (new RequisitionService(db, new FakeNumbers(), new CatalogService(db, clock), clock), db, clock);
    }

    private static async Task<PurchaseRequisition> DraftAsync(RequisitionService svc, Actor requester, params ItemInput[] items)
    {
        var (pr, error) = await svc.CreateAsync(requester, "Reposição de equipamentos", "CC-TI-001", "NORMAL", null, items);
        Assert.Null(error);
        return pr!;
    }

    [Fact]
    public async Task Criacao_gera_numero_sequencial_em_rascunho_com_total_calculado()
    {
        var (svc, _, _) = Build();

        var pr = await DraftAsync(svc, Ana, Notebook);

        Assert.Equal("PR-2026-000001", pr.Number);
        Assert.Equal(RequisitionStatus.Draft, pr.Status);
        Assert.Equal(1, pr.Cycle);
        Assert.Equal(15230.60m, pr.TotalEstimatedValue);
    }

    [Fact]
    public async Task Quantidade_zero_e_recusada_com_PR_ERR_010()
    {
        var (svc, _, _) = Build();

        var (_, error) = await svc.CreateAsync(Ana, "Justificativa", "CC-01", null, null,
            [new ItemInput("Cabo de rede", 0, "UN", 10, null)]);

        Assert.Equal("PR-ERR-010", error!.Code);
    }

    [Fact]
    public async Task Submissao_sem_itens_e_recusada_com_PR_ERR_030()
    {
        var (svc, _, _) = Build();
        var pr = await DraftAsync(svc, Ana);

        var (_, error) = await svc.SubmitAsync(Ana, pr.Id);

        Assert.Equal("PR-ERR-030", error!.Code);
    }

    [Fact]
    public async Task Submissao_com_data_de_necessidade_no_passado_e_recusada_com_PR_ERR_050()
    {
        var (svc, _, _) = Build();
        var (pr, _) = await svc.CreateAsync(Ana, "Justificativa", "CC-01", null,
            new DateOnly(2026, 8, 1), [Notebook]);

        var (_, error) = await svc.SubmitAsync(Ana, pr!.Id);

        Assert.Equal("PR-ERR-050", error!.Code);
    }

    [Fact]
    public async Task Submissao_valida_leva_a_IN_APPROVAL()
    {
        var (svc, _, _) = Build();
        var pr = await DraftAsync(svc, Ana, Notebook);

        var (submitted, error) = await svc.SubmitAsync(Ana, pr.Id);

        Assert.Null(error);
        Assert.Equal(RequisitionStatus.InApproval, submitted!.Status);
        Assert.NotNull(submitted.SubmittedAt);
    }

    [Fact]
    public async Task Solicitante_nao_decide_a_propria_requisicao_SoD()
    {
        var (svc, _, _) = Build();
        var pr = await DraftAsync(svc, Ana, Notebook);
        await svc.SubmitAsync(Ana, pr.Id);

        var (_, error) = await svc.ApproveAsync(Ana, pr.Id, null);

        Assert.Equal("PR-ERR-041", error!.Code);
    }

    [Fact]
    public async Task Aprovacao_valida_leva_a_APPROVED_com_decisor_registrado()
    {
        var (svc, _, _) = Build();
        var pr = await DraftAsync(svc, Ana, Notebook);
        await svc.SubmitAsync(Ana, pr.Id);

        var (approved, error) = await svc.ApproveAsync(Bruno, pr.Id, "Ok, dentro do orçamento");

        Assert.Null(error);
        Assert.Equal(RequisitionStatus.Approved, approved!.Status);
        Assert.Equal(Bruno.Id, approved.DecidedById);
    }

    [Fact]
    public async Task Rejeicao_exige_motivo()
    {
        var (svc, _, _) = Build();
        var pr = await DraftAsync(svc, Ana, Notebook);
        await svc.SubmitAsync(Ana, pr.Id);

        var (_, missing) = await svc.RejectAsync(Bruno, pr.Id, "  ");
        var (rejected, ok) = await svc.RejectAsync(Bruno, pr.Id, "Fora de política");

        Assert.Equal("PR-ERR-030", missing!.Code);
        Assert.Null(ok);
        Assert.Equal(RequisitionStatus.Rejected, rejected!.Status);
    }

    [Fact]
    public async Task Devolucao_permite_editar_e_resubmeter_incrementando_o_ciclo()
    {
        var (svc, _, _) = Build();
        var pr = await DraftAsync(svc, Ana, Notebook);
        await svc.SubmitAsync(Ana, pr.Id);
        await svc.ReturnAsync(Bruno, pr.Id, "Detalhar especificação");

        var (_, editError) = await svc.UpdateHeaderAsync(Ana, pr.Id, "Justificativa detalhada", null, null, null, false);
        var (resubmitted, submitError) = await svc.SubmitAsync(Ana, pr.Id);

        Assert.Null(editError);
        Assert.Null(submitError);
        Assert.Equal(RequisitionStatus.InApproval, resubmitted!.Status);
        Assert.Equal(2, resubmitted.Cycle);
        Assert.Null(resubmitted.DecisionReason);
    }

    [Fact]
    public async Task Edicao_e_itens_sao_bloqueados_durante_a_aprovacao()
    {
        var (svc, _, _) = Build();
        var pr = await DraftAsync(svc, Ana, Notebook);
        await svc.SubmitAsync(Ana, pr.Id);

        var (_, edit) = await svc.UpdateHeaderAsync(Ana, pr.Id, "Nova justificativa", null, null, null, false);
        var (_, addItem) = await svc.AddItemAsync(Ana, pr.Id, Notebook);

        Assert.Equal("PR-ERR-040", edit!.Code);
        Assert.Equal("PR-ERR-040", addItem!.Code);
    }

    [Fact]
    public async Task Aprovar_algo_fora_de_IN_APPROVAL_e_recusado_com_PR_ERR_040()
    {
        var (svc, _, _) = Build();
        var pr = await DraftAsync(svc, Ana, Notebook);

        var (_, error) = await svc.ApproveAsync(Bruno, pr.Id, null);

        Assert.Equal("PR-ERR-040", error!.Code);
    }

    [Fact]
    public async Task Cancelamento_exige_motivo_e_nao_alcanca_aprovadas()
    {
        var (svc, _, _) = Build();
        var pr = await DraftAsync(svc, Ana, Notebook);
        var (_, noReason) = await svc.CancelAsync(Ana, pr.Id, "");
        await svc.SubmitAsync(Ana, pr.Id);
        await svc.ApproveAsync(Bruno, pr.Id, null);

        var (_, afterApproval) = await svc.CancelAsync(Ana, pr.Id, "Não preciso mais");

        Assert.Equal("PR-ERR-030", noReason!.Code);
        Assert.Equal("PR-ERR-040", afterApproval!.Code); // política cancel-after-approval = false
    }

    [Fact]
    public async Task Escopo_solicitante_ve_so_as_suas_e_fila_exclui_as_proprias()
    {
        var (svc, _, _) = Build();
        var deAna = await DraftAsync(svc, Ana, Notebook);
        var outroSolicitante = new Actor(Guid.NewGuid(), "Outro", Roles.Requester);
        var deOutro = await DraftAsync(svc, outroSolicitante, Notebook);
        await svc.SubmitAsync(Ana, deAna.Id);
        await svc.SubmitAsync(outroSolicitante, deOutro.Id);

        Assert.Single(await svc.ListAsync(Ana, null));                       // só a própria
        Assert.Null(await svc.GetAsync(Ana, deOutro.Id));                    // fora do escopo ⇒ 404
        Assert.Equal(2, (await svc.ListAsync(Clara, null)).Count);           // auditor vê tudo
        var filaDeBrunoQueTambemSolicitou = await svc.PendingApprovalsAsync(new Actor(Ana.Id, Ana.Label, Roles.Approver));
        Assert.DoesNotContain(filaDeBrunoQueTambemSolicitou, r => r.Id == deAna.Id); // SoD na fila
    }

    [Fact]
    public async Task Exclusao_somente_de_rascunho_pelo_titular()
    {
        var (svc, db, _) = Build();
        var pr = await DraftAsync(svc, Ana, Notebook);
        var outro = new Actor(Guid.NewGuid(), "Outro", Roles.Requester);

        var byOther = await svc.DeleteDraftAsync(outro, pr.Id);
        await svc.SubmitAsync(Ana, pr.Id);
        var afterSubmit = await svc.DeleteDraftAsync(Ana, pr.Id);

        Assert.NotNull(byOther);              // fora do escopo do outro solicitante ⇒ 404
        Assert.Equal("PR-ERR-040", afterSubmit!.Code);
        Assert.NotNull(await db.Requisitions.SingleAsync(r => r.Id == pr.Id)); // nunca some fisicamente
    }
}
