using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// Auditoria A6, A7 e A8: texto longo demais responde 400 com o campo, a renovação concorrente
/// do mesmo token não abre duas sessões, e o healthcheck anônimo não diz a versão no ar.
/// </summary>
public class ResilienciaTests
{
    private sealed class Numeros : IPrNumberGenerator
    {
        private int _n;
        public Task<string> NextAsync(CancellationToken ct = default) => Task.FromResult($"PR-2026-{++_n:000000}");
    }

    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana Solicitante", Roles.Requester);

    private static DbContextOptions<AppDbContext> Opcoes(string? nome = null) =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(nome ?? Guid.NewGuid().ToString()).Options;

    private static RequisitionService Sc(AppDbContext db) =>
        new(db, new Numeros(), new CatalogService(db, TimeProvider.System), TimeProvider.System);

    [Fact]
    public async Task Justificativa_acima_do_limite_e_recusada_antes_de_gravar_com_o_nome_do_campo()
    {
        using var db = new AppDbContext(Opcoes());
        var (pr, erro) = await Sc(db).CreateAsync(Ana, new string('x', 2001), "CC-01", "NORMAL", null,
            [new ItemInput("Caneta azul", 10, "UN", 2, null)]);
        Assert.Null(pr);
        Assert.Equal("PR-ERR-031", erro!.Code);
        Assert.Contains("justificativa", erro.Message);
        Assert.Contains("2.000", erro.Message.Replace(",", "."));
        Assert.Empty(db.Requisitions);

        // no limite exato, passa
        var (ok, erroOk) = await Sc(db).CreateAsync(Ana, new string('x', 2000), "CC-01", "NORMAL", null,
            [new ItemInput("Caneta azul", 10, "UN", 2, null)]);
        Assert.Null(erroOk);
        Assert.NotNull(ok);
    }

    [Fact]
    public async Task Descricao_de_item_longa_demais_tambem_e_dita_pelo_nome()
    {
        using var db = new AppDbContext(Opcoes());
        var (_, erro) = await Sc(db).CreateAsync(Ana, "Material do escritório", "CC-01", "NORMAL", null,
            [new ItemInput(new string('y', 501), 1, "UN", 2, null)]);
        Assert.Equal("PR-ERR-031", erro!.Code);
        Assert.Contains("descrição do item", erro.Message);
    }

    [Fact]
    public async Task O_SaveChanges_barra_o_texto_que_o_servico_nao_conferiu()
    {
        // a rede para os serviços que ainda não conferem o próprio texto: 400 com o campo,
        // e não o 500 que o Postgres devolveria
        using var db = new AppDbContext(Opcoes());
        db.CostCenters.Add(new CostCenter { Code = "CC-01", Name = new string('z', 5000) });
        var falha = await Assert.ThrowsAsync<TextoAcimaDoLimiteException>(() => db.SaveChangesAsync());
        Assert.Contains("Name", falha.Message);
    }

    [Fact]
    public async Task Duas_renovacoes_com_o_mesmo_token_nao_abrem_duas_sessoes()
    {
        var nome = Guid.NewGuid().ToString();
        var token = new RefreshToken
        {
            UserId = Guid.NewGuid(), TokenHash = new string('a', 64),
            CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        };
        using (var db = new AppDbContext(Opcoes(nome))) { db.RefreshTokens.Add(token); await db.SaveChangesAsync(); }

        // as duas leem "não revogado" antes de qualquer uma gravar
        using var primeira = new AppDbContext(Opcoes(nome));
        using var segunda = new AppDbContext(Opcoes(nome));
        var a = await primeira.RefreshTokens.SingleAsync();
        var b = await segunda.RefreshTokens.SingleAsync();
        a.RevokedAt = DateTimeOffset.UtcNow;
        await primeira.SaveChangesAsync();

        b.RevokedAt = DateTimeOffset.UtcNow.AddSeconds(1);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => segunda.SaveChangesAsync());
    }

    [Fact]
    public void O_healthcheck_anonimo_nao_diz_a_versao_no_ar()
    {
        var agora = DateTimeOffset.UtcNow;
        var anonimo = JsonSerializer.SerializeToElement(Saude.Corpo(true, false, true, "abc1234def", agora, agora));
        Assert.Equal("healthy", anonimo.GetProperty("status").GetString());
        Assert.False(anonimo.TryGetProperty("commit", out _));
        Assert.False(anonimo.TryGetProperty("startedAt", out _));

        var comSessao = JsonSerializer.SerializeToElement(Saude.Corpo(true, true, true, "abc1234def", agora, agora));
        Assert.Equal("abc1234def", comSessao.GetProperty("commit").GetString());
    }
}
