using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// Um tipo de solicitação: a classificação que a empresa dá à solicitação — reposição,
/// emergencial, projeto, contrato — e que decide, entre outras coisas, o prazo de cada etapa.
///
/// <para>
/// O campo já existia na solicitação (<c>NeedType</c>, "Tipo SC") e era <b>texto livre que
/// nenhuma tela preenchia</b>: nasceu aceito pela API e morto na interface. Livre, ele daria
/// "EPI", "epi" e "E.P.I." como três tipos que nunca somam em relatório nenhum — e é sobre
/// esse valor que o prazo por tipo passa a ser escolhido, então ele precisa ser uma
/// identidade, não uma digitação.
/// </para>
///
/// <para>
/// O <see cref="Code"/> é a ligação com as SCs: ele é gravado em <c>NeedType</c>. Solicitação
/// antiga com um texto que não está no cadastro continua legível — ela simplesmente cai no
/// prazo padrão, que é o que já valia para ela.
/// </para>
/// </summary>
public class RequestType
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Identidade do tipo, em caixa alta. É o que fica gravado na SC.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Explicação de quando usar este tipo — o que evita cada um escolher pelo palpite.</summary>
    public string? Description { get; set; }

    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Mantém os tipos de solicitação.
///
/// <para>
/// O código é <b>identidade</b> e não muda depois de gravado: SCs já criadas o carregam, e
/// trocá-lo faria as antigas apontarem para um tipo que deixou de existir. O nome, sim, se
/// corrige à vontade — é rótulo, não chave.
/// </para>
/// </summary>
public class TipoDeSolicitacaoService(AppDbContext db, TimeProvider clock)
{
    /// <summary>Quem mantém o cadastro: o mesmo par que mantém centro de custo e catálogo.</summary>
    public static bool CanMaintain(string role) =>
        role is Roles.SupplyManager or Roles.SystemAdministrator;

    public async Task<IReadOnlyList<RequestType>> ListarAsync(
        bool incluirInativos = false, CancellationToken ct = default) =>
        await db.RequestTypes
            .Where(t => incluirInativos || t.Active)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

    public async Task<(RequestType? tipo, UserError? error)> CriarAsync(
        Actor actor, string? codigo, string? nome, string? descricao, CancellationToken ct = default)
    {
        if (!CanMaintain(actor.Role))
            return (null, new("TP-ERR-900", "Seu papel não mantém os tipos de solicitação."));

        var code = Normalizar(codigo);
        var name = (nome ?? "").Trim();
        if (code.Length < 2 || name.Length < 2)
            return (null, new("TP-ERR-010", "Informe o código e o nome do tipo (mínimo 2 caracteres)."));
        if (await db.RequestTypes.AnyAsync(t => t.Code == code, ct))
            return (null, new("TP-ERR-011", $"Já existe um tipo com o código {code}."));

        var agora = clock.GetUtcNow();
        var tipo = new RequestType
        {
            Code = code, Name = name, Description = Limpar(descricao),
            CreatedAt = agora, UpdatedAt = agora,
        };
        db.RequestTypes.Add(tipo);
        await db.SaveChangesAsync(ct);
        return (tipo, null);
    }

    public async Task<(RequestType? tipo, UserError? error)> AtualizarAsync(
        Actor actor, Guid id, string? nome, string? descricao, bool? ativo, CancellationToken ct = default)
    {
        if (!CanMaintain(actor.Role))
            return (null, new("TP-ERR-900", "Seu papel não mantém os tipos de solicitação."));

        var tipo = await db.RequestTypes.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (tipo is null) return (null, new("TP-ERR-404", "Tipo de solicitação não encontrado."));

        if (nome is not null)
        {
            var name = nome.Trim();
            if (name.Length < 2) return (null, new("TP-ERR-010", "O nome do tipo precisa de ao menos 2 caracteres."));
            tipo.Name = name;
        }
        if (descricao is not null) tipo.Description = Limpar(descricao);
        if (ativo is { } a) tipo.Active = a;
        tipo.UpdatedAt = clock.GetUtcNow();

        await db.SaveChangesAsync(ct);
        return (tipo, null);
    }

    /// <summary>Como o tipo é gravado e comparado: caixa alta, sem espaços nas pontas.</summary>
    public static string Normalizar(string? valor) => (valor ?? "").Trim().ToUpperInvariant();

    private static string? Limpar(string? v) =>
        string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
