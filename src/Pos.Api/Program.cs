using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Pos.Api.Auth;
using Pos.Api.Endpoints;
using Pos.Api.Errors;
using Pos.Application.Abstractions;
using Pos.Application.Payments;
using Pos.Application.Sales;
using Pos.Infrastructure;
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
builder.Services.AddSingleton<IMpesaClient>(sp =>
    new DarajaMpesaClient(new HttpClient { BaseAddress = new Uri(mpesaOptions.BaseUrl) }, mpesaOptions,
        sp.GetRequiredService<ILogger<DarajaMpesaClient>>()));
builder.Services.AddScoped<MpesaPaymentService>();

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

// TEMP DIAGNOSTIC (remove once STK push works): confirm the RUNNING process actually loaded the
// M-Pesa secrets (passkey/secret masked). ShortCode is a public till number, shown in full.
app.Logger.LogInformation(
    "M-Pesa options loaded: BaseUrl={BaseUrl} ShortCode={ShortCode} TxnType={Txn} " +
    "ConsumerKey={CK} ConsumerSecret={CS} Passkey={PK} IsConfigured={Cfg}",
    mpesaOptions.BaseUrl, mpesaOptions.ShortCode, mpesaOptions.TransactionType,
    MaskSecret(mpesaOptions.ConsumerKey), MaskSecret(mpesaOptions.ConsumerSecret),
    MaskSecret(mpesaOptions.Passkey), mpesaOptions.IsConfigured);

static string MaskSecret(string? s) =>
    string.IsNullOrEmpty(s) ? "(MISSING!)" : $"len={s.Length},…{(s.Length >= 4 ? s[^4..] : s)}";

app.UseExceptionHandler();
app.MapOpenApi();

var v1 = app.MapGroup("/api/v1").AddEndpointFilter<AuthEndpointFilter>();
v1.MapCatalog();
v1.MapSales();
v1.MapMpesa();
v1.MapInventory();

// Unauthenticated: Daraja can't send identity headers. Idempotent + reconciled by CheckoutRequestID.
app.MapMpesaCallback();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program; // for WebApplicationFactory<Program> in tests
