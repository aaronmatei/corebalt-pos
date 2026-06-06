using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Pos.Api;
using Pos.Api.Auth;
using Pos.Api.Endpoints;
using Pos.Api.Errors;
using Pos.Application.Abstractions;
using Pos.Application.Fiscalization;
using Pos.Application.Payments;
using Pos.Application.Receipts;
using Pos.Application.Sales;
using Pos.Infrastructure;
using Pos.Infrastructure.Fiscalization;
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
builder.Services.AddScoped<ICurrentContext, HeaderCurrentContext>();

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
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<PosDbContext>().Database.Migrate();
}

app.UseExceptionHandler();
app.MapOpenApi();

var v1 = app.MapGroup("/api/v1").AddEndpointFilter<AuthEndpointFilter>();
v1.MapCatalog();
v1.MapSales();
v1.MapReceipts();
v1.MapMpesa();
v1.MapInventory();

// Unauthenticated: Daraja can't send identity headers. Idempotent + reconciled by CheckoutRequestID.
app.MapMpesaCallback();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program; // for WebApplicationFactory<Program> in tests
