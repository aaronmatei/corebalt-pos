using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Pos.Api.Contracts;
using Pos.Application.Reports;
using Pos.Domain.Catalog;
using Pos.Domain.Sales;
using Xunit;

namespace Pos.Api.Tests;

[Collection(PosApiCollection.Name)]
public sealed class ReportsTests(PosApiFixture fx)
{
    [Fact]
    public async Task Vat_report_aggregates_output_tax_by_class_from_committed_sales()
    {
        var (client, _, _, _) = fx.NewClient();

        // VAT-inclusive prices: standard-rated 116 backs out to taxable 100 + VAT 16; zero-rated is all net.
        var std = await CreateProduct(client, $"STD-{Guid.NewGuid():N}"[..14], 116m, TaxClass.StandardRated);
        var zero = await CreateProduct(client, $"ZER-{Guid.NewGuid():N}"[..14], 50m, TaxClass.ZeroRated);

        var register = await client.OpenShiftAsync();
        var checkout = new CheckoutRequest(
            RegisterId: register,
            Lines: [new CheckoutLineRequest(std.Id, 1), new CheckoutLineRequest(zero.Id, 1)],
            Tenders: [new CheckoutTenderRequest(TenderType.Cash, 166m)],
            Currency: "KES",
            SaleId: Guid.CreateVersion7());
        (await client.PostAsJsonAsync("/api/v1/sales/checkout", checkout, PosApiFixture.Json))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // A range that spans today's sale regardless of the EAT clock.
        var eatToday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).Date);
        var from = eatToday.AddDays(-1).ToString("yyyy-MM-dd");
        var to = eatToday.AddDays(1).ToString("yyyy-MM-dd");

        var resp = await client.GetAsync($"/api/v1/reports/vat?from={from}&to={to}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = (await resp.Content.ReadFromJsonAsync<VatReport>(PosApiFixture.Json))!;

        report.SaleCount.Should().Be(1);
        report.TotalTaxable.Should().Be(150m);
        report.TotalVat.Should().Be(16m);
        report.TotalGross.Should().Be(166m);

        report.Lines.Should().Contain(l => l.TaxClass == "StandardRated" && l.Taxable == 100m && l.Vat == 16m && l.Gross == 116m);
        report.Lines.Should().Contain(l => l.TaxClass == "ZeroRated" && l.Taxable == 50m && l.Vat == 0m && l.Gross == 50m);
    }

    [Fact]
    public async Task Vat_report_csv_format_downloads_as_text_csv()
    {
        var (client, _, _, _) = fx.NewClient();
        var resp = await client.GetAsync("/api/v1/reports/vat?format=csv");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var csv = await resp.Content.ReadAsStringAsync();
        csv.Should().Contain("Tax class,Rate %,Taxable,VAT,Gross,Sales");
        csv.Should().Contain("TOTAL");
    }

    private static async Task<ProductResponse> CreateProduct(HttpClient client, string sku, decimal price, TaxClass tax)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            Sku: sku, Name: $"Test {sku}", PriceAmount: price, PriceCurrency: "KES",
            UnitOfMeasure: UnitOfMeasure.Each, TaxClass: tax), PosApiFixture.Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
    }
}
