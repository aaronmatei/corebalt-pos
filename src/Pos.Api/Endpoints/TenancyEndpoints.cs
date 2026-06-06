using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pos.Api.Contracts;
using Pos.Application.Abstractions;
using Pos.Application.Tenancy;
using Pos.Domain.Tenancy;
using Pos.SharedKernel.Ids;

namespace Pos.Api.Endpoints;

internal static class TenancyEndpoints
{
    public static IEndpointRouteBuilder MapTenancy(this IEndpointRouteBuilder app)
    {
        // The merchant's own profile (CLIENT identity) — never Corebalt's.
        app.MapGet("/merchant", async (ICurrentContext ctx, IMerchantProfileRepository merchants, CancellationToken ct) =>
        {
            var p = await merchants.GetAsync(ctx.TenantId, ct);
            return p is null
                ? Results.NotFound()
                : Results.Ok(new MerchantProfileResponse(p.LegalName, p.TradingName, p.KraPin, p.VatRegistered, p.VatNumber, p.Currency, p.SetupComplete));
        }).WithTags("Tenancy");

        app.MapGet("/entitlements", async (IEntitlements entitlements, CancellationToken ct) =>
        {
            var e = await entitlements.CurrentAsync(ct);
            if (e is null) return Results.NotFound();
            var features = Enum.GetValues<Feature>().Where(f => f != Feature.None && (e.Features & f) == f).Select(f => f.ToString()).ToArray();
            return Results.Ok(new EntitlementsResponse(e.Edition.ToString(), features, e.MaxTills, e.MaxBranches, e.ValidUntil,
                e.ValidUntil is { } u && DateTimeOffset.UtcNow > u));
        }).WithTags("Tenancy");

        // Apply a Corebalt-signed licence key (Manager). The client cannot set edition/flags/limits
        // directly — only apply a key; a tampered/expired/wrong-tenant key is rejected (400).
        app.MapPost("/license", async (ApplyLicenseRequest req, ICurrentContext ctx, SettingsService settings, CancellationToken ct) =>
        {
            var result = await settings.ApplyLicenseAsync(ctx.TenantId, req.LicenseKey ?? "", ct);
            if (!result.Ok) return Results.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest);
            var l = result.License!;
            var features = Enum.GetValues<Feature>().Where(f => f != Feature.None && (l.Features & f) == f).Select(f => f.ToString()).ToArray();
            return Results.Ok(new EntitlementsResponse(l.Edition.ToString(), features, l.MaxTills, l.MaxBranches, l.ValidUntil, false));
        }).RequireAuthorization("Manager").WithTags("Tenancy");

        // Add a branch — Manager + gated by the MultiBranch entitlement (feature flag blocks the module).
        app.MapPost("/branches", async (CreateBranchRequest req, ICurrentContext ctx, IEntitlements entitlements,
            IMerchantProfileRepository merchants, IUnitOfWork uow, CancellationToken ct) =>
        {
            if (!await entitlements.HasAsync(Feature.MultiBranch, ct))
                return Results.Problem("Multi-branch is not enabled on this licence.", statusCode: StatusCodes.Status403Forbidden);

            var profile = await merchants.GetAsync(ctx.TenantId, ct);
            if (profile is null) return Results.NotFound();

            var id = Uuid7.NewGuid();
            profile.AddBranch(id, req.Name, req.Code, req.Address);
            await uow.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/branches/{id}", new BranchResponse(id, req.Name, req.Code, req.Address));
        }).RequireAuthorization("Manager").WithTags("Tenancy");

        return app;
    }
}
