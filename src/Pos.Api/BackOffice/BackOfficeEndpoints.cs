using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Pos.Application.Abstractions;
using Pos.Application.Catalog;
using Pos.Application.Identity;
using Pos.Application.Inventory;
using Pos.Application.Notifications;
using Pos.Application.Tenancy;
using Pos.Domain.Catalog;
using Pos.Domain.Identity;
using Pos.Domain.Inventory;
using Pos.Domain.Tenancy;
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

        // First-run setup (anonymous; only while not yet complete). Provisions the install's StoreServer
        // scope: merchant profile, M-Pesa/eTIMS settings, entitlements and the first manager.
        g.MapPost("/setup", async (HttpContext http, SetupService setup, StoreServerOptions server, CancellationToken ct) =>
        {
            if (await setup.IsCompleteAsync(server.TenantId, ct)) return Results.Redirect("/login");

            var form = http.Request.Form;
            string S(string k) => form[k].ToString();
            bool B(string k) => form[k] == "true" || form[k] == "on";

            var req = new ProvisionRequest(
                LegalName: S("legalName"), TradingName: S("tradingName"), KraPin: S("kraPin"),
                VatRegistered: B("vatRegistered"), VatNumber: S("vatNumber"),
                Phone: S("phone"), Email: S("email"), Address: S("address"),
                Currency: string.IsNullOrWhiteSpace(S("currency")) ? "KES" : S("currency"),
                BranchName: S("branchName"), BranchCode: S("branchCode"), BranchAddress: S("branchAddress"),
                ReceiptFooter: S("receiptFooter"), ShowPoweredBy: B("showPoweredBy"),
                MpesaEnabled: B("mpesaEnabled"), MpesaShortCode: S("mpesaShortCode"), MpesaConsumerKey: S("mpesaConsumerKey"),
                MpesaConsumerSecret: S("mpesaConsumerSecret"), MpesaPasskey: S("mpesaPasskey"),
                MpesaEnvironment: Enum.TryParse<MpesaEnvironment>(S("mpesaEnvironment"), true, out var me) ? me : MpesaEnvironment.Sandbox,
                EtimsEnabled: B("etimsEnabled"), EtimsMode: Enum.TryParse<EtimsMode>(S("etimsMode"), true, out var em) ? em : EtimsMode.Vscu,
                EtimsDeviceSerial: S("etimsDeviceSerial"), EtimsBranchId: S("etimsBranchId"), EtimsCmcKey: S("etimsCmcKey"), EtimsBaseUrl: S("etimsBaseUrl"),
                // Entitlements come from a Corebalt-signed licence key (verified in SetupService) — not
                // editable edition/flag inputs. Blank = run on the unlicensed baseline until a key is applied.
                LicenseKey: S("licenseKey"),
                ManagerName: S("managerName"), ManagerUsername: S("managerUsername"), ManagerPassword: S("managerPassword"));

            try { await setup.ProvisionAsync(server.TenantId, server.StoreId, req, ct); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.Redirect($"/setup?error={Uri.EscapeDataString(ex.Message)}");
            }
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
        g.MapPost("/products", async (ProductService svc, ICurrentContext ctx, IMerchantProfileRepository merchants,
            [FromForm] string sku, [FromForm] string name, [FromForm] string? barcode,
            [FromForm] string unit, [FromForm] string taxClass, [FromForm] decimal priceAmount,
            [FromForm] string? categoryId, CancellationToken ct) =>
        {
            try
            {
                var currency = (await merchants.GetAsync(ctx.TenantId, ct))?.Currency ?? "KES";
                await svc.CreateAsync(sku, name, new Money(priceAmount, currency),
                    Parse<UnitOfMeasure>(unit), barcode, Parse<TaxClass>(taxClass), ParseGuid(categoryId), ct);
                return Results.Redirect("/products");
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return Back("/products/new", ex); }
        }).RequireAuthorization("BackOfficeManager");

        g.MapPost("/products/{id:guid}", async (Guid id, ProductService svc,
            [FromForm] string name, [FromForm] string? barcode, [FromForm] string unit,
            [FromForm] string taxClass, [FromForm] string? isActive, [FromForm] string? categoryId,
            [FromForm] decimal? reorderLevel, [FromForm] decimal? reorderQuantity, CancellationToken ct) =>
        {
            try
            {
                await svc.UpdateAsync(id, name, barcode, Parse<UnitOfMeasure>(unit), Parse<TaxClass>(taxClass),
                    isActive == "true", ParseGuid(categoryId), reorderLevel, reorderQuantity, ct);
                return Results.Redirect("/products");
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return Back($"/products/{id}", ex); }
        }).RequireAuthorization("BackOfficeManager");

        // ── Categories ──
        g.MapPost("/categories", async (CategoryService svc,
            [FromForm] string name, [FromForm] int? displayOrder, CancellationToken ct) =>
        {
            try { await svc.CreateAsync(name, parentId: null, displayOrder ?? 0, ct); return Results.Redirect("/categories"); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return Back("/categories", ex); }
        }).RequireAuthorization("BackOfficeManager");

        g.MapPost("/categories/{id:guid}", async (Guid id, CategoryService svc,
            [FromForm] string name, [FromForm] int? displayOrder, [FromForm] string? isActive, CancellationToken ct) =>
        {
            try
            {
                await svc.UpdateAsync(id, name, parentId: null, displayOrder ?? 0, isActive == "true", ct);
                return Results.Redirect("/categories");
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return Back("/categories", ex); }
        }).RequireAuthorization("BackOfficeManager");

        g.MapPost("/products/{id:guid}/price", async (Guid id, ProductService svc, ICurrentContext ctx,
            IMerchantProfileRepository merchants, [FromForm] decimal amount, CancellationToken ct) =>
        {
            try
            {
                var currency = (await merchants.GetAsync(ctx.TenantId, ct))?.Currency ?? "KES";
                await svc.RepriceAsync(id, new Money(amount, currency), ct); return Results.Redirect($"/products/{id}");
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return Back($"/products/{id}", ex); }
        }).RequireAuthorization("BackOfficeManager");

        // ── Notifications (in-app feed: acknowledge) ──
        g.MapPost("/notifications/{id:guid}/read", async (Guid id, NotificationFeedService feed, CancellationToken ct) =>
        {
            await feed.MarkReadAsync(id, ct);
            return Results.Redirect("/reorder");
        }).RequireAuthorization("BackOfficeManager");

        g.MapPost("/notifications/read-all", async (NotificationFeedService feed, CancellationToken ct) =>
        {
            await feed.MarkAllReadAsync(ct);
            return Results.Redirect("/reorder");
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

        // Enrol a cashier's fingerprint under supervision (consent required). The template is captured at
        // the reader; in dev it's the base64 stub the page supplies. No-consent → InvalidOperationException → error.
        g.MapPost("/users/{id:guid}/enroll-fingerprint", async (Guid id, FingerprintService fp,
            [FromForm] string template, [FromForm] string? fingerLabel, [FromForm] string? consent, CancellationToken ct) =>
        {
            try
            {
                if (!TryBase64(template, out var bytes)) return Back("/users", new ArgumentException("Invalid capture."));
                await fp.EnrollAsync(id, bytes, fingerLabel, consent == "true" || consent == "on", ct);
                return Results.Redirect("/users");
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return Back("/users", ex); }
        }).RequireAuthorization("BackOfficeManager");

        g.MapPost("/users/{id:guid}/fingerprints/{fpId:guid}/remove", async (Guid id, Guid fpId, FingerprintService fp, CancellationToken ct) =>
        {
            await fp.RemoveAsync(id, fpId, ct);
            return Results.Redirect("/users");
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

        // ── Settings (Manager) — the client edits their OWN integration creds and applies licence keys ──
        g.MapPost("/settings/mpesa", async (SettingsService settings, ICurrentContext ctx,
            [FromForm] bool enabled, [FromForm] string? shortCode, [FromForm] string? consumerKey,
            [FromForm] string? consumerSecret, [FromForm] string? passkey, [FromForm] string? environment, CancellationToken ct) =>
        {
            var env = Enum.TryParse<MpesaEnvironment>(environment, true, out var e) ? e : MpesaEnvironment.Sandbox;
            await settings.UpdateMpesaAsync(ctx.TenantId, enabled, shortCode ?? "", consumerKey ?? "",
                consumerSecret ?? "", passkey ?? "", env, ct);
            return Results.Redirect("/settings?saved=mpesa");
        }).RequireAuthorization("BackOfficeManager");

        g.MapPost("/settings/etims", async (SettingsService settings, ICurrentContext ctx,
            [FromForm] bool enabled, [FromForm] string? mode, [FromForm] string? deviceSerial,
            [FromForm] string? branchId, [FromForm] string? cmcKey, [FromForm] string? baseUrl, CancellationToken ct) =>
        {
            var m = Enum.TryParse<EtimsMode>(mode, true, out var em) ? em : EtimsMode.Vscu;
            await settings.UpdateEtimsAsync(ctx.TenantId, enabled, m, deviceSerial ?? "", branchId ?? "", cmcKey ?? "", baseUrl ?? "", ct);
            return Results.Redirect("/settings?saved=etims");
        }).RequireAuthorization("BackOfficeManager");

        g.MapPost("/settings/license", async (SettingsService settings, ICurrentContext ctx,
            [FromForm] string licenseKey, CancellationToken ct) =>
        {
            var result = await settings.ApplyLicenseAsync(ctx.TenantId, licenseKey ?? "", ct);
            return result.Ok
                ? Results.Redirect("/settings?saved=license")
                : Results.Redirect($"/settings?error={Uri.EscapeDataString(result.Error ?? "Invalid licence key.")}");
        }).RequireAuthorization("BackOfficeManager");

        // ── Backups (Manager) ──
        g.MapPost("/settings/second-location", async (SettingsService settings, ICurrentContext ctx,
            [FromForm] string? path, CancellationToken ct) =>
        {
            await settings.UpdateSecondBackupLocationAsync(ctx.TenantId, path, ct);
            return Results.Redirect("/settings?saved=backup");
        }).RequireAuthorization("BackOfficeManager");

        g.MapPost("/backups/now", async (Pos.Application.Ops.IBackupService backups, CancellationToken ct) =>
        {
            try { var r = await backups.BackupNowAsync("manual", ct); return Results.Redirect($"/backups?saved=Backup+created:+{Uri.EscapeDataString(r.FileName)}"); }
            catch (Exception ex) { return Results.Redirect($"/backups?error={Uri.EscapeDataString(ex.Message)}"); }
        }).RequireAuthorization("BackOfficeManager");

        g.MapPost("/backups/restore", async (Pos.Application.Ops.IBackupService backups, [FromForm] string fileName, CancellationToken ct) =>
        {
            var result = await backups.RestoreAsync(fileName, ct);
            return result.Ok
                ? Results.Redirect($"/backups?saved=Restored+from+{Uri.EscapeDataString(fileName)}+(safety+backup:+{Uri.EscapeDataString(result.SafetyBackup ?? "")})")
                : Results.Redirect($"/backups?error={Uri.EscapeDataString(result.Error ?? "Restore failed.")}");
        }).RequireAuthorization("BackOfficeManager");

        // Print an X/Z report on the session's register printer (reuses the ESC/POS pipeline).
        g.MapPost("/sessions/{id:guid}/print", async (Guid id, ICurrentContext ctx,
            Pos.Application.Cash.IRegisterSessionRepository sessions, Pos.Application.Cash.CashOfficeReportService reports,
            Pos.Application.Printing.ReceiptOutputService output, CancellationToken ct) =>
        {
            var session = await sessions.GetAsync(ctx.TenantId, ctx.StoreId, id, ct);
            if (session is null) return Results.NotFound();
            var report = await reports.BuildAsync(session, ct);
            await output.PrintShiftReportAsync(report.RegisterId, report, ct);
            return Results.Redirect($"/sessions/{id}");
        }).RequireAuthorization("BackOfficeManager");

        return app;
    }

    private static IResult Back(string path, Exception ex) =>
        Results.Redirect($"{path}?error={Uri.EscapeDataString(ex.Message)}");

    private static T Parse<T>(string value) where T : struct, Enum => Enum.Parse<T>(value, ignoreCase: true);

    /// <summary>Form selects post "" for "no category"; turn a blank/invalid value into null.</summary>
    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var g) && g != Guid.Empty ? g : null;

    private static bool TryBase64(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { bytes = Convert.FromBase64String(value); return bytes.Length > 0; }
        catch (FormatException) { return false; }
    }

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
