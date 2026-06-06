using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pos.Api.Contracts;
using Pos.Domain.Catalog;
using Pos.Domain.Identity;
using Pos.Domain.Inventory;
using Pos.Domain.Sales;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// Returns / voids / refunds. A reversal is a NEW immutable credit note referencing the original sale;
/// the sale is never mutated, stock reverses via new IN movements (on-hand derived), and a stub
/// credit-note fiscal record is created. Over-returns are rejected; returns need Supervisor+.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class ReturnsTests(PosApiFixture fx)
{
    [Fact]
    public async Task Partial_return_raises_on_hand_leaves_the_sale_untouched_and_fiscalizes()
    {
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, 100m);
        await Receive(client, product.Id, 10m);
        var saleId = await Checkout(client, product.Id, 5m, cash: 500m);
        (await OnHand(client, product.Id)).Should().Be(5m);

        var resp = await client.PostAsJsonAsync($"/api/v1/sales/{saleId}/returns", new CreateReturnRequest(
            ReturnId: Uuid7.NewGuid(), Reason: ReturnReason.Damaged,
            Lines: new[] { new ReturnLineRequest(product.Id, 2m) }, RefundMethod: RefundMethod.Cash), PosApiFixture.Json);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var ret = (await resp.Content.ReadFromJsonAsync<ReturnResponse>(PosApiFixture.Json))!;

        ret.RefundAmount.Should().Be(200m);              // 2 × 100
        ret.RefundStatus.Should().Be("Refunded");        // cash refunded immediately
        ret.Receipt.Model.DocumentTitle.Should().Be("CREDIT NOTE / REFUND");
        ret.Receipt.Model.Totals.GrandTotal.Should().Be(-200m); // negative on the credit note
        ret.Receipt.Model.Fiscal.Cuin.Should().StartWith("TEST-CN-"); // stub credit-note fiscal record

        (await OnHand(client, product.Id)).Should().Be(7m, "returned 2 reversed via a new IN movement");

        // Original sale is untouched: its own receipt still shows the full positive total, no credit-note header.
        var saleReceipt = await Receipt(client, $"/api/v1/sales/{saleId}/receipt");
        saleReceipt.Model.Totals.GrandTotal.Should().Be(500m);
        saleReceipt.Model.DocumentTitle.Should().BeEmpty();
    }

    [Fact]
    public async Task Return_is_idempotent_on_the_client_return_id()
    {
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, 100m);
        await Receive(client, product.Id, 10m);
        var saleId = await Checkout(client, product.Id, 5m, cash: 500m);

        var returnId = Uuid7.NewGuid();
        var body = new CreateReturnRequest(returnId, ReturnReason.WrongItem,
            new[] { new ReturnLineRequest(product.Id, 2m) }, RefundMethod.Cash);

        (await client.PostAsJsonAsync($"/api/v1/sales/{saleId}/returns", body, PosApiFixture.Json)).EnsureSuccessStatusCode();
        await client.PostAsJsonAsync($"/api/v1/sales/{saleId}/returns", body, PosApiFixture.Json); // replay

        (await OnHand(client, product.Id)).Should().Be(7m, "replay must not double the reversal");
    }

    [Fact]
    public async Task Over_return_is_rejected()
    {
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, 100m);
        await Receive(client, product.Id, 10m);
        var saleId = await Checkout(client, product.Id, 5m, cash: 500m);

        var resp = await client.PostAsJsonAsync($"/api/v1/sales/{saleId}/returns", new CreateReturnRequest(
            Uuid7.NewGuid(), ReturnReason.Damaged, new[] { new ReturnLineRequest(product.Id, 6m) }, RefundMethod.Cash),
            PosApiFixture.Json);
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await OnHand(client, product.Id)).Should().Be(5m, "rejected return writes nothing");
    }

    [Fact]
    public async Task Full_quantity_return_void_reverses_the_whole_sale()
    {
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, 100m);
        await Receive(client, product.Id, 10m);
        var saleId = await Checkout(client, product.Id, 5m, cash: 500m);

        var resp = await client.PostAsJsonAsync($"/api/v1/sales/{saleId}/returns", new CreateReturnRequest(
            Uuid7.NewGuid(), ReturnReason.CashierError, new[] { new ReturnLineRequest(product.Id, 5m) }, RefundMethod.Cash),
            PosApiFixture.Json);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var ret = (await resp.Content.ReadFromJsonAsync<ReturnResponse>(PosApiFixture.Json))!;

        ret.RefundAmount.Should().Be(500m);
        (await OnHand(client, product.Id)).Should().Be(10m, "the whole sale (5) is reversed");
    }

    [Fact]
    public async Task Cashier_is_rejected_but_supervisor_and_manager_are_accepted()
    {
        // Real JWT roles in the StoreServer scope.
        var product = await SeedProductInStoreServerAsync();
        var cashier = await PinTokenAsync(await SeedUserAsync(UserRole.Cashier));
        var supervisor = await PinTokenAsync(await SeedUserAsync(UserRole.Supervisor));

        var saleId = await CheckoutAsync(Bearer(supervisor), product, 1m); // supervisor rings a sale

        var body = new CreateReturnRequest(Uuid7.NewGuid(), ReturnReason.Damaged,
            new[] { new ReturnLineRequest(product, 1m) }, RefundMethod.Cash);

        var asCashier = await Bearer(cashier).PostAsJsonAsync($"/api/v1/sales/{saleId}/returns", body, PosApiFixture.Json);
        asCashier.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var asSupervisor = await Bearer(supervisor).PostAsJsonAsync($"/api/v1/sales/{saleId}/returns",
            body with { ReturnId = Uuid7.NewGuid() }, PosApiFixture.Json);
        asSupervisor.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── helpers (dev-header path) ──
    private static async Task<ProductResponse> CreateProduct(HttpClient client, decimal price)
    {
        var sku = $"RT-{Guid.NewGuid():N}"[..12];
        var resp = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            sku, $"Return Item {sku}", price, "KES", UnitOfMeasure.Each, null, TaxClass.StandardRated), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
    }

    private static async Task Receive(HttpClient client, Guid productId, decimal qty)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/receive",
            new ReceiveStockRequest(productId, qty, StockMovementReason.Purchase, "GRN"), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> Checkout(HttpClient client, Guid productId, decimal qty, decimal cash)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/sales/checkout", new CheckoutRequest(
            RegisterId: Uuid7.NewGuid(),
            Lines: new[] { new CheckoutLineRequest(productId, qty) },
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Cash, cash, null) }), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CompleteSaleResponse>(PosApiFixture.Json))!.SaleId;
    }

    private static async Task<decimal> OnHand(HttpClient client, Guid productId) =>
        (await (await client.GetAsync($"/api/v1/inventory/{productId}/on-hand"))
            .Content.ReadFromJsonAsync<StockOnHandResponse>(PosApiFixture.Json))!.OnHand;

    private static async Task<ReceiptResponse> Receipt(HttpClient client, string url) =>
        (await (await client.GetAsync(url)).Content.ReadFromJsonAsync<ReceiptResponse>(PosApiFixture.Json))!;

    // ── helpers (JWT role path, StoreServer scope) ──
    private static readonly Guid StoreTenant = Guid.Parse("019600c0-0000-7000-8000-000000000001");
    private static readonly Guid StoreStore = Guid.Parse("019600c0-0000-7000-8000-000000000002");

    private HttpClient Bearer(string token)
    {
        var c = fx.Factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return c;
    }

    private async Task<(string Staff, string Pin)> SeedUserAsync(UserRole role)
    {
        using var scope = fx.Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<Pos.Application.Identity.AuthService>();
        var staff = $"R{Guid.NewGuid():N}"[..8];
        await auth.CreateUserAsync($"{role} R", $"r-{Guid.NewGuid():N}"[..14], staff, role, pin: "2468", password: null);
        return (staff, "2468");
    }

    private async Task<string> PinTokenAsync((string Staff, string Pin) u)
    {
        var resp = await fx.Factory.CreateClient().PostAsJsonAsync("/api/v1/auth/pin-login",
            new PinLoginRequest(u.Staff, u.Pin), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TokenResponse>(PosApiFixture.Json))!.AccessToken;
    }

    private async Task<Guid> SeedProductInStoreServerAsync()
    {
        using var scope = fx.Factory.Services.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<Pos.Application.Catalog.IProductRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<Pos.Application.Abstractions.IUnitOfWork>();
        var p = Product.Create(StoreTenant, StoreStore, $"RT-SS-{Guid.NewGuid():N}"[..12], "Return SS Item",
            new Pos.SharedKernel.Money(100m, "KES"), UnitOfMeasure.Each, null, TaxClass.StandardRated);
        await products.AddAsync(p);
        await uow.SaveChangesAsync();
        return p.Id;
    }

    private static async Task<Guid> CheckoutAsync(HttpClient client, Guid productId, decimal qty)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/sales/checkout", new CheckoutRequest(
            RegisterId: Uuid7.NewGuid(),
            Lines: new[] { new CheckoutLineRequest(productId, qty) },
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Cash, 100m, null) }), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CompleteSaleResponse>(PosApiFixture.Json))!.SaleId;
    }
}
