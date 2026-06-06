using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Pos.Api.Contracts;
using Pos.Application.Payments;
using Pos.Domain.Catalog;
using Pos.Domain.Sales;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// End-to-end M-Pesa flow over HTTP against the fake provider (no network, no Daraja creds):
/// initiate → pending → poll/callback → confirmed/failed, plus callback idempotency and a split
/// cash+M-Pesa payment. Proves the sale only finalizes (and writes stock) once the async tender
/// confirms.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class MpesaFlowTests(PosApiFixture fx)
{
    [Fact]
    public async Task Initiate_creates_an_open_sale_with_a_pending_mpesa_tender()
    {
        fx.Mpesa.Reset();
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, 100m);

        var init = await Initiate(client, product.Id, mpesaAmount: 100m);
        init.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = (await init.Content.ReadFromJsonAsync<MpesaInitiateResponse>(PosApiFixture.Json))!;
        body.Status.Should().Be("Pending");
        body.CheckoutRequestId.Should().NotBeNullOrEmpty();
        fx.Mpesa.LastPush.Should().NotBeNull();

        // The sale exists, is still Open, and its M-Pesa tender is Pending (not counted as paid).
        var sale = await GetSale(client, body.SaleId);
        sale.Status.Should().Be(SaleStatus.Open);
        sale.Paid.Amount.Should().Be(0m);
        sale.Tenders.Should().ContainSingle(t => t.Type == TenderType.Mpesa && t.Status == TenderStatus.Pending);
    }

    [Fact]
    public async Task Polling_a_successful_push_confirms_and_completes_the_sale()
    {
        fx.Mpesa.Reset();
        fx.Mpesa.QueryState = MpesaQueryState.Success;
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, 100m);

        var body = await InitiatePending(client, product.Id, 100m);

        var statusResp = await client.GetAsync($"/api/v1/sales/mpesa/{body.SaleId}/status");
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = (await statusResp.Content.ReadFromJsonAsync<MpesaStatusResponse>(PosApiFixture.Json))!;

        status.PaymentStatus.Should().Be("Confirmed");
        status.SaleStatus.Should().Be(nameof(SaleStatus.Completed));
        status.Receipt.Should().Be("FAKE12RECEIPT");

        var sale = await GetSale(client, body.SaleId);
        sale.Status.Should().Be(SaleStatus.Completed);
        sale.Tenders.Single().Status.Should().Be(TenderStatus.Confirmed);

        // Completion fanned out exactly one -1 stock movement.
        var onHand = await OnHand(client, product.Id);
        onHand.Should().Be(-1m);
    }

    [Fact]
    public async Task A_rejected_push_marks_the_tender_failed_and_leaves_the_sale_open()
    {
        fx.Mpesa.Reset();
        fx.Mpesa.PushShouldFail = true;
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, 100m);

        var init = await Initiate(client, product.Id, 100m);
        init.StatusCode.Should().Be(HttpStatusCode.OK, "a rejected push is a business outcome, not a transport error");
        var body = (await init.Content.ReadFromJsonAsync<MpesaInitiateResponse>(PosApiFixture.Json))!;
        body.Status.Should().Be("Failed");

        var sale = await GetSale(client, body.SaleId);
        sale.Status.Should().Be(SaleStatus.Open);
        sale.Tenders.Single().Status.Should().Be(TenderStatus.Failed);
        (await OnHand(client, product.Id)).Should().Be(0m, "nothing sold yet");
    }

    [Fact]
    public async Task A_cancelled_payment_polls_to_failed_and_does_not_complete()
    {
        fx.Mpesa.Reset();
        fx.Mpesa.QueryState = MpesaQueryState.Failed;
        fx.Mpesa.QueryResultCode = 1032;
        fx.Mpesa.QueryResultDesc = "Request cancelled by user";
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, 100m);

        var body = await InitiatePending(client, product.Id, 100m);

        var status = (await (await client.GetAsync($"/api/v1/sales/mpesa/{body.SaleId}/status"))
            .Content.ReadFromJsonAsync<MpesaStatusResponse>(PosApiFixture.Json))!;
        status.PaymentStatus.Should().Be("Failed");
        status.SaleStatus.Should().Be(nameof(SaleStatus.Open));

        (await GetSale(client, body.SaleId)).Tenders.Single().Status.Should().Be(TenderStatus.Failed);
        (await OnHand(client, product.Id)).Should().Be(0m);
    }

    [Fact]
    public async Task Callback_confirms_the_sale_and_is_idempotent_on_replay()
    {
        fx.Mpesa.Reset();
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, 100m);
        var body = await InitiatePending(client, product.Id, 100m);

        var callback = DarajaCallback(body.CheckoutRequestId!, resultCode: 0, amount: 100m, receipt: "QGOOD123");

        // The callback route is unauthenticated (Daraja can't send our headers).
        var anon = fx.Factory.CreateClient();
        var first = await anon.PostAsJsonAsync("/mpesa/callback", callback, PosApiFixture.Json);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var sale = await GetSale(client, body.SaleId);
        sale.Status.Should().Be(SaleStatus.Completed);
        sale.Tenders.Single().Reference.Should().Be("QGOOD123");
        (await OnHand(client, product.Id)).Should().Be(-1m);

        // Replay the exact same callback — must NOT double-confirm or write a second movement.
        var second = await anon.PostAsJsonAsync("/mpesa/callback", callback, PosApiFixture.Json);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await OnHand(client, product.Id)).Should().Be(-1m, "idempotent: the replay changed nothing");
    }

    [Fact]
    public async Task Split_cash_and_mpesa_completes_when_the_mpesa_leg_confirms()
    {
        fx.Mpesa.Reset();
        fx.Mpesa.QueryState = MpesaQueryState.Success;
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, 100m); // 2 × 100 = 200

        var req = new MpesaCheckoutRequest(
            RegisterId: Uuid7.NewGuid(),
            Lines: new[] { new CheckoutLineRequest(product.Id, 2m) },
            MpesaAmount: 150m,
            PhoneNumber: "0712345678",
            AccountReference: null,
            CashTenders: new[] { new CheckoutTenderRequest(TenderType.Cash, 50m, null) },
            Currency: "KES");
        var body = (await (await client.PostAsJsonAsync("/api/v1/sales/mpesa/checkout", req, PosApiFixture.Json))
            .Content.ReadFromJsonAsync<MpesaInitiateResponse>(PosApiFixture.Json))!;
        body.Status.Should().Be("Pending");

        var status = (await (await client.GetAsync($"/api/v1/sales/mpesa/{body.SaleId}/status"))
            .Content.ReadFromJsonAsync<MpesaStatusResponse>(PosApiFixture.Json))!;
        status.PaymentStatus.Should().Be("Confirmed");
        status.SaleStatus.Should().Be(nameof(SaleStatus.Completed));

        var sale = await GetSale(client, body.SaleId);
        sale.Subtotal.Amount.Should().Be(200m);
        sale.Paid.Amount.Should().Be(200m, "cash 50 + confirmed M-Pesa 150");
        sale.Tenders.Should().HaveCount(2);
        (await OnHand(client, product.Id)).Should().Be(-2m);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────
    private static async Task<ProductResponse> CreateProduct(HttpClient client, decimal price)
    {
        var sku = $"MP-{Guid.NewGuid():N}"[..12];
        var resp = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            Sku: sku, Name: $"Test {sku}", PriceAmount: price, PriceCurrency: "KES",
            UnitOfMeasure: UnitOfMeasure.Each), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
    }

    private static Task<HttpResponseMessage> Initiate(HttpClient client, Guid productId, decimal mpesaAmount) =>
        client.PostAsJsonAsync("/api/v1/sales/mpesa/checkout", new MpesaCheckoutRequest(
            RegisterId: Uuid7.NewGuid(),
            Lines: new[] { new CheckoutLineRequest(productId, 1m) },
            MpesaAmount: mpesaAmount,
            PhoneNumber: "0712345678"), PosApiFixture.Json);

    private static async Task<MpesaInitiateResponse> InitiatePending(HttpClient client, Guid productId, decimal mpesaAmount)
    {
        var resp = await Initiate(client, productId, mpesaAmount);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = (await resp.Content.ReadFromJsonAsync<MpesaInitiateResponse>(PosApiFixture.Json))!;
        body.Status.Should().Be("Pending");
        return body;
    }

    private static async Task<SaleResponse> GetSale(HttpClient client, Guid saleId) =>
        (await (await client.GetAsync($"/api/v1/sales/{saleId}"))
            .Content.ReadFromJsonAsync<SaleResponse>(PosApiFixture.Json))!;

    private static async Task<decimal> OnHand(HttpClient client, Guid productId) =>
        (await (await client.GetAsync($"/api/v1/inventory/{productId}/on-hand"))
            .Content.ReadFromJsonAsync<StockOnHandResponse>(PosApiFixture.Json))!.OnHand;

    private static object DarajaCallback(string checkoutRequestId, int resultCode, decimal amount, string receipt) => new
    {
        Body = new
        {
            stkCallback = new
            {
                MerchantRequestID = "mr-test",
                CheckoutRequestID = checkoutRequestId,
                ResultCode = resultCode,
                ResultDesc = "The service request is processed successfully.",
                CallbackMetadata = new
                {
                    Item = new object[]
                    {
                        new { Name = "Amount", Value = amount },
                        new { Name = "MpesaReceiptNumber", Value = receipt },
                        new { Name = "PhoneNumber", Value = "254712345678" }
                    }
                }
            }
        }
    };
}
