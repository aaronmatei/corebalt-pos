using Pos.Application.Tenancy;

namespace Pos.Api.Endpoints;

/// <summary>
/// Platform-admin (vendor) endpoints for the HQ/cloud tier — tenant onboarding lives here, not behind a
/// tenant subdomain. Guarded by a shared admin token (<c>Admin:ApiToken</c> config, sent as the
/// <c>X-Admin-Token</c> header) rather than a tenant user, because the platform admin has no tenant
/// scope. Mapped only in Hq mode; if no token is configured every call is refused.
/// </summary>
internal static class AdminEndpoints
{
    public const string TokenHeader = "X-Admin-Token";

    public static IEndpointRouteBuilder MapAdmin(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/admin").AllowAnonymous();

        g.MapPost("/tenants", async (HttpContext http, IConfiguration config,
            TenantProvisioningService provisioner, ProvisionTenantBody body, CancellationToken ct) =>
        {
            if (!IsAuthorized(http, config)) return Results.Unauthorized();
            try
            {
                var result = await provisioner.ProvisionAsync(new ProvisionTenantRequest(
                    body.Slug, body.DisplayName, body.KraPin,
                    body.VatRegistered, body.VatNumber,
                    body.Phone, body.Email, body.Address, body.Currency,
                    body.LicenseKey,
                    body.ManagerName, body.ManagerUsername, body.ManagerPassword), ct);
                return Results.Created($"/admin/tenants/{result.Slug}", result);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        });

        g.MapPost("/tenants/{slug}/rotate-sync-token", async (string slug, HttpContext http, IConfiguration config,
            TenantProvisioningService provisioner, CancellationToken ct) =>
        {
            if (!IsAuthorized(http, config)) return Results.Unauthorized();
            var token = await provisioner.RotateSyncTokenAsync(slug, ct);
            return token is null ? Results.NotFound() : Results.Ok(new { slug, syncToken = token });
        });

        g.MapGet("/tenants", async (HttpContext http, IConfiguration config, ITenantRepository tenants, CancellationToken ct) =>
        {
            if (!IsAuthorized(http, config)) return Results.Unauthorized();
            var list = await tenants.ListAsync(ct);
            return Results.Ok(list.Select(t => new
            {
                t.Id, t.Slug, t.DisplayName, t.PrimaryStoreId, t.IsActive, t.CreatedAtUtc,
            }));
        });

        return app;
    }

    private static bool IsAuthorized(HttpContext http, IConfiguration config)
    {
        var configured = config["Admin:ApiToken"];
        if (string.IsNullOrWhiteSpace(configured)) return false; // no token set → admin surface is closed
        return http.Request.Headers.TryGetValue(TokenHeader, out var sent)
            && sent.Count == 1
            && CryptographicEquals(sent[0]!, configured);
    }

    // Constant-time compare so a wrong token can't be timed character by character.
    private static bool CryptographicEquals(string a, string b)
    {
        var ab = System.Text.Encoding.UTF8.GetBytes(a);
        var bb = System.Text.Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(ab, bb);
    }

    /// <summary>JSON body for tenant onboarding. Subdomain slug + display name + the retailer's KRA PIN +
    /// the first manager's credentials are the essentials; the rest the tenant can refine in Settings.</summary>
    public sealed record ProvisionTenantBody(
        string Slug, string DisplayName, string KraPin,
        bool VatRegistered = false, string? VatNumber = null,
        string? Phone = null, string? Email = null, string? Address = null, string? Currency = "KES",
        string? LicenseKey = null,
        string ManagerName = "Manager", string ManagerUsername = "", string ManagerPassword = "");
}
