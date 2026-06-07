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
/// The receipt is a deterministic projection over the completed, persisted sale. One sale with a
/// standard-rated item, a zero-rated item and a weighed item, paid by M-Pesa: VAT is backed out and
/// STORED per class, the grand total reconciles, the fiscal block shows the PENDING stub, and a
/// second fetch renders byte-identically.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class ReceiptTests(PosApiFixture fx)
{
    [Fact]
    public async Task Receipt_projects_stored_vat_and_is_deterministic()
    {
        fx.Mpesa.Reset();
        fx.Mpesa.QueryState = MpesaQueryState.Success;
        var (client, _, _, _) = fx.NewClient();

        // 116 incl VAT (standard) → vat 16.00, taxable 100.00
        var standard = await CreateProduct(client, "Cooking Oil 1L", 116m, UnitOfMeasure.Each, TaxClass.StandardRated);
        // 110 (zero-rated bread) → vat 0, taxable 110
        var zero = await CreateProduct(client, "Bread 400g", 110m, UnitOfMeasure.Each, TaxClass.ZeroRated);
        // 200/kg weighed, standard → 1.250kg = 250.00 incl; vat 34.48, taxable 215.52
        var weighed = await CreateProduct(client, "Beef per kg", 200m, UnitOfMeasure.Kg, TaxClass.StandardRated);

        // Pay the lot by M-Pesa (476.00) via the async flow + fake confirmation.
        var register = await client.OpenShiftAsync();
        var req = new MpesaCheckoutRequest(
            RegisterId: register,
            Lines: new[]
            {
                new CheckoutLineRequest(standard.Id, 1m),
                new CheckoutLineRequest(zero.Id, 1m),
                new CheckoutLineRequest(weighed.Id, 1.250m),
            },
            MpesaAmount: 476m,
            PhoneNumber: "0712345678");
        var init = (await (await client.PostAsJsonAsync("/api/v1/sales/mpesa/checkout", req, PosApiFixture.Json))
            .Content.ReadFromJsonAsync<MpesaInitiateResponse>(PosApiFixture.Json))!;
        init.Status.Should().Be("Pending");

        var status = (await (await client.GetAsync($"/api/v1/sales/mpesa/{init.SaleId}/status"))
            .Content.ReadFromJsonAsync<MpesaStatusResponse>(PosApiFixture.Json))!;
        status.SaleStatus.Should().Be(nameof(SaleStatus.Completed));

        // ── The receipt ──
        var resp = await client.GetAsync($"/api/v1/sales/{init.SaleId}/receipt");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var receipt = (await resp.Content.ReadFromJsonAsync<ReceiptResponse>(PosApiFixture.Json))!;
        var model = receipt.Model;

        // VAT backed out per class (stored, not recomputed)
        var std = model.Vat.Single(v => v.TaxCode == "A");   // StandardRated
        std.Taxable.Should().Be(315.52m); // 100.00 + 215.52
        std.Vat.Should().Be(50.48m);      // 16.00 + 34.48
        var zr = model.Vat.Single(v => v.TaxCode == "B");    // ZeroRated
        zr.Taxable.Should().Be(110m);
        zr.Vat.Should().Be(0m);

        // Totals reconcile: net + VAT = grand total
        model.Totals.Subtotal.Should().Be(425.52m);
        model.Totals.TotalVat.Should().Be(50.48m);
        model.Totals.GrandTotal.Should().Be(476m);
        (model.Totals.Subtotal + model.Totals.TotalVat).Should().Be(model.Totals.GrandTotal);

        // M-Pesa tender with its receipt reference; no change
        model.Tenders.Should().ContainSingle(t => t.Type == nameof(TenderType.Mpesa) && t.Reference == "FAKE12RECEIPT");
        model.Change.Should().Be(0m);

        // Fiscal block: eTIMS is enabled in the test host → the sale is signed by the fake provider.
        model.Fiscal.Fiscalized.Should().BeTrue();
        model.Fiscal.Status.Should().Be("Signed");
        model.Fiscal.Cuin.Should().StartWith("TEST-");

        // Rendered text: weighed line, formatted money, fiscal block with the CUIN
        receipt.Text.Should().Contain("1.250 kg @ 200.00");
        receipt.Text.Should().Contain("GRAND TOTAL");
        receipt.Text.Should().Contain("476.00");
        receipt.Text.Should().Contain("eTIMS FISCAL RECEIPT");
        receipt.Text.Should().Contain(model.Fiscal.Cuin!);
        receipt.Html.Should().Contain(model.Fiscal.Cuin!);

        // ── Reprint is byte-identical ──
        var again = (await (await client.GetAsync($"/api/v1/sales/{init.SaleId}/receipt"))
            .Content.ReadFromJsonAsync<ReceiptResponse>(PosApiFixture.Json))!;
        again.Text.Should().Be(receipt.Text);
        again.Html.Should().Be(receipt.Html);
    }

    [Fact]
    public async Task Receipt_supports_58mm_width()
    {
        fx.Mpesa.Reset();
        var (client, _, _, _) = fx.NewClient();
        var p = await CreateProduct(client, "Soda 500ml", 80m, UnitOfMeasure.Each, TaxClass.StandardRated);

        var register = await client.OpenShiftAsync();
        var checkoutResp = await client.PostAsJsonAsync("/api/v1/sales/checkout", new CheckoutRequest(
            RegisterId: register,
            Lines: new[] { new CheckoutLineRequest(p.Id, 1m) },
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Cash, 100m, null) }), PosApiFixture.Json);
        var sale = (await checkoutResp.Content.ReadFromJsonAsync<CompleteSaleResponse>(PosApiFixture.Json))!;

        var r = (await (await client.GetAsync($"/api/v1/sales/{sale.SaleId}/receipt?cols=32"))
            .Content.ReadFromJsonAsync<ReceiptResponse>(PosApiFixture.Json))!;
        r.Columns.Should().Be(32);
        // Every rendered line fits the 58mm width.
        r.Text.Split('\n').Should().OnlyContain(line => line.Length <= 32);
        r.Model.Change.Should().Be(20m); // 100 cash - 80
    }

    private static async Task<ProductResponse> CreateProduct(
        HttpClient client, string name, decimal price, UnitOfMeasure unit, TaxClass taxClass)
    {
        var sku = $"R-{Guid.NewGuid():N}"[..12];
        var resp = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            Sku: sku, Name: name, PriceAmount: price, PriceCurrency: "KES",
            UnitOfMeasure: unit, Barcode: null, TaxClass: taxClass), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
    }
}
