using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>Como este processo resolve a segunda alçada.</summary>
public enum CaminhoDoNivel2
{
    /// <summary>A régua de sempre: a lista do Nível 2 do centro e, sem lista, o diretor vinculado.</summary>
    Padrao,

    /// <summary>Não há Nível 2: a compra é do próprio Gestor de Suprimentos, que já deu o Nível 1.</summary>
    Dispensado,

    /// <summary>O Nível 2 é o Gestor de Suprimentos responsável pelo comprador que deu o Nível 1.</summary>
    GestorResponsavel,
}

/// <param name="GestorId">Só em <see cref="CaminhoDoNivel2.GestorResponsavel"/>.</param>
public record RotaDoNivel2(CaminhoDoNivel2 Caminho, Guid? GestorId = null, string? GestorNome = null)
{
    public bool SemNivel2 => Caminho == CaminhoDoNivel2.Dispensado;
}

/// <summary>
/// A segunda alçada da <b>compra que a própria área de compras pediu</b> (decisão da empresa,
/// 2026-09). Antes tudo terminava na diretoria; agora a hierarquia de compras resolve o que é dela:
///
/// <list type="bullet">
/// <item>compra do <b>Gestor de Suprimentos</b>, que já deu o Nível 1 — <b>sem Nível 2</b>: o
/// processo é aprovado na mesma decisão e o pedido nasce ali, pronto para a O.C.;</item>
/// <item>compra de um <b>comprador</b>, que já deu o Nível 1 — o Nível 2 é o <b>gestor responsável
/// por ele</b> (<see cref="User.SupplyManagerId"/>), e não a diretoria;</item>
/// <item>qualquer outro processo — a régua de sempre, intocada.</item>
/// </list>
///
/// <para>
/// <b>"Compra própria" é exigida nos dois casos</b>, e é o que impede o atalho de virar porta
/// dos fundos: vale só quando <b>todas</b> as SCs de origem são de quem deu o Nível 1. Bastasse
/// uma, juntar a SC de um solicitante qualquer à do gestor no mesmo processo apagaria a segunda
/// assinatura da compra alheia junto com a dele.
/// </para>
///
/// <para>
/// <b>Sem responsável cadastrado, cai no padrão.</b> Comprador sem gestor vinculado volta para o
/// Nível 2 do centro — que é um crivo mais alto, não menor. Travar a fila seria pior, e afrouxá-la
/// em silêncio seria inaceitável.
/// </para>
/// </summary>
public static class AlcadaDoComprador
{
    /// <param name="autorDoNivel1">
    /// Quem deu (ou está dando) a primeira alçada. Nulo antes de o Nível 1 acontecer, e aí não
    /// há o que rotear: o caminho só se decide quando se sabe de quem é a compra.
    /// </param>
    public static async Task<RotaDoNivel2> RotaAsync(
        AppDbContext db, Quotation q, Guid? autorDoNivel1, CancellationToken ct = default)
    {
        if (autorDoNivel1 is not { } autor) return new(CaminhoDoNivel2.Padrao);
        if (!await CompraPropriaAsync(db, q, autor, ct)) return new(CaminhoDoNivel2.Padrao);

        var quem = await db.Users.Where(u => u.Id == autor)
            .Select(u => new { u.Role, u.SupplyManagerId }).FirstOrDefaultAsync(ct);
        if (quem is null) return new(CaminhoDoNivel2.Padrao);

        if (quem.Role == Roles.SupplyManager) return new(CaminhoDoNivel2.Dispensado);

        if (quem.Role == Roles.PurchasingOfficer && quem.SupplyManagerId is { } gestorId)
        {
            // o gestor precisa existir, estar ativo e ainda ser gestor: papel trocado no cadastro
            // não pode deixar a 2ª alçada com quem não a tem mais
            var gestor = await db.Users
                .Where(u => u.Id == gestorId && u.Active && u.Role == Roles.SupplyManager)
                .Select(u => new { u.Id, u.Name }).FirstOrDefaultAsync(ct);
            if (gestor is not null)
                return new(CaminhoDoNivel2.GestorResponsavel, gestor.Id, gestor.Name);
        }
        return new(CaminhoDoNivel2.Padrao);
    }

    /// <summary>
    /// Todas as SCs de origem são de quem deu o Nível 1. Processo sem SC de origem não conta
    /// como compra própria: sem origem não há como dizer de quem ela é.
    /// </summary>
    private static async Task<bool> CompraPropriaAsync(
        AppDbContext db, Quotation q, Guid autor, CancellationToken ct)
    {
        // filtro e projeção na entidade, `Contains` com array: o InMemory aceitaria qualquer
        // coisa aqui e o Npgsql não
        var ids = q.SourcePrIds.ToArray();
        if (ids.Length == 0) return false;
        var solicitantes = await db.Requisitions
            .Where(r => ids.Contains(r.Id))
            .Select(r => r.RequesterId).Distinct().ToListAsync(ct);
        return solicitantes.Count > 0 && solicitantes.TrueForAll(x => x == autor);
    }
}
