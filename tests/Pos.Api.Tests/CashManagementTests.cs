using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pos.Api.Contracts;
using Pos.Application.Cash;
using Pos.Domain.Catalog;
using Pos.Domain.Identity;
using Pos.Domain.Sales;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// Cash management + close-of-day. A register SHIFT is the spine; X/Z reports are read-side projections
/// over its immutable facts (sales tenders, VAT, credit notes, cash movements). Expected drawer cash is
/// computed from the formula, never a running counter.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class CashManagementTests(PosApiFixture fx)
{
    [Fact]
    public async Task X_report_totals_sales_by_tender_and_VAT()
    {
        var (client, _, _, _) = fx.NewClient();
        var register = Uuid7.NewGuid();
        var session = await OpenSession(client, register, 1000m);
        var product = await CreateProduct(client, 116m); // 16% incl → net 100, VAT 16

        await CashCheckout(client, register, product.Id, qty: 1, cash: 116m);
        await MpesaCheckout(client, register, product.Id, qty: 1, mpesa: 116m);

        var x = await Report(client, session.Id);
        x.Report.Kind.Should().Be("X");
        x.Report.TransactionCount.Should().Be(2);
        x.Report.GrossSales.Should().Be(232m);

        x.Report.Tenders.Should().ContainSingle(t => t.Type == "Cash").Which.Amount.Should().Be(116m);
        x.Report.Tenders.Should().ContainSingle(t => t.Type == "Mpesa").Which.Amount.Should().Be(116m);

        x.Report.Vat.Should().ContainSingle(v => v.Net == 200m && v.Vat == 32m);

        // Drawer = opening float + cash sales only (M-Pesa never counted).
        x.Report.Cash.OpeningFloat.Should().Be(1000m);
        x.Report.Cash.CashSales.Should().Be(116m);
        x.Report.Cash.Expected.Should().Be(1116m);
        x.Report.Cash.Counted.Should().BeNull("X report counts nothing");
    }

    [Fact]
    public async Task Pay_in_out_drop_and_a_cash_refund_adjust_expected_cash_per_the_formula()
    {
        var (client, _, _, _) = fx.NewClient();
        var register = Uuid7.NewGuid();
        var session = await OpenSession(client, register, 1000m);
        var product = await CreateProduct(client, 100m); // exact, no change

        var saleId = await CashCheckout(client, register, product.Id, qty: 2, cash: 200m); // cash 200
        await MpesaCheckout(client, register, product.Id, qty: 3, mpesa: 300m);            // M-Pesa 300 (not drawer)

        await Movement(client, register, "PayIn", 50m);
        await Movement(client, register, "PayOut", 30m);
        await Movement(client, register, "Drop", 100m);
        await Refund(client, register, saleId, product.Id, qty: 1, RefundMethod.Cash);      // cash refund 100

        var x = await Report(client, session.Id);
        var c = x.Report.Cash;
        c.CashSales.Should().Be(200m);
        c.CashRefunds.Should().Be(100m);
        c.PayIns.Should().Be(50m);
        c.PayOuts.Should().Be(30m);
        c.Drops.Should().Be(100m);
        // 1000 + 200 − 100 + 50 − 30 − 100 = 1020.
        c.Expected.Should().Be(1020m);
        x.Report.Tenders.Should().Contain(t => t.Type == "Mpesa" && t.Amount == 300m, "M-Pesa is reported but not in the drawer");
    }

    [Fact]
    public async Task Close_freezes_expected_and_variance_makes_the_session_immutable_and_a_new_one_is_isolated()
    {
        var (client, _, _, _) = fx.NewClient();
        var register = Uuid7.NewGuid();
        var session = await OpenSession(client, register, 1000m);
        var product = await CreateProduct(client, 100m);
        await CashCheckout(client, register, product.Id, qty: 5, cash: 500m); // expected 1500

        // Count 1400 → short by 100 (within threshold, no ack needed).
        var closeResp = await client.PostAsJsonAsync($"/api/v1/sessions/{session.Id}/close",
            new CloseSessionRequest(CountedCash: 1400m, Acknowledged: false), PosApiFixture.Json);
        closeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var z = (await closeResp.Content.ReadFromJsonAsync<ShiftReportResponse>(PosApiFixture.Json))!;
        z.Report.Kind.Should().Be("Z");
        z.Report.Cash.Expected.Should().Be(1500m);
        z.Report.Cash.Counted.Should().Be(1400m);
        z.Report.Cash.Variance.Should().Be(-100m);

        // Immutable: closing again is rejected.
        (await client.PostAsJsonAsync($"/api/v1/sessions/{session.Id}/close",
            new CloseSessionRequest(1400m, false), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // A new session on the same register starts clean and aggregates only its own facts.
        var session2 = await OpenSession(client, register, 800m);
        await CashCheckout(client, register, product.Id, qty: 1, cash: 100m);
        var x2 = await Report(client, session2.Id);
        x2.Report.GrossSales.Should().Be(100m, "only the new session's sale");
        x2.Report.Cash.Expected.Should().Be(900m, "new float 800 + cash 100");
    }

    [Fact]
    public async Task Cannot_checkout_without_an_open_session()
    {
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, 100m);

        var resp = await client.PostAsJsonAsync("/api/v1/sales/checkout", new CheckoutRequest(
            RegisterId: Uuid7.NewGuid(), // no shift opened
            Lines: new[] { new CheckoutLineRequest(product.Id, 1m) },
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Cash, 100m, null) }), PosApiFixture.Json);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict); // "No open register session"
    }

    [Fact]
    public async Task Authorization_cashier_opens_and_closes_payout_needs_supervisor_large_variance_needs_manager()
    {
        var cashier = await PinTokenAsync(await SeedUserAsync(UserRole.Cashier));
        var supervisor = await PinTokenAsync(await SeedUserAsync(UserRole.Supervisor));
        var manager = await PinTokenAsync(await SeedUserAsync(UserRole.Manager));

        // Cashier opens AND closes their OWN session (no sales → expected = float, count it exactly).
        var reg1 = Uuid7.NewGuid();
        var s1 = await OpenSession(Bearer(cashier), reg1, 1000m);
        (await Bearer(cashier).PostAsJsonAsync($"/api/v1/sessions/{s1.Id}/close",
            new CloseSessionRequest(1000m, false), PosApiFixture.Json)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Pay-out: Cashier forbidden, Supervisor allowed.
        var reg2 = Uuid7.NewGuid();
        await OpenSession(Bearer(supervisor), reg2, 1000m);
        (await Bearer(cashier).PostAsJsonAsync("/api/v1/sessions/movements",
            new CashMovementRequest(reg2, "PayOut", 50m, "petty cash"), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Bearer(supervisor).PostAsJsonAsync("/api/v1/sessions/movements",
            new CashMovementRequest(reg2, "PayOut", 50m, "petty cash"), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // Large variance: a Cashier close is blocked; a Manager acknowledges it.
        var reg3 = Uuid7.NewGuid();
        var s3 = await OpenSession(Bearer(cashier), reg3, 1000m); // expected 1000
        (await Bearer(cashier).PostAsJsonAsync($"/api/v1/sessions/{s3.Id}/close",
            new CloseSessionRequest(5000m, false), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "variance 4000 > threshold needs a manager");
        (await Bearer(manager).PostAsJsonAsync($"/api/v1/sessions/{s3.Id}/close",
            new CloseSessionRequest(5000m, true), PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.OK, "manager acknowledges the large variance");
    }

    // ── helpers ──
    private static async Task<SessionResponse> OpenSession(HttpClient client, Guid register, decimal openingFloat)
    {
        var r = await client.PostAsJsonAsync("/api/v1/sessions/open", new OpenSessionRequest(register, openingFloat), PosApiFixture.Json);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<SessionResponse>(PosApiFixture.Json))!;
    }

    private static async Task<ShiftReportResponse> Report(HttpClient client, Guid sessionId) =>
        (await (await client.GetAsync($"/api/v1/sessions/{sessionId}/report"))
            .Content.ReadFromJsonAsync<ShiftReportResponse>(PosApiFixture.Json))!;

    private static async Task Movement(HttpClient client, Guid register, string type, decimal amount)
    {
        var r = await client.PostAsJsonAsync("/api/v1/sessions/movements",
            new CashMovementRequest(register, type, amount, "test"), PosApiFixture.Json);
        r.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CashCheckout(HttpClient client, Guid register, Guid productId, decimal qty, decimal cash)
    {
        var r = await client.PostAsJsonAsync("/api/v1/sales/checkout", new CheckoutRequest(
            RegisterId: register,
            Lines: new[] { new CheckoutLineRequest(productId, qty) },
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Cash, cash, null) }), PosApiFixture.Json);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<CompleteSaleResponse>(PosApiFixture.Json))!.SaleId;
    }

    private static async Task MpesaCheckout(HttpClient client, Guid register, Guid productId, decimal qty, decimal mpesa)
    {
        var r = await client.PostAsJsonAsync("/api/v1/sales/checkout", new CheckoutRequest(
            RegisterId: register,
            Lines: new[] { new CheckoutLineRequest(productId, qty) },
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Mpesa, mpesa, "SGR" + Guid.NewGuid().ToString("N")[..6]) }), PosApiFixture.Json);
        r.EnsureSuccessStatusCode();
    }

    private static async Task Refund(HttpClient client, Guid register, Guid saleId, Guid productId, decimal qty, RefundMethod method)
    {
        var r = await client.PostAsJsonAsync($"/api/v1/sales/{saleId}/returns", new CreateReturnRequest(
            ReturnId: Uuid7.NewGuid(), RegisterId: register, Reason: ReturnReason.Damaged,
            Lines: new[] { new ReturnLineRequest(productId, qty) }, RefundMethod: method), PosApiFixture.Json);
        r.EnsureSuccessStatusCode();
    }

    private static async Task<ProductResponse> CreateProduct(HttpClient client, decimal price)
    {
        var sku = $"CS-{Guid.NewGuid():N}"[..12];
        var r = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            sku, $"Item {sku}", price, "KES", UnitOfMeasure.Each, null, TaxClass.StandardRated), PosApiFixture.Json);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
    }

    // ── JWT role helpers (StoreServer scope) ──
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
        var staff = $"C{Guid.NewGuid():N}"[..8];
        await auth.CreateUserAsync($"{role} C", $"c-{Guid.NewGuid():N}"[..14], staff, role, pin: "1357", password: null);
        return (staff, "1357");
    }

    private async Task<string> PinTokenAsync((string Staff, string Pin) u)
    {
        var resp = await fx.Factory.CreateClient().PostAsJsonAsync("/api/v1/auth/pin-login",
            new PinLoginRequest(u.Staff, u.Pin), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TokenResponse>(PosApiFixture.Json))!.AccessToken;
    }
}
