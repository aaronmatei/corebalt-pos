using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pos.Application.Abstractions;
using Pos.Application.Catalog;
using Pos.Application.Receipts;
using Pos.Application.Sales;
using Pos.Domain.Catalog;
using Pos.Domain.Sales;
using Pos.Infrastructure;
using Pos.Infrastructure.Persistence;
using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

const string DefaultConn = "Host=localhost;Port=5544;Database=pos;Username=postgres;Password=pos";
var conn = Environment.GetEnvironmentVariable("POS_DB") ?? DefaultConn;

// Stand-in scope. In production these come from the authenticated request (step 3 API);
// keeping them here as fixed values lets us re-run the demo and see the same scoping.
var demoCtx = new DemoCurrentContext(
    tenantId: Guid.Parse("019e9a8a-0000-7000-8000-000000000001"),
    storeId:  Guid.Parse("019e9a8a-0000-7000-8000-000000000002"),
    userId:   Guid.Parse("019e9a8a-0000-7000-8000-000000000003"));

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<ICurrentContext>(demoCtx);
builder.Services.AddInfrastructure(conn);
builder.Services.AddSingleton(new ReceiptNumberFormatter("MB"));
builder.Services.AddScoped<SaleCompletion>();
builder.Services.AddScoped<CheckoutService>();

using var host = builder.Build();

await using (var scope = host.Services.CreateAsyncScope())
{
    var sp = scope.ServiceProvider;
    var db = sp.GetRequiredService<PosDbContext>();

    Console.WriteLine($"== Migrating database ({MaskPassword(conn)}) ==");
    await db.Database.MigrateAsync();

    // First-run setup: provision a merchant profile for the demo tenant so checkout is allowed.
    var setup = sp.GetRequiredService<Pos.Application.Tenancy.SetupService>();
    if (!await setup.IsCompleteAsync(demoCtx.TenantId))
        await setup.ProvisionAsync(demoCtx.TenantId, demoCtx.StoreId, new Pos.Application.Tenancy.ProvisionRequest(
            LegalName: "Demo Retailer Ltd", TradingName: "Demo Retailer", KraPin: "P051234567X",
            VatRegistered: true, VatNumber: "VAT001", Phone: "+254700000000", Email: null, Address: "Nairobi",
            Currency: "KES", BranchName: "Main Branch", BranchCode: "MB", BranchAddress: "Nairobi",
            ReceiptFooter: null, ShowPoweredBy: true,
            MpesaEnabled: false, MpesaShortCode: null, MpesaConsumerKey: null, MpesaConsumerSecret: null, MpesaPasskey: null,
            MpesaEnvironment: Pos.Domain.Tenancy.MpesaEnvironment.Sandbox,
            EtimsEnabled: false, EtimsMode: Pos.Domain.Tenancy.EtimsMode.Vscu,
            EtimsDeviceSerial: null, EtimsBranchId: null, EtimsCmcKey: null, EtimsBaseUrl: null,
            Edition: Pos.Domain.Tenancy.Edition.Retail, Features: Pos.Domain.Tenancy.Feature.None,
            MaxTills: 2, MaxBranches: 1, LicenseKey: null, ValidUntil: null,
            ManagerName: "Manager", ManagerUsername: "demo-manager", ManagerPassword: "DemoPass123"));

    var products = sp.GetRequiredService<IProductRepository>();
    var uow = sp.GetRequiredService<IUnitOfWork>();

    // Ensure the demo product exists. SKU lookup is unique per (tenant, store).
    var milk = await products.FindBySkuAsync(demoCtx.TenantId, demoCtx.StoreId, "MILK-500");
    if (milk is null)
    {
        milk = Product.Create(demoCtx.TenantId, demoCtx.StoreId,
            sku: "MILK-500", name: "Brookside Milk 500ml",
            price: new Money(60m), unit: UnitOfMeasure.Each);
        await products.AddAsync(milk);
        await uow.SaveChangesAsync();
        Console.WriteLine($"  + product {milk.Sku} created ({milk.Id})");
    }
    else
    {
        Console.WriteLine($"  = product {milk.Sku} already exists ({milk.Id})");
    }

    Console.WriteLine();
    Console.WriteLine("== Checkout via CheckoutService ==");
    var checkout = sp.GetRequiredService<CheckoutService>();
    var registerId = Uuid7.NewGuid();

    var saleId = await checkout.StartAsync(registerId);
    await checkout.AddLineAsync(saleId, milk.Id, quantity: 2);
    await checkout.AddTenderAsync(saleId, TenderType.Cash, amount: 120m);
    var result = await checkout.CompleteAsync(saleId);
    Console.WriteLine($"  saleId   = {result.SaleId}");
    Console.WriteLine($"  total    = {result.Currency} {result.Total:0.00}");
    Console.WriteLine($"  change   = {result.Currency} {result.ChangeDue:0.00}");

    Console.WriteLine();
    Console.WriteLine("== Reload via repository (proves the EF mapping round-trips) ==");
    var sales = sp.GetRequiredService<ISaleRepository>();
    var reloaded = await sales.GetAsync(demoCtx.TenantId, demoCtx.StoreId, saleId)
        ?? throw new InvalidOperationException("Sale did not reload — EF mapping is broken.");
    Console.WriteLine($"  status   = {reloaded.Status}");
    Console.WriteLine($"  lines    = {reloaded.Lines.Count} ({string.Join(", ", reloaded.Lines.Select(l => $"{l.Quantity} × {l.Description}"))})");
    Console.WriteLine($"  tenders  = {reloaded.Tenders.Count} ({string.Join(", ", reloaded.Tenders.Select(t => $"{t.Type} {t.Amount}"))})");
    Console.WriteLine($"  subtotal = {reloaded.Subtotal}");

    Console.WriteLine();
    Console.WriteLine("== Outbox rows for this sale (drained by DomainEventsToOutboxInterceptor) ==");
    var outbox = await db.OutboxMessages
        .Where(m => m.AggregateId == saleId)
        .OrderBy(m => m.OccurredAtUtc).ThenBy(m => m.Id)
        .ToListAsync();
    foreach (var m in outbox)
    {
        Console.WriteLine($"  type={m.EventType}");
        Console.WriteLine($"    tenant={m.TenantId} store={m.StoreId}");
        Console.WriteLine($"    payload={m.Payload}");
        Console.WriteLine($"    processed={(m.ProcessedAtUtc.HasValue ? "yes" : "no")} attempts={m.Attempts}");
    }
    if (outbox.Count != 1) throw new InvalidOperationException(
        $"Expected exactly one SaleCompleted outbox row for this sale, got {outbox.Count}.");
}

return 0;

static string MaskPassword(string s)
{
    var idx = s.IndexOf("Password=", StringComparison.OrdinalIgnoreCase);
    if (idx < 0) return s;
    var end = s.IndexOf(';', idx);
    return s[..idx] + "Password=***" + (end > 0 ? s[end..] : string.Empty);
}

sealed class DemoCurrentContext(Guid tenantId, Guid storeId, Guid userId) : ICurrentContext
{
    public Guid TenantId { get; } = tenantId;
    public Guid StoreId  { get; } = storeId;
    public Guid UserId   { get; } = userId;
    public Pos.Domain.Identity.UserRole Role { get; } = Pos.Domain.Identity.UserRole.Manager;
    public string UserName { get; } = "Demo Cashier";
    public string StaffCode { get; } = "DEMO";
}
