using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pos.Api.Auth;
using Pos.Api.Contracts;
using Pos.Application.Fiscalization;
using Pos.Domain.Catalog;
using Pos.Domain.Sales;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// The eTIMS fiscalization seam (fake/training provider). Enabled: a completed sale is signed
/// (CUIN + QR persisted), the receipt renders the fiscal block, reprints don't re-sign, and the
/// sync worker flips it to Synced. Disabled: the sale is NotRequired and the receipt shows the
/// non-fiscal note.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class EtimsFiscalizationTests(PosApiFixture fx)
{
    [Fact]
    public async Task Enabled_signs_the_sale_persists_cuin_and_qr_and_reprints_without_resigning()
    {
        var (client, _, _, _) = fx.NewClient();
        var product = await CreateProduct(client, 100m);
        var saleId = await CashCheckout(client, product.Id);

        var receipt = await GetReceipt(client, saleId);
        receipt.Model.Fiscal.Status.Should().Be("Signed");
        receipt.Model.Fiscal.Fiscalized.Should().BeTrue();
        receipt.Model.Fiscal.Cuin.Should().StartWith("TEST-");                 // clearly fake
        receipt.Model.Fiscal.Cuin.Should().Contain(receipt.Model.Meta.ReceiptNo); // deterministic from receipt no
        receipt.Model.Fiscal.QrData.Should().Contain("kra.go.ke").And.Contain(Uri.EscapeDataString(receipt.Model.Fiscal.Cuin!));
        receipt.Text.Should().Contain("eTIMS FISCAL RECEIPT").And.Contain(receipt.Model.Fiscal.Cuin!);
        receipt.Html.Should().Contain(receipt.Model.Fiscal.QrData!);

        // Reprint is byte-identical → no re-sign (a re-sign would restamp SignedAt and change the text).
        var reprint = await GetReceipt(client, saleId);
        reprint.Text.Should().Be(receipt.Text);
        reprint.Model.Fiscal.Cuin.Should().Be(receipt.Model.Fiscal.Cuin);

        // The background sync seam transmits Signed sales → Synced.
        using (var scope = fx.Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<FiscalSyncService>().RunOnceAsync();
        }
        var afterSync = await GetReceipt(client, saleId);
        afterSync.Model.Fiscal.Status.Should().Be("Synced");
        afterSync.Model.Fiscal.SyncedAtEat.Should().NotBeNullOrEmpty();
        afterSync.Model.Fiscal.Cuin.Should().Be(receipt.Model.Fiscal.Cuin, "sync transmits, it doesn't re-sign");
    }

    [Fact]
    public async Task Disabled_completes_as_not_required_and_receipt_shows_the_non_fiscal_note()
    {
        // A tenant provisioned with eTIMS turned OFF (per-tenant setting, not appsettings).
        var tenant = Uuid7.NewGuid();
        var store = Uuid7.NewGuid();
        fx.Provision(tenant, store, r => r with { EtimsEnabled = false });
        var client = ClientFor(tenant, store);

        var product = await CreateProduct(client, 100m);
        var saleId = await CashCheckout(client, product.Id);

        var receipt = await GetReceipt(client, saleId);
        receipt.Model.Fiscal.Status.Should().Be("NotRequired");
        receipt.Model.Fiscal.Fiscalized.Should().BeFalse();
        receipt.Model.Fiscal.Cuin.Should().BeNull();
        receipt.Text.Should().Contain("NON-FISCAL / TRAINING");
        receipt.Text.Should().NotContain("eTIMS FISCAL RECEIPT");
    }

    // ── helpers ──
    private static async Task<Guid> CashCheckout(HttpClient client, Guid productId)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/sales/checkout", new CheckoutRequest(
            RegisterId: Uuid7.NewGuid(),
            Lines: new[] { new CheckoutLineRequest(productId, 1m) },
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Cash, 100m, null) }), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CompleteSaleResponse>(PosApiFixture.Json))!.SaleId;
    }

    private static async Task<ReceiptResponse> GetReceipt(HttpClient client, Guid saleId) =>
        (await (await client.GetAsync($"/api/v1/sales/{saleId}/receipt"))
            .Content.ReadFromJsonAsync<ReceiptResponse>(PosApiFixture.Json))!;

    private static async Task<ProductResponse> CreateProduct(HttpClient client, decimal price)
    {
        var sku = $"ET-{Guid.NewGuid():N}"[..12];
        var resp = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            Sku: sku, Name: $"Item {sku}", PriceAmount: price, PriceCurrency: "KES",
            UnitOfMeasure: UnitOfMeasure.Each, Barcode: null, TaxClass: TaxClass.StandardRated), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
    }

    private HttpClient ClientFor(Guid tenant, Guid store)
    {
        var client = fx.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthMiddleware.TenantHeader, tenant.ToString());
        client.DefaultRequestHeaders.Add(DevHeaderAuthMiddleware.StoreHeader, store.ToString());
        client.DefaultRequestHeaders.Add(DevHeaderAuthMiddleware.UserHeader, Uuid7.NewGuid().ToString());
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
