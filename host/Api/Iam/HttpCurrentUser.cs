using System.Security.Claims;
using TrinoSupply.BuildingBlocks.Security;

namespace TrinoSupply.Api.Iam;

/// <summary>
/// Identidade autenticada da requisição a partir do JWT (SEC-004). Aceita <c>sub</c> ou o
/// <see cref="ClaimTypes.NameIdentifier"/> mapeado pelo handler como identificador do sujeito.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    public string? Subject
    {
        get
        {
            var user = accessor.HttpContext?.User;
            return user?.FindFirst("sub")?.Value
                ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
    }
}
