using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// Os dois cadastros de pagamento — a <b>forma</b> (por onde sai) e a <b>condição</b>
/// (quando se paga). Ficam juntos porque respondem a mesma pergunta do comprador na hora
/// de registrar uma proposta, e separá-los em dois serviços só espalharia a mesma regra.
///
/// <para>
/// Nada aqui é apagado: um cadastro que já foi usado em proposta vira <c>Active = false</c>
/// e some das listas novas, mas continua legível no histórico. Apagar de verdade deixaria
/// a proposta antiga apontando para o nada.
/// </para>
/// </summary>
public class PagamentoService(AppDbContext db, TimeProvider clock)
{
    /// <summary>Manter os cadastros é do time de compras; ver, de quem registra proposta.</summary>
    public static bool CanMaintain(string role) =>
        role is Roles.PurchasingOfficer or Roles.SupplyManager or Roles.SystemAdministrator;

    private static string? Limpo(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // ---- formas de pagamento --------------------------------------------------

    public Task<List<PaymentMethod>> FormasAsync(bool incluirInativas, CancellationToken ct = default) =>
        db.PaymentMethods.Where(m => incluirInativas || m.Active)
            .OrderBy(m => m.Name).ToListAsync(ct);

    public async Task<(PaymentMethod? forma, UserError? error)> CriarFormaAsync(
        string? nome, CancellationToken ct = default)
    {
        var limpo = Limpo(nome);
        if (limpo is null || limpo.Length < 2)
            return (null, new("PAY-ERR-010", "Informe o nome da forma de pagamento (mín. 2 caracteres)."));
        if (await db.PaymentMethods.AnyAsync(m => m.Name.ToLower() == limpo.ToLower(), ct))
            return (null, new("PAY-ERR-011", "Já existe uma forma de pagamento com este nome."));

        var agora = clock.GetUtcNow();
        var forma = new PaymentMethod { Name = limpo, CreatedAt = agora, UpdatedAt = agora };
        db.PaymentMethods.Add(forma);
        await db.SaveChangesAsync(ct);
        return (forma, null);
    }

    public async Task<(PaymentMethod? forma, UserError? error)> AtualizarFormaAsync(
        Guid id, string? nome, bool? ativa, CancellationToken ct = default)
    {
        var forma = await db.PaymentMethods.SingleOrDefaultAsync(m => m.Id == id, ct);
        if (forma is null) return (null, new("PAY-ERR-404", "Forma de pagamento não encontrada."));

        if (Limpo(nome) is { } novo)
        {
            if (novo.Length < 2)
                return (null, new("PAY-ERR-010", "Informe o nome da forma de pagamento (mín. 2 caracteres)."));
            if (await db.PaymentMethods.AnyAsync(m => m.Id != id && m.Name.ToLower() == novo.ToLower(), ct))
                return (null, new("PAY-ERR-011", "Já existe uma forma de pagamento com este nome."));
            forma.Name = novo;
        }
        if (ativa is not null) forma.Active = ativa.Value;
        forma.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return (forma, null);
    }

    // ---- condições de pagamento -----------------------------------------------

    public Task<List<PaymentTermOption>> CondicoesAsync(bool incluirInativas, CancellationToken ct = default) =>
        db.PaymentTermOptions.Where(c => incluirInativas || c.Active)
            .OrderBy(c => c.Installments).ThenBy(c => c.Name).ToListAsync(ct);

    public async Task<(PaymentTermOption? condicao, UserError? error)> CriarCondicaoAsync(
        string? nome, int parcelas, int? primeiroVencimento, bool padrao, CancellationToken ct = default)
    {
        var limpo = Limpo(nome);
        if (limpo is null || limpo.Length < 2)
            return (null, new("PAY-ERR-020", "Informe o nome da condição de pagamento (mín. 2 caracteres)."));
        if (parcelas < 1)
            return (null, new("PAY-ERR-021", "A condição precisa de ao menos uma parcela."));
        if (primeiroVencimento is < 0)
            return (null, new("PAY-ERR-022", "O prazo da primeira parcela não pode ser negativo."));
        if (await db.PaymentTermOptions.AnyAsync(c => c.Name.ToLower() == limpo.ToLower(), ct))
            return (null, new("PAY-ERR-023", "Já existe uma condição de pagamento com este nome."));

        var agora = clock.GetUtcNow();
        var condicao = new PaymentTermOption
        {
            Name = limpo, Installments = parcelas, FirstDueDays = primeiroVencimento,
            CreatedAt = agora, UpdatedAt = agora,
        };
        db.PaymentTermOptions.Add(condicao);
        if (padrao) await TrocarPadraoAsync(condicao, ct);
        await db.SaveChangesAsync(ct);
        return (condicao, null);
    }

    public async Task<(PaymentTermOption? condicao, UserError? error)> AtualizarCondicaoAsync(
        Guid id, string? nome, int? parcelas, int? primeiroVencimento, bool? padrao, bool? ativa,
        CancellationToken ct = default)
    {
        var condicao = await db.PaymentTermOptions.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (condicao is null) return (null, new("PAY-ERR-404", "Condição de pagamento não encontrada."));

        if (Limpo(nome) is { } novo)
        {
            if (novo.Length < 2)
                return (null, new("PAY-ERR-020", "Informe o nome da condição de pagamento (mín. 2 caracteres)."));
            if (await db.PaymentTermOptions.AnyAsync(c => c.Id != id && c.Name.ToLower() == novo.ToLower(), ct))
                return (null, new("PAY-ERR-023", "Já existe uma condição de pagamento com este nome."));
            condicao.Name = novo;
        }
        if (parcelas is { } p)
        {
            if (p < 1) return (null, new("PAY-ERR-021", "A condição precisa de ao menos uma parcela."));
            condicao.Installments = p;
        }
        if (primeiroVencimento is { } d)
        {
            if (d < 0) return (null, new("PAY-ERR-022", "O prazo da primeira parcela não pode ser negativo."));
            condicao.FirstDueDays = d;
        }
        if (ativa is not null) condicao.Active = ativa.Value;
        // condição desligada não pode continuar sendo a sugerida: a tela ofereceria o que
        // ela mesma escondeu da lista
        if (padrao == true) await TrocarPadraoAsync(condicao, ct);
        else if (padrao == false || condicao.Active == false) condicao.IsDefault = false;

        condicao.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return (condicao, null);
    }

    /// <summary>Só uma condição é a padrão: marcar a nova desmarca a anterior.</summary>
    private async Task TrocarPadraoAsync(PaymentTermOption nova, CancellationToken ct)
    {
        foreach (var outra in await db.PaymentTermOptions.Where(c => c.IsDefault).ToListAsync(ct))
            outra.IsDefault = false;
        nova.IsDefault = true;
    }
}
