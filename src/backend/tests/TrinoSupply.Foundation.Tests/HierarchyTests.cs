using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// Alçadas por centro de custo (vínculos CC → gerente → diretor):
/// Júnior solicita só dos CCs vinculados; Pleno aprova só os CCs que gerencia;
/// Diretor decide só processos roteados a ele; empresa (CNPJ) por CC.
/// </summary>
public class HierarchyTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeNumbers : IPrNumberGenerator
    {
        private int _next;
        public Task<string> NextAsync(CancellationToken ct = default) =>
            Task.FromResult($"PR-2026-{++_next:000000}");
    }

    private sealed record World(
        AppDbContext Db, RequisitionService Prs, QuotationService Rfq,
        SupplierService Sup, CompanyService Companies, CostCenterService Ccs,
        Actor Junior, Actor Pleno, Actor OutroPleno, Actor Carla, Actor Gustavo,
        Actor Diana, Actor Otto, Supplier Alfa);

    private static User MakeUser(Actor a, string? costCenters = null, Guid? directorId = null) => new()
    {
        Id = a.Id, Email = $"{a.Id:N}@trino.dev", Name = a.Label, Role = a.Role,
        PasswordHash = "x", CostCenters = costCenters, DirectorId = directorId,
    };

    private static async Task<World> BuildAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));

        var junior = new Actor(Guid.NewGuid(), "Júlio Júnior", Roles.Requester);
        var pleno = new Actor(Guid.NewGuid(), "Thiago Gerente", Roles.Approver);
        var outroPleno = new Actor(Guid.NewGuid(), "Otávio Gerente", Roles.Approver);
        var carla = new Actor(Guid.NewGuid(), "Carla Compradora", Roles.PurchasingOfficer);
        var gustavo = new Actor(Guid.NewGuid(), "Gustavo Gestor", Roles.SupplyManager);
        var diana = new Actor(Guid.NewGuid(), "Gerson Diretor", Roles.Director);
        var otto = new Actor(Guid.NewGuid(), "Outro Diretor", Roles.Director);

        db.Users.AddRange(
            MakeUser(junior, costCenters: "PBA-001"),
            MakeUser(pleno, costCenters: "PBA-001", directorId: diana.Id),
            MakeUser(outroPleno),
            MakeUser(carla),
            MakeUser(gustavo, directorId: diana.Id),
            MakeUser(diana),
            MakeUser(otto));
        db.CostCenters.AddRange(
            new CostCenter { Code = "PBA-001", Name = "Whirlpool PB", Region = "PARAIBA", ManagerUserId = pleno.Id, ManagerName = pleno.Label, CreatedBy = gustavo.Id },
            new CostCenter { Code = "BAH-001", Name = "PepsiCo BA", Region = "BAHIA", ManagerUserId = outroPleno.Id, ManagerName = outroPleno.Label, CreatedBy = gustavo.Id });
        await db.SaveChangesAsync();

        var prs = new RequisitionService(db, new FakeNumbers(), new CatalogService(db, clock), clock);
        var rfq = new QuotationService(db, clock);
        var sup = new SupplierService(db, clock);
        var (alfa, _) = await sup.CreateAsync(carla.Id, "Alfa LTDA", "Alfa", "12345678000190", null, null);
        // fornecedor novo nasce PROSPECT: participar da cotação é livre, vencer exige
        // homologação (SUP-ERR-030). O cenário destes testes é o do fornecedor já
        // homologado — quem cuida do caminho do prospect é o teste próprio dele.
        await sup.SetHomologationAsync(alfa!.Id, SupplierHomologation.Homologado);
        return new World(db, prs, rfq, sup, new CompanyService(db, clock), new CostCenterService(db, clock),
            junior, pleno, outroPleno, carla, gustavo, diana, otto, alfa!);
    }

    /// <summary>Coloca a SC no estado do fluxo anterior (autorização prévia, sem preço).</summary>
    private static async Task PutInLegacyApprovalAsync(World w, PurchaseRequisition pr)
    {
        var tracked = await w.Db.Requisitions.SingleAsync(r => r.Id == pr.Id);
        tracked.Status = RequisitionStatus.InApproval;
        await w.Db.SaveChangesAsync();
    }

    private static Task<(PurchaseRequisition? pr, UserError? error)> CreatePrAsync(World w, Actor actor, string cc) =>
        w.Prs.CreateAsync(actor, "Material de manutenção", cc, "NORMAL", null,
            [new ItemInput("Detergente neutro", 2, "UN", 10, null)]);

    // ---- solicitação: escopo do Júnior/Pleno ---------------------------------

    [Fact]
    public async Task Junior_com_vinculo_so_solicita_dos_ccs_vinculados()
    {
        var w = await BuildAsync();
        var (_, blocked) = await CreatePrAsync(w, w.Junior, "BAH-001");
        Assert.Equal("PR-ERR-021", blocked!.Code);
        var (ok, error) = await CreatePrAsync(w, w.Junior, "PBA-001");
        Assert.Null(error);
        Assert.NotNull(ok);
    }

    [Fact]
    public async Task Junior_sem_vinculo_solicita_de_qualquer_cc()
    {
        var w = await BuildAsync();
        var livre = new Actor(Guid.NewGuid(), "Livre", Roles.Requester);
        w.Db.Users.Add(MakeUser(livre));
        await w.Db.SaveChangesAsync();
        var (pr, error) = await CreatePrAsync(w, livre, "BAH-001");
        Assert.Null(error);
        Assert.NotNull(pr);
    }

    [Fact]
    public async Task Edicao_de_pedido_nao_permite_mudar_para_cc_fora_do_vinculo()
    {
        var w = await BuildAsync();
        var (pr, _) = await CreatePrAsync(w, w.Junior, "PBA-001");
        var (_, blocked) = await w.Prs.UpdateHeaderAsync(w.Junior, pr!.Id, null, "BAH-001", null, null, false);
        Assert.Equal("PR-ERR-021", blocked!.Code);
        var (updated, error) = await w.Prs.UpdateHeaderAsync(w.Junior, pr.Id, "Justificativa ajustada", null, "HIGH", null, false);
        Assert.Null(error);
        Assert.Equal("HIGH", updated!.Priority);
    }

    // ---- aprovação da SC: escopo do Pleno ------------------------------------

    [Fact]
    public async Task Pleno_so_aprova_scs_legadas_dos_ccs_que_gerencia()
    {
        var w = await BuildAsync();
        var (pr, _) = await CreatePrAsync(w, w.Junior, "PBA-001");
        await w.Prs.SubmitAsync(w.Junior, pr!.Id);
        await PutInLegacyApprovalAsync(w, pr);   // SC anterior à mudança de fluxo

        var pending = await w.Prs.PendingApprovalsAsync(w.Pleno);
        Assert.Contains(pending, r => r.Id == pr.Id);
        var pendingOutro = await w.Prs.PendingApprovalsAsync(w.OutroPleno);
        Assert.DoesNotContain(pendingOutro, r => r.Id == pr.Id); // gerencia só BAH-001

        var (_, blocked) = await w.Prs.ApproveAsync(w.OutroPleno, pr.Id, null);
        Assert.Equal("PR-ERR-002", blocked!.Code);

        var (approved, error) = await w.Prs.ApproveAsync(w.Pleno, pr.Id, null);
        Assert.Null(error);
        Assert.Equal(RequisitionStatus.Approved, approved!.Status);
    }

    // ---- cotação: alçada gerencial por CC e roteamento ao diretor -------------

    private static ProposalInput ProposalFor(Quotation q, decimal price) =>
        new(10, "28 dias", 0, null, null,
            q.Items.Select(i => new ProposalItemInput(i.Id, price, null)).ToList());

    private static async Task<Quotation> UpToAwaitingManagerAsync(World w)
    {
        var (pr, e0) = await CreatePrAsync(w, w.Junior, "PBA-001");
        Assert.Null(e0);
        await w.Prs.SubmitAsync(w.Junior, pr!.Id);   // enviada vai direto para cotação
        var (q, e1) = await w.Rfq.CreateFromPrAsync(w.Carla, pr.Id, QuotationKind.Purchase, null, null);
        Assert.Null(e1);
        await w.Rfq.InviteSuppliersAsync(w.Carla, q!.Id, [w.Alfa.Id]);
        await w.Rfq.SubmitProposalAsync(q.Id, w.Alfa.Id, ProposalFor(q, 20m), "PORTAL", "Alfa");
        await w.Rfq.CloseForAnalysisAsync(w.Carla, q.Id);
        var reloaded = (await w.Rfq.GetAsync(q.Id))!;
        var winner = reloaded.Proposals.First();
        var (_, e2) = await w.Rfq.SelectWinnerAsync(w.Carla, q.Id, winner.Id, "Preço", "Única proposta válida.");
        Assert.Null(e2);
        return (await w.Rfq.GetAsync(q.Id))!;
    }

    [Fact]
    public async Task Aprovacao_gerencial_do_processo_respeita_o_cc_gerenciado()
    {
        var w = await BuildAsync();
        var q = await UpToAwaitingManagerAsync(w);

        var (_, blocked) = await w.Rfq.ManagerDecisionAsync(w.OutroPleno, q.Id, "APROVAR", null);
        Assert.Equal("RFQ-ERR-031", blocked!.Code);

        var (ok, error) = await w.Rfq.ManagerDecisionAsync(w.Pleno, q.Id, "APROVAR", null);
        Assert.Null(error);
        Assert.Equal(QuotationStatus.AwaitingDirector, ok!.Status);
    }

    [Fact]
    public async Task Aprovacao_da_diretoria_respeita_o_diretor_vinculado_ao_gerente()
    {
        var w = await BuildAsync();
        var q = await UpToAwaitingManagerAsync(w);
        await w.Rfq.ManagerDecisionAsync(w.Pleno, q.Id, "APROVAR", null); // Pleno tem DirectorId = Diana

        var (_, blocked) = await w.Rfq.DirectorDecisionAsync(w.Otto, q.Id, "APROVAR", null);
        Assert.Equal("RFQ-ERR-032", blocked!.Code);

        var (ok, error) = await w.Rfq.DirectorDecisionAsync(w.Diana, q.Id, "APROVAR", null);
        Assert.Null(error);
        Assert.Equal(QuotationStatus.ApprovedForIssue, ok!.Status);
    }

    [Fact]
    public async Task Sc_enviada_entra_direto_na_fila_de_cotacao()
    {
        var w = await BuildAsync();
        var (pr, _) = await CreatePrAsync(w, w.Junior, "PBA-001");
        await w.Prs.SubmitAsync(w.Junior, pr!.Id);

        var (ready, blocked) = await w.Rfq.QueueAsync();
        Assert.Contains(ready, e => e.Pr.Id == pr.Id);   // sem autorização prévia: já dá para cotar
        Assert.Empty(blocked);

        var (q, error) = await w.Rfq.CreateFromPrAsync(w.Carla, pr.Id, QuotationKind.Purchase, null, null);
        Assert.Null(error);
        Assert.NotNull(q);
    }

    [Fact]
    public async Task Sc_legada_retida_na_aprovacao_aparece_na_fila_com_o_motivo()
    {
        var w = await BuildAsync();
        var (pr, _) = await CreatePrAsync(w, w.Junior, "PBA-001");
        await w.Prs.SubmitAsync(w.Junior, pr!.Id);
        await PutInLegacyApprovalAsync(w, pr);

        var (ready, blocked) = await w.Rfq.QueueAsync();
        Assert.DoesNotContain(ready, e => e.Pr.Id == pr.Id);
        var retida = Assert.Single(blocked, b => b.Pr.Id == pr.Id);
        Assert.Contains(w.Pleno.Label, retida.Reason);

        await w.Prs.ApproveAsync(w.Pleno, pr.Id, null);
        (ready, blocked) = await w.Rfq.QueueAsync();
        Assert.Contains(ready, e => e.Pr.Id == pr.Id);
        Assert.DoesNotContain(blocked, b => b.Pr.Id == pr.Id);
    }

    [Fact]
    public async Task Compra_aprovada_pela_diretoria_marca_a_sc_de_origem_como_aprovada()
    {
        var w = await BuildAsync();
        var q = await UpToAwaitingManagerAsync(w);
        await w.Rfq.ManagerDecisionAsync(w.Pleno, q.Id, "APROVAR", null);

        var antes = await w.Db.Requisitions.SingleAsync(r => r.Id == q.SourcePrId);
        Assert.Equal(RequisitionStatus.Submitted, antes.Status);   // ainda em processo

        await w.Rfq.DirectorDecisionAsync(w.Diana, q.Id, "APROVAR", null);
        var depois = await w.Db.Requisitions.SingleAsync(r => r.Id == q.SourcePrId);
        Assert.Equal(RequisitionStatus.Approved, depois.Status);   // compra autorizada, com preço
        Assert.Contains(q.Number, depois.DecisionReason!);
    }

    // ---- cadastro: usuários com vínculos e empresas (CNPJs) -------------------

    [Fact]
    public async Task Usuario_com_diretor_invalido_e_rejeitado()
    {
        var w = await BuildAsync();
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<User>();
        var users = new UserService(w.Db, hasher, TimeProvider.System);

        var (_, invalid) = await users.CreateAsync("novo@trino.dev", "Novo Gerente", Roles.Approver,
            "Wq5!chaveNova#z", null, ["PBA-001"], w.Junior.Id); // Júnior não é diretor
        Assert.Equal("IAM-ERR-019", invalid!.Code);

        var (user, error) = await users.CreateAsync("novo@trino.dev", "Novo Gerente", Roles.Approver,
            "Wq5!chaveNova#z", null, ["pba-001", "BAH-001"], w.Diana.Id);
        Assert.Null(error);
        Assert.Equal("PBA-001,BAH-001", user!.CostCenters);
        Assert.Equal(w.Diana.Id, user.DirectorId);
    }

    [Fact]
    public async Task Empresa_do_grupo_tem_cnpj_unico_e_vincula_no_cc()
    {
        var w = await BuildAsync();
        var (company, e1) = await w.Companies.CreateAsync("TRINO LOGISTICA INTEGRADA LTDA", "05.345.258/0001-43",
            null, "Av Agamenon Magalhaes 2936", "Espinheiro", "Recife", "pe", "52020-000", null, null);
        Assert.Null(e1);
        Assert.Equal("05345258000143", company!.TaxId);
        Assert.Equal("PE", company.State);

        var (_, dup) = await w.Companies.CreateAsync("Duplicada", "05345258000143",
            null, "Rua X", null, "Recife", "PE", "50000-000", null, null);
        Assert.Equal("EMP-ERR-012", dup!.Code);

        var (cc, e2) = await w.Ccs.CreateAsync(w.Gustavo.Id, null, "Novo Atacarejo PB", "PARAIBA",
            w.Pleno.Id, "Novo Atacarejo", company.Id);
        Assert.Null(e2);
        Assert.Equal(company.Id, cc!.CompanyId);

        var (_, badCompany) = await w.Ccs.CreateAsync(w.Gustavo.Id, null, "CC Órfão", "BAHIA",
            null, null, Guid.NewGuid());
        Assert.Equal("CC-ERR-014", badCompany!.Code);
    }

    // ---- continuidade do fluxo: nenhuma SC fica órfã de aprovador -------------

    [Fact]
    public async Task Sc_de_cc_sem_gerente_aparece_para_qualquer_aprovador()
    {
        var w = await BuildAsync();
        // CC novo, sem gerente vinculado
        w.Db.CostCenters.Add(new CostCenter { Code = "BAH-002", Name = "Contrato sem gerente", Region = "BAHIA", CreatedBy = w.Gustavo.Id });
        await w.Db.SaveChangesAsync();

        var livre = new Actor(Guid.NewGuid(), "Livre", Roles.Requester);
        w.Db.Users.Add(MakeUser(livre));
        await w.Db.SaveChangesAsync();

        var (pr, _) = await w.Prs.CreateAsync(livre, "Viagem de acompanhamento contratual", "BAH-002", "NORMAL", null,
            [new ItemInput("Passagem aérea", 1, "UN", 1200, null)]);
        await w.Prs.SubmitAsync(livre, pr!.Id);
        await PutInLegacyApprovalAsync(w, pr);

        // Thiago gerencia PBA-001, mas o CC órfão não pode sumir da fila dele
        var fila = await w.Prs.PendingApprovalsAsync(w.Pleno);
        Assert.Contains(fila, r => r.Id == pr.Id);

        var (approved, error) = await w.Prs.ApproveAsync(w.Pleno, pr.Id, null);
        Assert.Null(error);
        Assert.Equal(RequisitionStatus.Approved, approved!.Status);
    }

    [Fact]
    public async Task Pedido_informa_quem_precisa_aprovar_e_o_impedimento()
    {
        var w = await BuildAsync();
        w.Db.CostCenters.Add(new CostCenter { Code = "BAH-002", Name = "Contrato sem gerente", Region = "BAHIA", CreatedBy = w.Gustavo.Id });
        await w.Db.SaveChangesAsync();

        var (comGerente, _) = await CreatePrAsync(w, w.Junior, "PBA-001");
        var livre = new Actor(Guid.NewGuid(), "Livre", Roles.Requester);
        w.Db.Users.Add(MakeUser(livre));
        await w.Db.SaveChangesAsync();
        var (semGerente, _) = await w.Prs.CreateAsync(livre, "Viagem", "BAH-002", "NORMAL", null,
            [new ItemInput("Passagem aérea", 1, "UN", 1200, null)]);

        var hints = await w.Prs.ApproverHintsAsync([comGerente!, semGerente!]);
        var comHint = w.Prs.HintFor(hints, comGerente!);
        Assert.Equal(w.Pleno.Label, comHint.ApproverLabel);
        Assert.Null(comHint.Issue);

        var semHint = w.Prs.HintFor(hints, semGerente!);
        Assert.Null(semHint.ApproverLabel);
        Assert.Contains("gerente", semHint.Issue!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Edicao_de_empresa_atualiza_dados_mantendo_cnpj()
    {
        var w = await BuildAsync();
        var (company, _) = await w.Companies.CreateAsync("TRINO NORDESTE LTDA", "11222333000181",
            null, "Rua A", null, "Recife", "PE", "50000-000", null, null);
        var (updated, error) = await w.Companies.UpdateAsync(company!.Id,
            "TRINO NORDESTE S/A", "123456", "Rua B, 42", "Centro", "Olinda", "pe", "53000-000",
            "8133334444", "compras@trino.dev", null);
        Assert.Null(error);
        Assert.Equal("TRINO NORDESTE S/A", updated!.LegalName);
        Assert.Equal("11222333000181", updated.TaxId);   // CNPJ não muda na edição
        Assert.Equal("Olinda", updated.City);
        Assert.Equal(2, updated.Version);
    }
}
