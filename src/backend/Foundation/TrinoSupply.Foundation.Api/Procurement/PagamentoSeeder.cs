using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// Formas e condições que o Grupo Trino já usa no dia a dia. Entram uma vez, para o
/// cadastro não nascer vazio e obrigar alguém a redigitar o que já é sabido.
///
/// <para>
/// Cada lista é semeada só quando está <b>vazia</b>, e nunca de novo: quem apagar,
/// renomear ou desativar um item está decidindo, e uma reinicialização não pode
/// desfazer essa decisão trazendo tudo de volta.
/// </para>
/// </summary>
public static class PagamentoSeeder
{
    private static readonly string[] Formas =
        ["Boleto Bancário", "Cartão de Crédito", "Depósito Bancário", "Dinheiro", "Pix"];

    // (nome, parcelas, dias até a primeira) — o primeiro número do nome é o prazo
    private static readonly (string Nome, int Parcelas, int PrimeiroVencimento)[] Condicoes =
    [
        ("À Vista", 1, 0),
        ("Parcelado 14/28", 2, 14),
        ("Parcelado 15/30/45/60/75/90", 6, 15),
        ("Parcelado 30/60/90", 3, 30),
    ];

    public static async Task SeedAsync(AppDbContext db, TimeProvider clock, CancellationToken ct = default)
    {
        var agora = clock.GetUtcNow();
        var mudou = false;

        if (!await db.PaymentMethods.AnyAsync(ct))
        {
            db.PaymentMethods.AddRange(Formas.Select(n => new PaymentMethod
            {
                Name = n, CreatedAt = agora, UpdatedAt = agora,
            }));
            mudou = true;
        }

        if (!await db.PaymentTermOptions.AnyAsync(ct))
        {
            db.PaymentTermOptions.AddRange(Condicoes.Select(c => new PaymentTermOption
            {
                Name = c.Nome, Installments = c.Parcelas, FirstDueDays = c.PrimeiroVencimento,
                CreatedAt = agora, UpdatedAt = agora,
            }));
            mudou = true;
        }

        // nenhuma condição nasce marcada como padrão de propósito: sugerir uma que
        // ninguém escolheu faria toda proposta sair com ela por omissão
        if (mudou) await db.SaveChangesAsync(ct);
    }
}
