using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Pos.Api;
using Pos.Api.Auth;
using Pos.Api.Endpoints;
using Pos.Api.Errors;
using Pos.Application.Abstractions;
using Pos.Application.Fiscalization;
using Pos.Application.Identity;
using Pos.Application.Payments;
using Pos.Application.Receipts;
using Pos.Application.Sales;
using Pos.Domain.Identity;
using Pos.Infrastructure;
using Pos.Infrastructure.Fiscalization;
using Pos.Infrastructure.Identity;
using Pos.Infrastructure.Mpesa;
using Pos.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var conn = Environment.GetEnvironmentVariable("POS_DB")
    ?? builder.Configuration.GetConnectionString("Pos")
    ?? "Host=localhost;Port=5544;Database=pos;Username=postgres;Password=pos";

builder.Services.AddInfrastructure(conn);
builder.Services.AddScoped<CheckoutService>();

// M-Pesa (Daraja). Secrets come from the "Mpesa" config section / user-secrets / POS_MPESA_* env;
// never hardcoded. Singleton client so its OAuth token cache survives across requests.
var mpesaOptions = MpesaOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(mpesaOptions);

// DEV-ONLY fake provider toggle (Mpesa:UseFake / POS_MPESA_USEFAKE): auto-confirms STK so the till's
// Pay-with-M-Pesa completes without Daraja or a real device. Never honored in Production.
var useFakeMpesa = mpesaOptions.UseFake && !builder.Environment.IsProduction();
if (useFakeMpesa)
    builder.Services.AddSingleton<IMpesaClient, FakeMpesaClient>();
else
    builder.Services.AddSingleton<IMpesaClient>(_ =>
        new DarajaMpesaClient(new HttpClient { BaseAddress = new Uri(mpesaOptions.BaseUrl) }, mpesaOptions));
builder.Services.AddScoped<MpesaPaymentService>();

// Receipt header config (config-swappable via the "Store" section) + the renderer service.
var store = new StoreInfo(
    LegalName:     builder.Configuration["Store:LegalName"]     ?? "Corebalt Technologies",
    KraPin:        builder.Configuration["Store:KraPin"]        ?? "A006143399W",
    BranchName:    builder.Configuration["Store:BranchName"]    ?? "Main Branch",
    BranchAddress: builder.Configuration["Store:BranchAddress"] ?? "Nairobi, Kenya",
    Phone:         builder.Configuration["Store:Phone"]         ?? "+254722680861",
    VatNumber:     builder.Configuration["Store:VatNumber"]     ?? "VAT-PLACEHOLDER",
    Currency:      builder.Configuration["Store:Currency"]      ?? "KES");
builder.Services.AddSingleton(store);
builder.Services.AddSingleton(new ReceiptOptions());
builder.Services.AddScoped<ReceiptService>();

// Human-readable receipt numbers: branch-prefixed, per-(tenant,store) sequence (e.g. "MB-000123").
builder.Services.AddSingleton(new ReceiptNumberFormatter(builder.Configuration["Store:BranchCode"] ?? "MB"));
builder.Services.AddScoped<SaleCompletion>();

// eTIMS fiscalization seam (config section "Etims"). Only the fake/training provider exists today;
// the real VSCU/OSCU client drops in behind IFiscalizationProvider once real credentials are present.
var etims = new EtimsOptions
{
    Enabled = bool.TryParse(builder.Configuration["Etims:Enabled"], out var etimsEnabled) && etimsEnabled,
    Mode = Enum.TryParse<EtimsMode>(builder.Configuration["Etims:Mode"], out var etimsMode) ? etimsMode : EtimsMode.Vscu,
    DeviceSerial = builder.Configuration["Etims:DeviceSerial"] ?? "",
    BranchId = builder.Configuration["Etims:BranchId"] ?? "",
    CmcKey = builder.Configuration["Etims:CmcKey"] ?? "",
    BaseUrl = builder.Configuration["Etims:BaseUrl"] ?? "",
    SyncIntervalSeconds = int.TryParse(builder.Configuration["Etims:SyncIntervalSeconds"], out var etimsInterval) ? etimsInterval : 30,
    SyncMaxAttempts = int.TryParse(builder.Configuration["Etims:SyncMaxAttempts"], out var etimsAttempts) ? etimsAttempts : 5,
};
builder.Services.AddSingleton(etims);
builder.Services.AddSingleton<IFiscalizationProvider, FakeEtimsProvider>(); // real provider selected by etims.HasRealCredentials later
builder.Services.AddScoped<FiscalizationService>();
builder.Services.AddScoped<FiscalSyncService>();
if (etims.Enabled && !builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<EtimsSyncWorker>(); // tests drive FiscalSyncService directly

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentContext, ClaimsCurrentContext>();

// ── Lightweight custom identity: JWT issuing + bearer validation + role policies (NOT ASP.NET Core
// Identity). The signing key is a local store-server secret from config (works on the LAN, offline).
var jwt = new JwtOptions
{
    Key = builder.Configuration["Jwt:Key"] ?? "",
    Issuer = builder.Configuration["Jwt:Issuer"] ?? "corebalt-pos",
    Audience = builder.Configuration["Jwt:Audience"] ?? "corebalt-pos",
    LifetimeMinutes = int.TryParse(builder.Configuration["Jwt:LifetimeMinutes"], out var jwtLife) ? jwtLife : 720,
};
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();

// The single tenant/store this store-server serves (login scope + bootstrap target).
var storeServer = new StoreServerOptions
{
    TenantId = Guid.TryParse(builder.Configuration["StoreServer:TenantId"], out var ssTenant) ? ssTenant : Guid.Empty,
    StoreId = Guid.TryParse(builder.Configuration["StoreServer:StoreId"], out var ssStore) ? ssStore : Guid.Empty,
};
builder.Services.AddSingleton(storeServer);
builder.Services.AddScoped<AuthService>();

JwtSecurityTokenHandler.DefaultMapInboundClaims = false; // keep raw claim names ("sub", "role", ...)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                string.IsNullOrWhiteSpace(jwt.Key) ? new string('0', 64) : jwt.Key)),
            RoleClaimType = PosClaims.Role,
            NameClaimType = PosClaims.Name,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("Manager", p => p.RequireRole(nameof(UserRole.Manager)));
    o.AddPolicy("CashierOrAbove", p => p.RequireRole(
        nameof(UserRole.Cashier), nameof(UserRole.Supervisor), nameof(UserRole.Manager)));
});

builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddOpenApi();
builder.Services.Configure<JsonOptions>(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

var app = builder.Build();

// In Development, self-heal a fresh/outdated database by applying migrations on startup, so a
// recreated docker container (or a clean checkout) doesn't 500 with "relation does not exist".
// NOT done in Production: there migrations are applied deliberately by ops/CI, never implicitly by
// the web host. Tests run under the "Testing" environment and migrate via their own fixture.
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<PosDbContext>().Database.Migrate();
}

// Bootstrap: seed the initial Manager (config username + default password, must-change-on-login) if
// none exists for this store. No credentials in source — they come from config. Wrapped so a not-yet-
// migrated production DB skips the seed rather than crashing the host.
if (storeServer.TenantId != Guid.Empty)
{
    try
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AuthService>().EnsureBootstrapManagerAsync(
            app.Configuration["Auth:Bootstrap:Username"] ?? "manager",
            app.Configuration["Auth:Bootstrap:Password"] ?? "ChangeMe!123");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Bootstrap manager seed skipped (DB not ready?).");
    }

    // DEV ONLY: seed a demo cashier with a PIN so the till's PIN login works out of the box.
    if (app.Environment.IsDevelopment())
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var staff = app.Configuration["Auth:DevCashier:StaffCode"] ?? "1001";
            var pin = app.Configuration["Auth:DevCashier:Pin"] ?? "1234";
            await scope.ServiceProvider.GetRequiredService<AuthService>().EnsureDevCashierAsync("Demo Cashier", staff, pin);
            app.Logger.LogWarning("DEV cashier available for till PIN login — staff code {Staff}, PIN {Pin}.", staff, pin);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Dev cashier seed skipped.");
        }
    }
}

if (useFakeMpesa)
    app.Logger.LogWarning("M-Pesa: using the in-memory FAKE provider (dev/demo). Real Daraja is bypassed — set Mpesa:UseFake=false to restore it.");

app.UseExceptionHandler();
app.MapOpenApi();

app.UseAuthentication();
app.UseMiddleware<DevHeaderAuthMiddleware>(); // dev/test bypass: synthesize a principal from headers
app.UseAuthorization();

// Every /api/v1 route requires an authenticated caller; back-office endpoints add the Manager policy.
var v1 = app.MapGroup("/api/v1").RequireAuthorization();
v1.MapCatalog();
v1.MapSales();
v1.MapReceipts();
v1.MapMpesa();
v1.MapInventory();

app.MapAuth();   // /api/v1/auth/* (login + pin-login anonymous; change-password authorized)
app.MapUsers();  // /api/v1/users (Manager only)

// Unauthenticated: Daraja can't send identity headers. Idempotent + reconciled by CheckoutRequestID.
app.MapMpesaCallback();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program; // for WebApplicationFactory<Program> in tests
