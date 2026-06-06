using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pos.Api.Contracts;
using Pos.Application.Abstractions;
using Pos.Application.Identity;

namespace Pos.Api.Endpoints;

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/auth").WithTags("Auth");

        // Fast till login: staff code + numeric PIN. Anonymous (it mints the token).
        g.MapPost("/pin-login", async (PinLoginRequest req, AuthService auth, CancellationToken ct) =>
        {
            var token = await auth.PinLoginAsync(req.StaffCode, req.Pin, ct);
            return token is null
                ? Results.Unauthorized()
                : Results.Ok(new TokenResponse(token.Token, token.ExpiresAtUtc));
        }).AllowAnonymous();

        // Back-office login: username + password.
        g.MapPost("/login", async (LoginRequest req, AuthService auth, CancellationToken ct) =>
        {
            var token = await auth.PasswordLoginAsync(req.Username, req.Password, ct);
            return token is null
                ? Results.Unauthorized()
                : Results.Ok(new TokenResponse(token.Token, token.ExpiresAtUtc));
        }).AllowAnonymous();

        // Change own password (used to clear the forced first-login change). Requires a valid token.
        g.MapPost("/change-password", async (ChangePasswordRequest req, AuthService auth, ICurrentContext ctx, CancellationToken ct) =>
        {
            var ok = await auth.ChangePasswordAsync(ctx.TenantId, ctx.StoreId, ctx.UserId, req.CurrentPassword, req.NewPassword, ct);
            return ok ? Results.NoContent() : Results.Unauthorized();
        }).RequireAuthorization();

        return app;
    }

    public static IEndpointRouteBuilder MapUsers(this IEndpointRouteBuilder app)
    {
        // Manager-only: create staff (cashiers/supervisors/managers) with a PIN and/or password.
        app.MapPost("/api/v1/users", async (CreateUserRequest req, AuthService auth, CancellationToken ct) =>
        {
            var user = await auth.CreateUserAsync(req.Name, req.Username, req.StaffCode, req.Role, req.Pin, req.Password, ct);
            return Results.Created($"/api/v1/users/{user.Id}",
                new UserResponse(user.Id, user.Name, user.Username, user.StaffCode, user.Role, user.IsActive));
        }).RequireAuthorization("Manager").WithTags("Users");

        return app;
    }
}
