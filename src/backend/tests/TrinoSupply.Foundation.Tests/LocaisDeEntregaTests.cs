using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Inventory;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// Local de entrega: o almoxarifado não é o único destino possível. Um centro de custo
/// marcado como quem recebe material entra na mesma lista, porque a SC é do centro que
/// paga e a entrega vai para o endereço do cliente.
/// </summary>
public class LocaisDeEntregaTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Agora = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static (AppDbContext db, CostCenterService svc) Build()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new AppDbContext(options);
        return (db, new CostCenterService(db, new FixedTimeProvider(Agora)));
    }

    private static void Almoxarifado(AppDbContext db, string code, string name, bool active = true) =>
        db.StorageLocations.Add(new StorageLocation
        {
            Code = code, Name = name, Active = active, CreatedAt = Agora, CreatedBy = Guid.NewGuid(),
        });

    [Fact]
    public async Task Somente_o_centro_marcado_entra_na_lista_de_locais()
    {
        var (db, svc) = Build();
        Almoxarifado(db, "ALM-01", "Almoxarifado Sede");
        await db.SaveChangesAsync();

        var ator = Guid.NewGuid();
        var (recebe, e1) = await svc.CreateAsync(ator, "CC-PB-002", "Whirlpool PB", "PARAIBA",
            null, null, receivesMaterial: true);
        var (naoRecebe, e2) = await svc.CreateAsync(ator, "CC-PB-001", "Novo Atacarejo PB", "PARAIBA",
            null, null);
        Assert.Null(e1);
        Assert.Null(e2);
        Assert.True(recebe!.ReceivesMaterial);
        Assert.False(naoRecebe!.ReceivesMaterial);   // o padrão é não receber

        var locais = await LocaisDeEntrega.ListarAsync(db);

        // o almoxarifado vem primeiro; o centro que não recebe não aparece
        Assert.Equal(["ALM-01", "CC-PB-002"], locais.Select(l => l.Code));
        Assert.Equal(TiposDeLocal.Almoxarifado, locais[0].Kind);
        Assert.Equal(TiposDeLocal.CentroDeCusto, locais[1].Kind);
        Assert.Equal("Whirlpool PB", locais[1].Name);
    }

    [Fact]
    public async Task Centro_inativo_e_almoxarifado_inativo_saem_da_lista()
    {
        var (db, svc) = Build();
        Almoxarifado(db, "ALM-01", "Almoxarifado Sede");
        Almoxarifado(db, "ALM-09", "Depósito desativado", active: false);
        await db.SaveChangesAsync();

        var (cc, _) = await svc.CreateAsync(Guid.NewGuid(), "CC-PB-002", "Whirlpool PB", null,
            null, null, receivesMaterial: true);
        await svc.UpdateAsync(cc!.Id, null, null, null, null, active: false);

        var locais = await LocaisDeEntrega.ListarAsync(db);

        Assert.Equal(["ALM-01"], locais.Select(l => l.Code));
    }

    [Fact]
    public async Task A_marca_do_centro_liga_e_desliga_pela_edicao()
    {
        var (db, svc) = Build();
        var (cc, _) = await svc.CreateAsync(Guid.NewGuid(), "CC-PB-001", "Novo Atacarejo PB", null, null, null);

        // null mantém a marca como está: editar o nome não pode mudar quem recebe material
        await svc.UpdateAsync(cc!.Id, "Novo Atacarejo PB II", null, null, null, null);
        Assert.Empty(await LocaisDeEntrega.ListarAsync(db));

        await svc.UpdateAsync(cc.Id, null, null, null, null, null, receivesMaterial: true);
        Assert.Equal(["CC-PB-001"], (await LocaisDeEntrega.ListarAsync(db)).Select(l => l.Code));

        await svc.UpdateAsync(cc.Id, null, null, null, null, null, receivesMaterial: false);
        Assert.Empty(await LocaisDeEntrega.ListarAsync(db));
    }
}
