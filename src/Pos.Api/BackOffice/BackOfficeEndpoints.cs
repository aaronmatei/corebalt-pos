using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Abstractions;
using Pos.Application.Catalog;
using Pos.Application.Identity;
using Pos.Application.Inventory;
using Pos.Application.Receipts;
using Pos.Domain.Catalog;
using Pos.Domain.Identity;
using Pos.Domain.Inventory;
using Pos.SharedKernel;

namespace Pos.Api.BackOffice;

/// <summary>
/// Form-post actions behind the Blazor back-office pages. Each calls the SAME Application services the
/// JSON API uses (no duplicated logic), then redirects back to the relevant page (errors flow back as
/// a ?error= query the page renders). Cookie auth + the BackOfficeManager policy gate everything but
/// login/logout. Antiforgery is enforced (the pages render the token).
/// </summary>
internal static class BackOfficeEndpoints
{
    public static IEndpointRouteBuilder MapBackOffice(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/backoffice");

        // ── Auth ──
        g.MapPost("/login", async (HttpContext http, AuthService auth,
            [FromForm] string username, [FromForm] string password, CancellationToken ct) =>
        {
            var user = await auth.ValidatePasswordAsync(username, password, ct);
            if (user is null) return Results.Redirect("/login?error=Invalid+username+or+password");
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, BuildPrincipal(user));
            return Results.Redirect(user.MustChangePassword ? "/change-password" : "/");
        }).AllowAnonymous();

        g.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        }).AllowAnonymous();

        g.MapPost("/change-password", async (HttpContext http, AuthService auth, ICurrentContext ctx,
            [FromForm] string currentPassword, [FromForm] string newPassword, CancellationToken ct) =>
        {
            try
            {
                if (!await auth.ChangePasswordAsync(ctx.TenantId, ctx.StoreId, ctx.UserId, currentPassword, newPassword, ct))
                    return Results.Redirect("/change-password?error=Current+password+is+incorrect");
                var user = await auth.GetUserAsync(ctx.UserId, ct);
                if (user is not null)
                    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, BuildPrincipal(user));
                return Results.Redirect("/");
            }
            catch (ArgumentException ex) { return Back("/change-password", ex); }
        }).RequireAuthorization("BackOfficeManager");

        // ── Products ──
        g.MapPost("/products", async (ProductService svc, StoreInfo store,
            [FromForm] string sku, [FromForm] string name, [FromForm] string? barcode,
            [FromForm] string unit, [FromForm] string taxClass, [FromForm] decimal priceAmount, CancellationToken ct) =>
        {
            try
            {
                await svc.CreateAsync(sku, name, new Money(priceAmount, store.Currency),
                    Parse<UnitOfMeasure>(unit), barcode, Parse<TaxClass>(taxClass), ct);
                return Results.Redirect("/products");
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return Back("/products/new", ex); }
        }).RequireAuthorization("BackOfficeManager");

        g.MapPost("/products/{id:guid}", async (Guid id, ProductService svc,
            [FromForm] string name, [FromForm] string? barcode, [FromForm] string unit,
            [FromForm] string taxClass, [FromForm] string? isActive, CancellationToken ct) =>
        {
            try
            {
                await svc.UpdateAsync(id, name, barcode, Parse<UnitOfMeasure>(unit), Parse<TaxClass>(taxClass),
                    isActive == "true", ct);
                return Results.Redirect("/products");
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return Back($"/products/{id}", ex); }
        }).RequireAuthorization("BackOfficeManager");

        g.MapPost("/products/{id:guid}/price", async (Guid id, ProductService svc, StoreInfo store,
            [FromForm] decimal amount, CancellationToken ct) =>
        {
            try { await svc.RepriceAsync(id, new Money(amount, store.Currency), ct); return Results.Redirect($"/products/{id}"); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return Back($"/products/{id}", ex); }
        }).RequireAuthorization("BackOfficeManager");

        // ── Stock ──
        g.MapPost("/stock/receive", async (StockService svc,
            [FromForm] Guid productId, [FromForm] decimal quantity, [FromForm] string reason, [FromForm] string? reference, CancellationToken ct) =>
        {
            try { await svc.ReceiveAsync(productId, quantity, Parse<StockMovementReason>(reason), reference, ct); return Results.Redirect("/stock"); }
            catch (ArgumentException ex) { return Back("/stock", ex); }
        }).RequireAuthorization("BackOfficeManager");

        g.MapPost("/stock/adjust", async (StockService svc,
            [FromForm] Guid productId, [FromForm] decimal quantity, [FromForm] string? reference, CancellationToken ct) =>
        {
            try { await svc.AdjustAsync(productId, quantity, reference, ct); return Results.Redirect("/stock"); }
            catch (ArgumentException ex) { return Back("/stock", ex); }
        }).RequireAuthorization("BackOfficeManager");

        // ── Users / cashiers ──
        g.MapPost("/users", async (AuthService auth,
            [FromForm] string name, [FromForm] string staffCode, [FromForm] string pin, [FromForm] string role, CancellationToken ct) =>
        {
            try
            {
                // No separate username field in the form — derive a unique one from the staff code.
                await auth.CreateUserAsync(name, $"staff-{staffCode.Trim().ToLowerInvariant()}", staffCode,
                    Parse<UserRole>(role), pin, password: null, ct);
                return Results.Redirect("/users");
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return Back("/users", ex); }
        }).RequireAuthorization("BackOfficeManager");

        g.MapPost("/users/{id:guid}/reset-pin", async (Guid id, AuthService auth, [FromForm] string pin, CancellationToken ct) =>
        {
            try { await auth.ResetPinAsync(id, pin, ct); return Results.Redirect("/users"); }
            catch (ArgumentException ex) { return Back("/users", ex); }
        }).RequireAuthorization("BackOfficeManager");

        g.MapPost("/users/{id:guid}/deactivate", async (Guid id, AuthService auth, CancellationToken ct) =>
        {
            await auth.DeactivateUserAsync(id, ct);
            return Results.Redirect("/users");
        }).RequireAuthorization("BackOfficeManager");

        g.MapPost("/users/{id:guid}/reactivate", async (Guid id, AuthService auth, CancellationToken ct) =>
        {
            await auth.ReactivateUserAsync(id, ct);
            return Results.Redirect("/users");
        }).RequireAuthorization("BackOfficeManager");

        return app;
    }

    private static IResult Back(string path, Exception ex) =>
        Results.Redirect($"{path}?error={Uri.EscapeDataString(ex.Message)}");

    private static T Parse<T>(string value) where T : struct, Enum => Enum.Parse<T>(value, ignoreCase: true);

    private static ClaimsPrincipal BuildPrincipal(User u)
    {
        var id = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme, PosClaims.Name, PosClaims.Role);
        id.AddClaim(new Claim(PosClaims.UserId, u.Id.ToString()));
        id.AddClaim(new Claim(PosClaims.TenantId, u.TenantId.ToString()));
        id.AddClaim(new Claim(PosClaims.StoreId, u.StoreId.ToString()));
        id.AddClaim(new Claim(PosClaims.Name, u.Name));
        id.AddClaim(new Claim(PosClaims.StaffCode, u.StaffCode));
        id.AddClaim(new Claim(PosClaims.Role, u.Role.ToString()));
        id.AddClaim(new Claim(PosClaims.MustChangePassword, u.MustChangePassword ? "true" : "false"));
        return new ClaimsPrincipal(id);
    }
}
