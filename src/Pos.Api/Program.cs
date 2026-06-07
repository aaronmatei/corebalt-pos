using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Pos.Api;
using Pos.Api.Auth;
using Pos.Api.BackOffice;
using Pos.Api.Components;
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
using Pos.Infrastructure.Ops;
using Pos.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Run headless as a Windows Service on the store-server box (no-op in console/dev — same binary).
builder.Host.UseWindowsService(o => o.ServiceName = "Corebalt POS Store Server");

// ── Per-install paths (under the install folder unless the installer overrides them in config) ──
var contentRoot = builder.Environment.ContentRootPath;
string OpsPath(string key, string fallbackSubfolder) =>
    builder.Configuration[key] is { Length: > 0 } configured ? configured : Path.Combine(contentRoot, fallbackSubfolder);
var logDirectory = OpsPath("Ops:LogDirectory", "logs");
var backupDirectory = OpsPath("Ops:BackupDirectory", "backups");
var dataProtectionKeysPath = OpsPath("Ops:DataProtectionKeysPath", "dp-keys");

// pg_dump/pg_restore live next to this path. The installer sets Ops:PgDumpPath to the bundled portable
// Postgres; when unset (dev, or an install that forgot it) auto-discover the newest installed pg_dump so
// backups/restore work, falling back to "pg_dump" on PATH.
var pgDumpPath = ResolvePgDumpPath(builder.Configuration["Ops:PgDumpPath"]);

static string ResolvePgDumpPath(string? configured)
{
    if (!string.IsNullOrWhiteSpace(configured)) return configured;
    try
    {
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var root = Path.Combine(Environment.GetFolderPath(folder), "PostgreSQL");
            if (!Directory.Exists(root)) continue;
            var found = Directory.GetFiles(root, "pg_dump.exe", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).LastOrDefault(); // newest version dir wins
            if (found is not null) return found;
        }
    }
    catch { /* discovery is best-effort */ }
    return "pg_dump"; // rely on PATH (Linux dev / explicitly installed)
}

// ── Structured rolling file logs (Serilog) for remote support, plus console for dev/SCM ──
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(logDirectory, "pos-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 31, // ~a month of daily logs
        rollOnFileSizeLimit: true, fileSizeLimitBytes: 50 * 1024 * 1024,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .CreateLogger();
builder.Host.UseSerilog();

var conn = Environment.GetEnvironmentVariable("POS_DB")
    ?? builder.Configuration.GetConnectionString("Pos")
    ?? "Host=localhost;Port=5544;Database=pos;Username=postgres;Password=pos";

builder.Services.AddInfrastructure(conn, dataProtectionKeysPath);
builder.Services.AddScoped<CheckoutService>();
builder.Services.AddScoped<Pos.Application.Catalog.ProductService>();
builder.Services.AddScoped<Pos.Application.Catalog.CategoryService>();
builder.Services.AddScoped<Pos.Application.Inventory.StockService>();
builder.Services.AddScoped<Pos.Application.Sales.ReturnService>();
// Per-tenant integration secrets are encrypted at rest via ASP.NET Core Data Protection — wired in
// AddInfrastructure (the install-level key ring on disk), not an appsettings key.

// M-Pesa (Daraja). Credentials are PER TENANT (DB, encrypted) — resolved by MpesaSettingsResolver at
// call time; CallbackUrl/TransactionType are host config. The fake provider is a dev toggle.
var useFakeMpesa =
    (bool.TryParse(builder.Configuration["Mpesa:UseFake"], out var f) && f
     || string.Equals(Environment.GetEnvironmentVariable("POS_MPESA_USEFAKE"), "true", StringComparison.OrdinalIgnoreCase))
    && !builder.Environment.IsProduction();
if (useFakeMpesa)
    builder.Services.AddSingleton<IMpesaClient, FakeMpesaClient>();
else
    builder.Services.AddScoped<IMpesaClient>(sp =>
        new DarajaMpesaClient(new HttpClient(), sp.GetRequiredService<MpesaSettingsResolver>()));
builder.Services.AddScoped<MpesaPaymentService>();

// Receipt header now comes from the tenant's DB-backed MerchantProfile (see ReceiptService), NOT
// appsettings. Only the receipt-NUMBER prefix is config (a generic branch code, not merchant identity).
builder.Services.AddSingleton(new ReceiptOptions());
builder.Services.AddScoped<ReceiptService>();
builder.Services.AddScoped<Pos.Application.Printing.ReceiptOutputService>(); // ESC/POS build + print + preview
// Vendor mark for the optional "Powered by Corebalt POS" footer (NEVER the merchant logo).
var markPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "assets", "corebalt-mark-black-mono.png");
builder.Services.AddSingleton(new Pos.Application.Printing.BrandAssets
{
    PoweredByMark = File.Exists(markPath) ? File.ReadAllBytes(markPath) : null,
});
// Cash-up: a close variance beyond this (store currency) needs Manager acknowledgement.
builder.Services.AddSingleton(new Pos.Application.Cash.CashOfficeOptions
{
    VarianceAckThreshold = decimal.TryParse(builder.Configuration["Cash:VarianceAckThreshold"], out var vt) ? vt : 500m,
});

// Backups: daily scheduled pg_dump + on-demand + restore (bundled portable Postgres tools).
builder.Services.AddSingleton(new Pos.Application.Ops.BackupOptions
{
    ConnectionString = conn,
    PgDumpPath = pgDumpPath,
    BackupDirectory = backupDirectory,
    RetentionDays = int.TryParse(builder.Configuration["Backup:RetentionDays"], out var brd) ? brd : 14,
    DailyTimeLocal = TimeOnly.TryParse(builder.Configuration["Backup:DailyTime"], out var bdt) ? bdt : new TimeOnly(22, 30),
    StaleHours = int.TryParse(builder.Configuration["Backup:StaleHours"], out var bsh) ? bsh : 48,
});
builder.Services.AddScoped<Pos.Application.Ops.IBackupService, Pos.Infrastructure.Ops.BackupManager>();
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<Pos.Infrastructure.Ops.BackupScheduler>(); // tests drive backups directly
builder.Services.AddSingleton(new ReceiptNumberFormatter(builder.Configuration["Receipt:NumberPrefix"] ?? "POS"));
builder.Services.AddScoped<SaleCompletion>();

// eTIMS: per-tenant enable/creds live in EtimsSettings (DB). Only worker tuning is host config.
builder.Services.AddSingleton(new EtimsWorkerOptions
{
    IntervalSeconds = int.TryParse(builder.Configuration["Etims:SyncIntervalSeconds"], out var ei) ? ei : 30,
    MaxAttempts = int.TryParse(builder.Configuration["Etims:SyncMaxAttempts"], out var ea) ? ea : 5,
});
builder.Services.AddSingleton<IFiscalizationProvider, FakeEtimsProvider>(); // real provider drops in by credentials later
builder.Services.AddScoped<FiscalizationService>();
builder.Services.AddScoped<FiscalSyncService>();
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<EtimsSyncWorker>(); // per-tenant enable checked inside; tests drive it directly

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
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme) // default = JWT (API + till)
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
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, o => // Blazor back-office session
    {
        o.Cookie.Name = "corebalt.backoffice";
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/login";
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("Manager", p => p.RequireRole(nameof(UserRole.Manager))); // API (JWT)
    o.AddPolicy("CashierOrAbove", p => p.RequireRole(
        nameof(UserRole.Cashier), nameof(UserRole.Supervisor), nameof(UserRole.Manager)));
    o.AddPolicy("SupervisorOrAbove", p => p.RequireRole(
        nameof(UserRole.Supervisor), nameof(UserRole.Manager))); // voids / returns / refunds
    // Back-office pages/forms: cookie principal + Manager role (challenge redirects to /login).
    o.AddPolicy("BackOfficeManager", p => p
        .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
        .RequireRole(nameof(UserRole.Manager)));
});

// Blazor Server back-office (static SSR + form posts), hosted in the store-server process.
builder.Services.AddRazorComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAntiforgery();

builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddOpenApi();
builder.Services.Configure<JsonOptions>(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

var app = builder.Build();

// Migrations on startup:
//  • Dev/Testing — self-heal a fresh/outdated DB straight through (disposable data).
//  • Production (on-prem install) — SAFE auto-migration: take a pre-migration pg_dump backup first
//    whenever the DB already holds client data; if the backup fails, REFUSE to migrate and fail the
//    service start loudly (StartupMigrator). Toggleable via Ops:AutoMigrate.
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<PosDbContext>().Database.Migrate();
}
else if (app.Configuration.GetValue("Ops:AutoMigrate", true))
{
    using var scope = app.Services.CreateScope();
    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
    var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
    var backup = new PgDumpBackup(conn, backupDirectory, pgDumpPath, loggerFactory.CreateLogger("Backup"));
    try
    {
        await StartupMigrator.RunAsync(db, backup, Path.Combine(contentRoot, "schema-version.json"),
            loggerFactory.CreateLogger("Migrator"));
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Startup migration aborted — the store server will not start.");
        Log.CloseAndFlush();
        throw; // a backup/migration failure must stop the service, not silently run on stale schema
    }
}

// First-run is handled by the setup wizard (/setup) — it creates the merchant profile + the first
// manager. No seeded credentials to hunt for. A fresh install routes to /setup until provisioned.

if (useFakeMpesa)
    app.Logger.LogWarning("M-Pesa: using the in-memory FAKE provider (dev/demo). Real Daraja is bypassed — set Mpesa:UseFake=false to restore it.");

app.UseExceptionHandler();
app.MapOpenApi();
app.UseStaticFiles(); // back-office css + favicon from wwwroot

// Route a fresh, un-provisioned install to the setup wizard (and away from it once complete).
app.UseMiddleware<SetupRedirectMiddleware>();

app.UseAuthentication();
app.UseMiddleware<DevHeaderAuthMiddleware>(); // dev/test bypass: synthesize a principal from headers
app.UseAuthorization();
app.UseAntiforgery(); // protects the back-office form posts

// Every /api/v1 route requires an authenticated caller; back-office endpoints add the Manager policy.
var v1 = app.MapGroup("/api/v1").RequireAuthorization();
v1.MapCatalog();
v1.MapCategories();
v1.MapSales();
v1.MapReceipts();
v1.MapReturns();
v1.MapMpesa();
v1.MapInventory();
v1.MapTenancy();
v1.MapCash();
v1.MapBackups();

app.MapAuth();   // /api/v1/auth/* (login + pin-login anonymous; change-password authorized)
app.MapUsers();  // /api/v1/users (Manager only)

// Blazor back-office: Razor Component pages + the form-post endpoints behind them.
app.MapRazorComponents<App>();
app.MapBackOffice();

// Unauthenticated: Daraja can't send identity headers. Idempotent + reconciled by CheckoutRequestID.
app.MapMpesaCallback();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program; // for WebApplicationFactory<Program> in tests
