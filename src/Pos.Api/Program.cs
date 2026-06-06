using Microsoft.AspNetCore.Http.Json;
using Pos.Api.Auth;
using Pos.Api.Endpoints;
using Pos.Api.Errors;
using Pos.Application.Abstractions;
using Pos.Application.Payments;
using Pos.Application.Sales;
using Pos.Infrastructure;
using Pos.Infrastructure.Mpesa;

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
