using TrinoSupply.Foundation.Application.Auth;

namespace TrinoSupply.Api.Auth;

public sealed record LoginRequest(Guid CompanyId, string Email, string Password);
public sealed record RefreshRequest(Guid CompanyId, string RefreshToken);
public sealed record LogoutRequest(Guid CompanyId, string RefreshToken);

/// <summary>Endpoints de autenticação (IdP local — FD-001-01). Abertos (pré-autenticação).</summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/auth");

        auth.MapPost("/login", async (LoginRequest req, IAuthService svc, CancellationToken ct) =>
        {
            var result = await svc.LoginAsync(req.CompanyId, req.Email, req.Password, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Json(new { code = result.Error.Code, message = result.Error.Message }, statusCode: 401);
        });

        auth.MapPost("/refresh", async (RefreshRequest req, IAuthService svc, CancellationToken ct) =>
        {
            var result = await svc.RefreshAsync(req.CompanyId, req.RefreshToken, ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Json(new { code = result.Error.Code, message = result.Error.Message }, statusCode: 401);
        });

        auth.MapPost("/logout", async (LogoutRequest req, IAuthService svc, CancellationToken ct) =>
        {
            await svc.LogoutAsync(req.CompanyId, req.RefreshToken, ct);
            return Results.NoContent();
        });

        return app;
    }
}
