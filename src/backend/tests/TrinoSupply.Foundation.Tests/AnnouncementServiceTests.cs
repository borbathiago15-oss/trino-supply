using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Comunicados;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// Comunicados do administrador. O que estes testes protegem é o que faz um recado
/// atrapalhar em vez de informar: aparecer fora da vigência, e voltar depois de a
/// pessoa já ter lido e fechado.
/// </summary>
public class AnnouncementServiceTests
{
    private sealed class RelogioAjustavel(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Agora;
    }

    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Bruno = Guid.NewGuid();

    private static (AppDbContext Db, AnnouncementService Svc, RelogioAjustavel Relogio) Build()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var relogio = new RelogioAjustavel(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        return (db, new AnnouncementService(db, relogio), relogio);
    }

    private static AnnouncementInput Comunicado(string titulo = "Parada do sistema no sábado",
        string? corpo = "O sistema fica fora do ar das 8h às 12h.",
        int deDia = 8, int ateDia = 12, bool ativo = true) =>
        new(titulo, corpo, new DateOnly(2026, 9, deDia), new DateOnly(2026, 9, ateDia), ativo);

    [Fact]
    public async Task Comunicado_aparece_dentro_da_vigencia_e_some_nas_duas_pontas()
    {
        // as duas pontas são inclusivas: quem escreve "de 8 até 12" espera ver no dia 8 e no dia 12
        var (_, svc, relogio) = Build();
        await svc.CreateAsync(Admin, "Administrador", Comunicado());

        async Task<int> QuantosEm(int dia)
        {
            relogio.Agora = new DateTimeOffset(2026, 9, dia, 9, 0, 0, TimeSpan.Zero);
            return (await svc.CurrentForAsync(Ana)).Count;
        }

        Assert.Equal(0, await QuantosEm(7));    // véspera
        Assert.Equal(1, await QuantosEm(8));    // primeiro dia
        Assert.Equal(1, await QuantosEm(10));   // meio
        Assert.Equal(1, await QuantosEm(12));   // último dia
        Assert.Equal(0, await QuantosEm(13));   // dia seguinte
    }

    [Fact]
    public async Task Fechar_vale_para_quem_fechou_e_nao_para_os_outros()
    {
        var (_, svc, _) = Build();
        var (com, _) = await svc.CreateAsync(Admin, "Administrador", Comunicado());

        Assert.Single(await svc.CurrentForAsync(Ana));
        Assert.Null(await svc.DismissAsync(com!.Id, Ana));

        Assert.Empty(await svc.CurrentForAsync(Ana));      // quem leu não vê de novo
        Assert.Single(await svc.CurrentForAsync(Bruno));   // quem não leu continua vendo
    }

    [Fact]
    public async Task Fechar_duas_vezes_nao_duplica_o_registro_de_leitura()
    {
        // a pessoa pode ter duas abas abertas; a segunda gravação bateria no índice único
        var (db, svc, _) = Build();
        var (com, _) = await svc.CreateAsync(Admin, "Administrador", Comunicado());

        Assert.Null(await svc.DismissAsync(com!.Id, Ana));
        Assert.Null(await svc.DismissAsync(com.Id, Ana));

        Assert.Equal(1, await db.AnnouncementDismissals.CountAsync(d => d.AnnouncementId == com.Id));
        Assert.Single((await svc.GetAsync(com.Id))!.Dismissals);
    }

    [Fact]
    public async Task Desligar_tira_do_ar_sem_apagar_quem_ja_leu()
    {
        // encerrar um comunicado não pode custar o histórico de leitura
        var (_, svc, _) = Build();
        var (com, _) = await svc.CreateAsync(Admin, "Administrador", Comunicado());
        await svc.DismissAsync(com!.Id, Ana);

        var (desligado, erro) = await svc.UpdateAsync(com.Id, new(null, null, null, null, false));
        Assert.Null(erro);
        Assert.False(desligado!.Active);
        Assert.Empty(await svc.CurrentForAsync(Bruno));
        Assert.Single((await svc.GetAsync(com.Id))!.Dismissals);
    }

    [Fact]
    public async Task Vigencia_invertida_e_titulo_curto_sao_recusados()
    {
        var (_, svc, _) = Build();

        var (_, semTitulo) = await svc.CreateAsync(Admin, "Administrador", Comunicado(titulo: "ab"));
        Assert.Equal("COM-ERR-010", semTitulo!.Code);

        var (_, invertida) = await svc.CreateAsync(Admin, "Administrador", Comunicado(deDia: 20, ateDia: 10));
        Assert.Equal("COM-ERR-012", invertida!.Code);

        var (_, semVigencia) = await svc.CreateAsync(Admin, "Administrador",
            new("Aviso", null, null, null, true));
        Assert.Equal("COM-ERR-011", semVigencia!.Code);
    }

    [Fact]
    public async Task Comunicado_pode_ser_so_a_imagem_sem_texto()
    {
        // o administrador manda um cartaz; obrigar texto seria obrigá-lo a repetir o que a imagem diz
        var (_, svc, _) = Build();
        var (com, erro) = await svc.CreateAsync(Admin, "Administrador", Comunicado(corpo: null));
        Assert.Null(erro);
        Assert.Null(com!.Body);

        var doc = Guid.NewGuid();
        var (comImagem, erroImagem) = await svc.SetImageAsync(com.Id, doc, "cartaz.png");
        Assert.Null(erroImagem);
        Assert.Equal(doc, comImagem!.ImageDocumentId);
        Assert.Equal("cartaz.png", comImagem.ImageFileName);
        Assert.Single(await svc.CurrentForAsync(Ana));
    }

    [Fact]
    public void Escrever_comunicado_e_do_administrador_so()
    {
        Assert.True(AnnouncementService.CanManage(Roles.SystemAdministrator));
        foreach (var papel in new[] { Roles.SupplyManager, Roles.Director, Roles.PurchasingOfficer,
                                      Roles.Approver, Roles.Requester, Roles.Auditor })
            Assert.False(AnnouncementService.CanManage(papel));
    }
}
