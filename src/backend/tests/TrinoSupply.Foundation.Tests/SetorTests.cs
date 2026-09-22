using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O cadastro de setores — a dimensão "quem trabalha", ao lado do centro de custo, que é
/// "onde o dinheiro cai". O que se testa aqui é o que o módulo de melhoria vai supor pronto:
/// código único e estável, e setor inativo que não volta pela porta do vínculo.
/// </summary>
public class SetorTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Agora = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.NewGuid();

    private static AppDbContext Banco() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SectorService Servico(AppDbContext db) => new(db, new FixedTimeProvider(Agora));

    [Fact]
    public async Task Codigo_vazio_sai_do_nome_sem_acento_e_sem_espaco()
    {
        var db = Banco();
        var (setor, erro) = await Servico(db).CreateAsync(Admin, null, "Manutenção Predial");
        Assert.Null(erro);
        Assert.Equal("MANUTE", setor!.Code);
    }

    [Fact]
    public async Task Codigo_repetido_e_recusado()
    {
        var db = Banco();
        var svc = Servico(db);
        await svc.CreateAsync(Admin, "RH", "Recursos Humanos");
        var (setor, erro) = await svc.CreateAsync(Admin, "rh", "RH da Filial");
        Assert.Null(setor);
        Assert.Equal("SET-ERR-012", erro!.Code);
    }

    [Fact]
    public async Task Nome_curto_demais_e_recusado()
    {
        var db = Banco();
        var (setor, erro) = await Servico(db).CreateAsync(Admin, "TI", " ");
        Assert.Null(setor);
        Assert.Equal("SET-ERR-010", erro!.Code);
    }

    [Fact]
    public async Task O_codigo_e_identidade_e_a_edicao_nao_o_troca()
    {
        // pessoas e ciclos já o carregam: trocá-lo faria o histórico apontar para outro setor
        var db = Banco();
        var svc = Servico(db);
        var (setor, _) = await svc.CreateAsync(Admin, "TI", "Tecnologia");
        var (atualizado, erro) = await svc.UpdateAsync(setor!.Id, "Tecnologia da Informação", null);
        Assert.Null(erro);
        Assert.Equal("TI", atualizado!.Code);
        Assert.Equal("Tecnologia da Informação", atualizado.Name);
    }

    [Fact]
    public async Task Setor_fora_de_uso_se_inativa_e_some_da_lista_padrao()
    {
        var db = Banco();
        var svc = Servico(db);
        var (setor, _) = await svc.CreateAsync(Admin, "RH", "Recursos Humanos");
        await svc.CreateAsync(Admin, "TI", "Tecnologia");
        await svc.UpdateAsync(setor!.Id, null, false);

        Assert.Equal(["TI"], (await svc.ListAsync(false)).Select(s => s.Code));
        // mas continua no cadastro: quem o cita no histórico ainda o encontra
        Assert.Equal(["RH", "TI"], (await svc.ListAsync(true)).Select(s => s.Code));
    }

    [Fact]
    public async Task Somente_quem_mantem_dimensao_organizacional_mantem_setor()
    {
        Assert.True(SectorService.CanMaintain(Roles.SupplyManager));
        Assert.True(SectorService.CanMaintain(Roles.SystemAdministrator));
        Assert.False(SectorService.CanMaintain(Roles.PurchasingOfficer));
        Assert.False(SectorService.CanMaintain(Roles.Requester));
        await Task.CompletedTask;
    }

    // ---- o vínculo do usuário ------------------------------------------------

    private static UserService Usuarios(AppDbContext db) =>
        new(db, new Microsoft.AspNetCore.Identity.PasswordHasher<User>(), new FixedTimeProvider(Agora));

    [Fact]
    public async Task Usuario_recebe_o_setor_em_que_trabalha()
    {
        var db = Banco();
        var (setor, _) = await Servico(db).CreateAsync(Admin, "RH", "Recursos Humanos");
        var (user, erro) = await Usuarios(db).CreateAsync(
            "ana@trino.com.br", "Ana Analista", Roles.Requester, "Bandeira#Azul47",
            sectorId: setor!.Id);
        Assert.Null(erro);
        Assert.Equal(setor.Id, user!.SectorId);
    }

    [Fact]
    public async Task Setor_inativo_nao_se_vincula()
    {
        // inativar é a forma de tirar um setor de uso; deixá-lo entrar por aqui desfaria
        // a decisão do cadastro sem ninguém perceber
        var db = Banco();
        var svc = Servico(db);
        var (setor, _) = await svc.CreateAsync(Admin, "RH", "Recursos Humanos");
        await svc.UpdateAsync(setor!.Id, null, false);

        var (user, erro) = await Usuarios(db).CreateAsync(
            "ana@trino.com.br", "Ana Analista", Roles.Requester, "Bandeira#Azul47",
            sectorId: setor.Id);
        Assert.Null(user);
        Assert.Equal("IAM-ERR-023", erro!.Code);
    }

    [Fact]
    public async Task Setor_inexistente_nao_se_vincula()
    {
        var db = Banco();
        var (user, erro) = await Usuarios(db).CreateAsync(
            "ana@trino.com.br", "Ana Analista", Roles.Requester, "Bandeira#Azul47",
            sectorId: Guid.NewGuid());
        Assert.Null(user);
        Assert.Equal("IAM-ERR-023", erro!.Code);
    }

    [Fact]
    public async Task O_vinculo_se_desfaz_com_clearSector()
    {
        var db = Banco();
        var (setor, _) = await Servico(db).CreateAsync(Admin, "RH", "Recursos Humanos");
        var usuarios = Usuarios(db);
        var (user, _) = await usuarios.CreateAsync(
            "ana@trino.com.br", "Ana Analista", Roles.Requester, "Bandeira#Azul47",
            sectorId: setor!.Id);

        var (semSetor, erro) = await usuarios.UpdateAsync(
            user!.Id, Admin, null, null, null, clearSector: true);
        Assert.Null(erro);
        Assert.Null(semSetor!.SectorId);
    }
}
