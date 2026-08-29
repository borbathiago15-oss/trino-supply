using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Materials;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// Triagem de demandas: toda SC aprovada e toda solicitação de material vira um ticket
/// no painel de Suprimentos, que designa o responsável pela continuidade.
/// </summary>
public class TriageServiceTests
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

    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana Solicitante", Roles.Requester);
    private static readonly Actor Bruno = new(Guid.NewGuid(), "Bruno Aprovador", Roles.Approver);
    private static readonly Actor Gustavo = new(Guid.NewGuid(), "Gustavo Gestor", Roles.SupplyManager);
    private static readonly Actor Carla = new(Guid.NewGuid(), "Carla Compradora", Roles.PurchasingOfficer);
    private static readonly Actor Wal = new(Guid.NewGuid(), "Wal Almoxarife", Roles.WarehouseOperator);

    private sealed record World(AppDbContext Db, TriageService Triage, RequisitionService Prs, PurchaseRequisition Pr);

    private static User MakeUser(Actor a) => new()
    {
        Id = a.Id, Email = $"{a.Id:N}@trino.dev", Name = a.Label, Role = a.Role, PasswordHash = "x",
    };

    private static async Task<World> BuildAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new AppDbContext(options);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        db.Users.AddRange(MakeUser(Ana), MakeUser(Bruno), MakeUser(Gustavo), MakeUser(Carla), MakeUser(Wal));
        await db.SaveChangesAsync();

        var prs = new RequisitionService(db, new FakeNumbers(), new CatalogService(db, clock), clock);
        var (pr, _) = await prs.CreateAsync(Ana, "Materiais de manutenção", "CC-01", "NORMAL", null,
            [new ItemInput("Martelete", 1, "UN", 900, null)]);
        await prs.SubmitAsync(Ana, pr!.Id);
        await prs.ApproveAsync(Bruno, pr.Id, null);
        return new World(db, new TriageService(db, clock), prs, pr);
    }

    private static async Task<MaterialRequisition> AddMaterialAsync(World w)
    {
        var mr = new MaterialRequisition
        {
            Number = "MR-2026-000001", CostCenter = "CC-01", RequesterId = Ana.Id, RequesterLabel = Ana.Label,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        w.Db.MaterialRequisitions.Add(mr);
        var item = new MaterialRequisitionItem
        {
            RequisitionId = mr.Id, CatalogItemId = Guid.NewGuid(), CatalogCode = "LMP-001",
            Description = "Detergente neutro", Quantity = 5, CreatedAt = DateTimeOffset.UtcNow,
        };
        w.Db.MaterialRequisitionItems.Add(item);
        mr.Items.Add(item);
        await w.Db.SaveChangesAsync();
        return mr;
    }

    [Fact]
    public async Task Painel_lista_sc_aprovada_e_solicitacao_de_material()
    {
        var w = await BuildAsync();
        await AddMaterialAsync(w);
        var tickets = await w.Triage.ListAsync(Gustavo, "TODAS");
        Assert.Equal(2, tickets.Count);
        Assert.Contains(tickets, t => t.Kind == "SC" && t.Number == w.Pr.Number);
        Assert.Contains(tickets, t => t.Kind == "MATERIAL");
        Assert.All(tickets, t => Assert.Null(t.AssignedToId));
    }

    [Fact]
    public async Task Designa_comprador_e_a_demanda_aparece_na_fila_dele()
    {
        var w = await BuildAsync();
        var (ticket, error) = await w.Triage.AssignAsync(Gustavo, "SC", w.Pr.Id, Carla.Id);
        Assert.Null(error);
        Assert.Equal(Carla.Id, ticket!.AssignedToId);
        Assert.Equal(Carla.Label, ticket.AssignedToLabel);
        Assert.Equal(Gustavo.Label, ticket.AssignedByLabel);

        var minhas = await w.Triage.ListAsync(Carla, "MINHAS");
        Assert.Single(minhas);
        var naoAtribuidas = await w.Triage.ListAsync(Gustavo, "NAO_ATRIBUIDAS");
        Assert.DoesNotContain(naoAtribuidas, t => t.Id == w.Pr.Id);
    }

    [Fact]
    public async Task Designa_almoxarife_para_solicitacao_de_material_e_remove_depois()
    {
        var w = await BuildAsync();
        var mr = await AddMaterialAsync(w);
        var (ticket, error) = await w.Triage.AssignAsync(Gustavo, "MATERIAL", mr.Id, Wal.Id);
        Assert.Null(error);
        Assert.Equal(Wal.Id, ticket!.AssignedToId);

        var (cleared, e2) = await w.Triage.AssignAsync(Gustavo, "MATERIAL", mr.Id, null);
        Assert.Null(e2);
        Assert.Null(cleared!.AssignedToId);
        Assert.Null(cleared.AssignedByLabel);
    }

    [Fact]
    public async Task Solicitante_nao_distribui_demandas()
    {
        var w = await BuildAsync();
        var (_, error) = await w.Triage.AssignAsync(Ana, "SC", w.Pr.Id, Carla.Id);
        Assert.Equal("TRI-ERR-900", error!.Code);
    }

    [Fact]
    public async Task Responsavel_precisa_ser_comprador_gestor_ou_almoxarife()
    {
        var w = await BuildAsync();
        var (_, error) = await w.Triage.AssignAsync(Gustavo, "SC", w.Pr.Id, Ana.Id);
        Assert.Equal("TRI-ERR-010", error!.Code);
    }

    [Fact]
    public async Task Sc_nao_aprovada_fica_fora_da_triagem()
    {
        var w = await BuildAsync();
        var (draft, _) = await w.Prs.CreateAsync(Ana, "Rascunho", "CC-01", "NORMAL", null,
            [new ItemInput("Item", 1, "UN", 10, null)]);
        var (_, error) = await w.Triage.AssignAsync(Gustavo, "SC", draft!.Id, Carla.Id);
        Assert.Equal("TRI-ERR-020", error!.Code);

        var tickets = await w.Triage.ListAsync(Gustavo, "TODAS");
        Assert.DoesNotContain(tickets, t => t.Id == draft.Id);
    }

    [Fact]
    public async Task Lista_de_responsaveis_traz_so_papeis_operacionais()
    {
        var w = await BuildAsync();
        var responsibles = await w.Triage.ResponsiblesAsync();
        Assert.Contains(responsibles, r => r.Id == Carla.Id);
        Assert.Contains(responsibles, r => r.Id == Wal.Id);
        Assert.Contains(responsibles, r => r.Id == Gustavo.Id);
        Assert.DoesNotContain(responsibles, r => r.Id == Ana.Id);
        Assert.DoesNotContain(responsibles, r => r.Id == Bruno.Id);
    }

    // ==== alteração de prioridade com justificativa (V2-P3) =====================

    [Fact]
    public async Task Alterar_prioridade_exige_justificativa_e_urgente_exige_impacto()
    {
        var w = await BuildAsync();

        var (_, semMotivo) = await w.Triage.ChangePriorityAsync(Carla, w.Pr.Id, "URGENT", null, null);
        Assert.Equal("TRI-ERR-031", semMotivo!.Code);

        var (_, semImpacto) = await w.Triage.ChangePriorityAsync(Carla, w.Pr.Id, "URGENT", "Máquina parou", null);
        Assert.Equal("TRI-ERR-031", semImpacto!.Code);

        var (pr, error) = await w.Triage.ChangePriorityAsync(Carla, w.Pr.Id, "URGENT",
            "Máquina parou", "Linha inteira sem produzir");
        Assert.Null(error);
        Assert.Equal("URGENT", pr!.Priority);
        Assert.Equal("Máquina parou", pr.UrgencyReason);
        Assert.Equal("Linha inteira sem produzir", pr.UrgencyImpact);
        Assert.Equal(Carla.Label, pr.PriorityChangedByLabel);
        Assert.Equal("Máquina parou", pr.PriorityChangeReason);

        // voltar a NORMAL limpa a urgência e registra o novo motivo
        var (normal, e2) = await w.Triage.ChangePriorityAsync(Gustavo, w.Pr.Id, "NORMAL",
            "Fornecedor local resolveu o pico", null);
        Assert.Null(e2);
        Assert.Equal("NORMAL", normal!.Priority);
        Assert.Null(normal.UrgencyReason);
        Assert.Null(normal.UrgencyImpact);
        Assert.Equal(Gustavo.Label, normal.PriorityChangedByLabel);
    }

    [Fact]
    public async Task Solicitante_nao_altera_prioridade()
    {
        var w = await BuildAsync();
        var (_, error) = await w.Triage.ChangePriorityAsync(Ana, w.Pr.Id, "URGENT", "quero logo", "atraso");
        Assert.Equal("TRI-ERR-900", error!.Code);
    }
}
