using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Pos.Api.Contracts;
using Pos.Domain.Catalog;
using Pos.Domain.Sales;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// Product categories: tenant-scoped master data used for till browsing, back-office organisation and
/// sales-by-category reporting. Covers create + uniqueness, assignment, filtered listing, the
/// uncategorized fallback (existing products keep selling), the report aggregation (join to current
/// category), and that deactivating a category hides it from pickers without breaking products in it.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class CategoryManagementTests(PosApiFixture fx)
{
    [Fact]
    public async Task Create_then_assign_to_a_product()
    {
        var (client, _, _, _) = fx.NewClient();
        var cat = await CreateCategory(client, "Beverages");
        cat.IsActive.Should().BeTrue();

        var product = await CreateProduct(client, "Soda", price: 100m, categoryId: cat.Id);
        product.CategoryId.Should().Be(cat.Id);

        // Round-trips through GET too.
        var fetched = (await (await client.GetAsync($"/api/v1/products/{product.Id}"))
            .Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
        fetched.CategoryId.Should().Be(cat.Id);
    }

    [Fact]
    public async Task Duplicate_name_under_the_same_parent_is_rejected()
    {
        var (client, _, _, _) = fx.NewClient();
        (await CreateCategoryRaw(client, "Produce")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await CreateCategoryRaw(client, "Produce")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Assigning_a_nonexistent_category_is_rejected()
    {
        var (client, _, _, _) = fx.NewClient();
        var resp = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            $"NC-{Guid.NewGuid():N}"[..12], "Orphan", 100m, "KES", UnitOfMeasure.Each, null,
            TaxClass.StandardRated, CategoryId: Uuid7.NewGuid()), PosApiFixture.Json);
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Product_list_filters_by_category_and_by_uncategorized()
    {
        var (client, _, _, _) = fx.NewClient(); // fresh store → list is exactly what we add
        var drinks = await CreateCategory(client, "Drinks");
        var inDrinks = await CreateProduct(client, "Water", 50m, categoryId: drinks.Id);
        var loose = await CreateProduct(client, "Loose item", 50m, categoryId: null);

        var byCat = await List(client, $"/api/v1/products?categoryId={drinks.Id}");
        byCat.Select(p => p.Id).Should().Contain(inDrinks.Id).And.NotContain(loose.Id);

        // Guid.Empty is the "uncategorized" sentinel.
        var uncategorized = await List(client, $"/api/v1/products?categoryId={Guid.Empty}");
        uncategorized.Select(p => p.Id).Should().Contain(loose.Id).And.NotContain(inDrinks.Id);

        // No filter → both.
        var all = await List(client, "/api/v1/products");
        all.Select(p => p.Id).Should().Contain(new[] { inDrinks.Id, loose.Id });
    }

    [Fact]
    public async Task Uncategorized_product_sells_normally()
    {
        var (client, _, _, _) = fx.NewClient();
        var register = Uuid7.NewGuid();
        await OpenSession(client, register, 0m);
        var product = await CreateProduct(client, "No category", 100m, categoryId: null);

        var resp = await client.PostAsJsonAsync("/api/v1/sales/checkout", new CheckoutRequest(
            RegisterId: register,
            Lines: new[] { new CheckoutLineRequest(product.Id, 1m) },
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Cash, 100m, null) }), PosApiFixture.Json);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Sales_by_category_aggregates_gross_and_vat_by_current_category()
    {
        var (client, _, _, _) = fx.NewClient();
        var register = Uuid7.NewGuid();
        var session = await OpenSession(client, register, 0m);

        var bev = await CreateCategory(client, "Beverages");
        var food = await CreateCategory(client, "Food");
        var soda = await CreateProduct(client, "Soda", 116m, categoryId: bev.Id);   // net 100, VAT 16
        var bread = await CreateProduct(client, "Bread", 232m, categoryId: food.Id); // net 200, VAT 32
        var misc = await CreateProduct(client, "Misc", 58m, categoryId: null);        // net 50, VAT 8

        await Sell(client, register, soda.Id, qty: 2, cash: 232m);   // Beverages gross 232
        await Sell(client, register, bread.Id, qty: 1, cash: 232m);  // Food gross 232
        await Sell(client, register, misc.Id, qty: 1, cash: 58m);    // Uncategorized gross 58

        var report = await Report(client, session.Id);
        var cats = report.Report.Categories;

        cats.Should().Contain(c => c.Name == "Beverages" && c.Gross == 232m && c.Vat == 32m && c.ItemCount == 2m);
        cats.Should().Contain(c => c.Name == "Food" && c.Gross == 232m && c.Vat == 32m && c.ItemCount == 1m);
        cats.Should().Contain(c => c.Name == "Uncategorized" && c.Gross == 58m && c.ItemCount == 1m);
        cats.Last().Name.Should().Be("Uncategorized", "the no-category bucket is listed last");
    }

    [Fact]
    public async Task Deactivating_a_category_hides_it_from_pickers_but_products_in_it_still_work()
    {
        var (client, _, _, _) = fx.NewClient();
        var register = Uuid7.NewGuid();
        await OpenSession(client, register, 0m);

        var cat = await CreateCategory(client, "Seasonal");
        var product = await CreateProduct(client, "Pumpkin", 100m, categoryId: cat.Id);

        (await client.PostAsync($"/api/v1/categories/{cat.Id}/deactivate", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Hidden from the default (active) picker, visible with includeInactive.
        var activePicker = await ListCategories(client, "/api/v1/categories");
        activePicker.Select(c => c.Id).Should().NotContain(cat.Id);
        var allPicker = await ListCategories(client, "/api/v1/categories?includeInactive=true");
        allPicker.Select(c => c.Id).Should().Contain(cat.Id);

        // The product keeps its link, still lists, and still sells.
        var fetched = (await (await client.GetAsync($"/api/v1/products/{product.Id}"))
            .Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
        fetched.CategoryId.Should().Be(cat.Id);

        var sale = await client.PostAsJsonAsync("/api/v1/sales/checkout", new CheckoutRequest(
            RegisterId: register,
            Lines: new[] { new CheckoutLineRequest(product.Id, 1m) },
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Cash, 100m, null) }), PosApiFixture.Json);
        sale.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── helpers ──
    private static async Task<CategoryResponse> CreateCategory(HttpClient client, string name)
    {
        var r = await CreateCategoryRaw(client, name);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<CategoryResponse>(PosApiFixture.Json))!;
    }

    private static Task<HttpResponseMessage> CreateCategoryRaw(HttpClient client, string name) =>
        client.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(name), PosApiFixture.Json);

    private static async Task<ProductResponse> CreateProduct(HttpClient client, string name, decimal price, Guid? categoryId)
    {
        var sku = $"CAT-{Guid.NewGuid():N}"[..12];
        var r = await client.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            sku, name, price, "KES", UnitOfMeasure.Each, null, TaxClass.StandardRated, categoryId), PosApiFixture.Json);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<ProductResponse>(PosApiFixture.Json))!;
    }

    private static async Task<List<ProductResponse>> List(HttpClient client, string url) =>
        (await (await client.GetAsync(url)).Content.ReadFromJsonAsync<List<ProductResponse>>(PosApiFixture.Json))!;

    private static async Task<List<CategoryResponse>> ListCategories(HttpClient client, string url) =>
        (await (await client.GetAsync(url)).Content.ReadFromJsonAsync<List<CategoryResponse>>(PosApiFixture.Json))!;

    private static async Task<SessionResponse> OpenSession(HttpClient client, Guid register, decimal openingFloat)
    {
        var r = await client.PostAsJsonAsync("/api/v1/sessions/open", new OpenSessionRequest(register, openingFloat), PosApiFixture.Json);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<SessionResponse>(PosApiFixture.Json))!;
    }

    private static async Task Sell(HttpClient client, Guid register, Guid productId, decimal qty, decimal cash)
    {
        var r = await client.PostAsJsonAsync("/api/v1/sales/checkout", new CheckoutRequest(
            RegisterId: register,
            Lines: new[] { new CheckoutLineRequest(productId, qty) },
            Tenders: new[] { new CheckoutTenderRequest(TenderType.Cash, cash, null) }), PosApiFixture.Json);
        r.EnsureSuccessStatusCode();
    }

    private static async Task<ShiftReportResponse> Report(HttpClient client, Guid sessionId) =>
        (await (await client.GetAsync($"/api/v1/sessions/{sessionId}/report"))
            .Content.ReadFromJsonAsync<ShiftReportResponse>(PosApiFixture.Json))!;
}
