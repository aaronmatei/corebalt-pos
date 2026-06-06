using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Pos.Api.Contracts;
using Pos.Domain.Catalog;
using Pos.Domain.Identity;
using Pos.Domain.Sales;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// Authentication & authorization: PIN login issues a usable token (bad PIN rejected), a checkout
/// records the authenticated cashier on the receipt, and role policies gate the back office.
/// These use REAL JWTs (not the dev-header bypass) against the bootstrap-seeded manager.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class AuthTests(PosApiFixture fx)
{
    private const string BootstrapUser = "manager";
    private const string BootstrapPassword = "ChangeMe!123";

    [Fact]
    public async Task Pin_login_returns_a_token_and_a_bad_pin_is_rejected()
    {
        var manager = await ManagerTokenAsync();
        var (staff, pin) = await CreateCashierAsync(manager, "Jane Cashier");

        var ok = await Anon().PostAsJsonAsync("/api/v1/auth/pin-login", new PinLoginRequest(staff, pin), PosApiFixture.Json);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await ok.Content.ReadFromJsonAsync<TokenResponse>(PosApiFixture.Json))!;
        token.AccessToken.Should().NotBeNullOrWhiteSpace();

        var bad = await Anon().PostAsJsonAsync("/api/v1/auth/pin-login", new PinLoginRequest(staff, "9999"), PosApiFixture.Json);
        bad.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Checkout_records_the_authenticated_cashier_on_the_receipt()
    {
        var manager = await ManagerTokenAsync();
        var (staff, pin) = await CreateCashierAsync(manager, "Otieno Mwangi");
        var cashier = await PinTokenAsync(staff, pin);

        var product = await CreateProductAsync(manager, 100m);          // Manager creates the catalogue
        var checkout = await Bearer(cashier).PostAsJsonAsync("/api/v1/sales/checkout", new CheckoutRequest(
            RegisterId: Uuid7.NewGuid(),
            Lines: new[] { new CheckoutLineRequest(product.Id, 1m) },
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Cash, 200m, null) }), PosApiFixture.Json);
        checkout.EnsureSuccessStatusCode();
        var saleId = (await checkout.Content.ReadFromJsonAsync<CompleteSaleResponse>(PosApiFixture.Json))!.SaleId;

        var receipt = (await (await Bearer(cashier).GetAsync($"/api/v1/sales/{saleId}/receipt"))
            .Content.ReadFromJsonAsync<ReceiptResponse>(PosApiFixture.Json))!;
        receipt.Model.Meta.Cashier.Should().Contain("Otieno Mwangi").And.Contain(staff);
        receipt.Text.Should().Contain("Otieno Mwangi");
    }

    [Fact]
    public async Task Back_office_endpoint_rejects_a_cashier_but_accepts_a_manager()
    {
        var manager = await ManagerTokenAsync();
        var (staff, pin) = await CreateCashierAsync(manager, "Limited Cashier");
        var cashier = await PinTokenAsync(staff, pin);

        var body = new CreateProductRequest(
            Sku: $"AU-{Guid.NewGuid():N}"[..12], Name: "Gated Item", PriceAmount: 10m, PriceCurrency: "KES",
            UnitOfMeasure: UnitOfMeasure.Each, Barcode: null, TaxClass: TaxClass.StandardRated);

        var asCashier = await Bearer(cashier).PostAsJsonAsync("/api/v1/products", body, PosApiFixture.Json);
        asCashier.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var asManager = await Bearer(manager).PostAsJsonAsync("/api/v1/products",
            body with { Sku = $"AU-{Guid.NewGuid():N}"[..12] }, PosApiFixture.Json);
        asManager.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── helpers ──
    private HttpClient Anon() => fx.Factory.CreateClient();

    private HttpClient Bearer(string token)
    {
        var c = fx.Factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return c;
    }

    private async Task<string> ManagerTokenAsync()
    {
        var resp = await Anon().PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(BootstrapUser, BootstrapPassword), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TokenResponse>(PosApiFixture.Json))!.AccessToken;
    }

    private async Task<string> PinTokenAsync(string staff, string pin)
    {
        var resp = await Anon().PostAsJsonAsync("/api/v1/auth/pin-login", new PinLoginRequest(staff, pin), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TokenResponse>(PosApiFixture.Json))!.AccessToken;
    }

    private async Task<(string Staff, string Pin)> CreateCashierAsync(string managerToken, string name)
    {
        var staff = $"C{Guid.NewGuid():N}"[..8]; // v4 (random) — Uuid7's time prefix would collide
        const string pin = "1234";
        var resp = await Bearer(managerToken).PostAsJsonAsync("/api/v1/users", new CreateUserRequest(
            Name: name, Username: $"u-{Guid.NewGuid():N}"[..12], StaffCode: staff, Role: UserRole.Cashier,
            Pin: pin, Password: null), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (staff, pin);
    }

    private async Task<ProductResponse> CreateProductAsync(string managerToken, decimal price)
    {
        var resp = await Bearer(managerToken).PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            Sku: $"AU-{Guid.NewGuid():N}"[..12], Name: "Auth Item", PriceAmount: price, PriceCurrency: "KES",
            UnitOfMeasure: UnitOfMeasure.Each, Barcode: null, TaxClass: TaxClass.StandardRated), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
    }
}
