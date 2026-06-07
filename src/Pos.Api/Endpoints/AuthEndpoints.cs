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

        // Optional fast sign-in: a fingerprint captured at the till's reader. Anonymous (it mints the
        // token); matching is local on the server. Falls through to a single 401 → cashier uses PIN.
        g.MapPost("/fingerprint-login", async (FingerprintLoginRequest req, FingerprintService fp, CancellationToken ct) =>
        {
            if (!TryDecodeBase64(req.Probe, out var probe)) return Results.Unauthorized();
            var result = await fp.LoginAsync(probe, ct);
            return result is null
                ? Results.Unauthorized()
                : Results.Ok(new FingerprintLoginResponse(result.Token.Token, result.Token.ExpiresAtUtc, result.StaffCode, result.Name));
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

        // ── Fingerprint enrolment / management (Manager, supervised). Templates never leave the server. ──
        app.MapPost("/api/v1/users/{id:guid}/fingerprints", async (Guid id, EnrollFingerprintRequest req, FingerprintService fp, CancellationToken ct) =>
        {
            if (!TryDecodeBase64(req.Template, out var template))
                return Results.BadRequest(new { error = "Template must be base64." });
            // No-consent / disabled surface as 409 via the DomainExceptionHandler.
            var ok = await fp.EnrollAsync(id, template, req.FingerLabel, req.Consent, ct);
            return ok ? Results.NoContent() : Results.NotFound();
        }).RequireAuthorization("Manager").WithTags("Users");

        app.MapGet("/api/v1/users/{id:guid}/fingerprints", async (Guid id, FingerprintService fp, CancellationToken ct) =>
        {
            var list = (await fp.ListAsync(id, ct))
                .Select(f => new FingerprintResponse(f.Id, f.FingerLabel, f.EnrolledAtUtc, f.ConsentGiven)).ToList();
            return Results.Ok(list);
        }).RequireAuthorization("Manager").WithTags("Users");

        app.MapDelete("/api/v1/users/{id:guid}/fingerprints/{fpId:guid}", async (Guid id, Guid fpId, FingerprintService fp, CancellationToken ct) =>
            await fp.RemoveAsync(id, fpId, ct) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization("Manager").WithTags("Users");

        return app;
    }

    /// <summary>Decode a base64 template/probe; a malformed value is treated as "no match" / bad input.</summary>
    private static bool TryDecodeBase64(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { bytes = Convert.FromBase64String(value); return bytes.Length > 0; }
        catch (FormatException) { return false; }
    }
}
