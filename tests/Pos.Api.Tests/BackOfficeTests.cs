using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Pos.Api.Contracts;
using Pos.Application.Identity;
using Pos.Domain.Identity;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// The Blazor back-office (static SSR + cookie auth) hosted in the store server. Drives the real pages
/// and form-post endpoints over HTTP: a Manager signs in and reaches the pages; a Cashier is bounced;
/// and creating a product / cashier and receiving stock through the UI persists and shows up.
/// </summary>
[Collection(PosApiCollection.Name)]
public sealed class BackOfficeTests(PosApiFixture fx)
{
    private const string Password = "BoTest123!";

    [Fact]
    public async Task Manager_signs_in_and_reaches_the_pages()
    {
        var (user, _) = await SeedUserAsync(UserRole.Manager);
        var client = Client();
        (await LoginAsync(client, user, Password)).StatusCode.Should().Be(HttpStatusCode.Found);

        foreach (var path in new[] { "/products", "/stock", "/users" })
        {
            var page = await client.GetAsync(path);
            page.StatusCode.Should().Be(HttpStatusCode.OK, $"manager should reach {path}");
        }
    }

    [Fact]
    public async Task Cashier_is_rejected_from_the_back_office()
    {
        var (user, _) = await SeedUserAsync(UserRole.Cashier);
        var client = Client();
        await LoginAsync(client, user, Password); // cashier has a password, so the cookie is issued…

        var page = await client.GetAsync("/products"); // …but the Manager policy bounces them to login
        page.StatusCode.Should().Be(HttpStatusCode.Found);
        page.Headers.Location!.OriginalString.Should().Contain("/login");
    }

    [Fact]
    public async Task Creating_a_product_through_the_ui_persists_and_appears_in_the_list()
    {
        var (user, _) = await SeedUserAsync(UserRole.Manager);
        var client = Client();
        await LoginAsync(client, user, Password);

        var sku = $"BO-{Guid.NewGuid():N}"[..10];
        var name = $"UI Widget {sku}";
        var resp = await PostFormAsync(client, "/products/new", "/backoffice/products", new()
        {
            ["sku"] = sku, ["name"] = name, ["unit"] = "Each", ["taxClass"] = "StandardRated", ["priceAmount"] = "199.00",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Found);
        resp.Headers.Location!.OriginalString.Should().Be("/products");

        var list = await (await client.GetAsync("/products")).Content.ReadAsStringAsync();
        list.Should().Contain(name).And.Contain(sku);
    }

    [Fact]
    public async Task Creating_a_cashier_through_the_ui_persists_and_can_pin_login()
    {
        var (user, _) = await SeedUserAsync(UserRole.Manager);
        var client = Client();
        await LoginAsync(client, user, Password);

        var staff = $"T{Guid.NewGuid():N}"[..7];
        var resp = await PostFormAsync(client, "/users", "/backoffice/users", new()
        {
            ["name"] = "Created Cashier", ["staffCode"] = staff, ["pin"] = "4455", ["role"] = "Cashier",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Found);

        var list = await (await client.GetAsync("/users")).Content.ReadAsStringAsync();
        list.Should().Contain(staff).And.Contain("Created Cashier");

        // The created cashier can actually sign in at a till.
        var pin = await fx.Factory.CreateClient().PostAsJsonAsync("/api/v1/auth/pin-login",
            new PinLoginRequest(staff, "4455"), PosApiFixture.Json);
        pin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Receiving_stock_through_the_ui_reflects_in_on_hand()
    {
        var (user, _) = await SeedUserAsync(UserRole.Manager);
        var productId = await SeedProductAsync();
        var client = Client();
        await LoginAsync(client, user, Password);

        var resp = await PostFormAsync(client, "/stock", "/backoffice/stock/receive", new()
        {
            ["productId"] = productId.ToString(), ["quantity"] = "7", ["reason"] = "Purchase", ["reference"] = "GRN-UI",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Found);

        // On-hand is derived from movements — confirm via the report page and the API.
        var report = await (await client.GetAsync("/stock")).Content.ReadAsStringAsync();
        report.Should().Contain("UI-STOCK");

        using var scope = fx.Factory.Services.CreateScope();
        var onHand = await scope.ServiceProvider.GetRequiredService<Pos.Application.Inventory.IStockMovementRepository>()
            .GetOnHandAsync(StoreTenant, StoreStore, productId);
        onHand.Should().Be(7m);
    }

    // ── helpers ──
    private static HttpClient ClientFrom(PosApiFixture fx) =>
        fx.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private HttpClient Client() => ClientFrom(fx);

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password)
    {
        var token = await TokenAsync(client, "/login");
        return await client.PostAsync("/backoffice/login", Form(new()
        {
            ["__RequestVerificationToken"] = token, ["username"] = username, ["password"] = password,
        }));
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string tokenPage, string action, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await TokenAsync(client, tokenPage);
        return await client.PostAsync(action, Form(fields));
    }

    private static async Task<string> TokenAsync(HttpClient client, string page)
    {
        var html = await (await client.GetAsync(page)).Content.ReadAsStringAsync();
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        m.Success.Should().BeTrue($"page {page} should render an antiforgery token");
        return m.Groups[1].Value;
    }

    private static FormUrlEncodedContent Form(Dictionary<string, string> fields) => new(fields);

    // The back-office operates in the StoreServer scope (appsettings StoreServer:*).
    private static readonly Guid StoreTenant = Guid.Parse("019600c0-0000-7000-8000-000000000001");
    private static readonly Guid StoreStore = Guid.Parse("019600c0-0000-7000-8000-000000000002");

    private async Task<(string Username, string Password)> SeedUserAsync(UserRole role)
    {
        using var scope = fx.Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        var username = $"bo-{Guid.NewGuid():N}"[..14];
        var staff = $"S{Guid.NewGuid():N}"[..7];
        await auth.CreateUserAsync($"{role} BO", username, staff, role, pin: "4321", password: Password);
        return (username, Password);
    }

    private async Task<Guid> SeedProductAsync()
    {
        // Create directly via the repo with explicit ids — a plain DI scope has no HttpContext, so the
        // ICurrentContext-based ProductService can't be used here (the UI path runs under the cookie).
        using var scope = fx.Factory.Services.CreateScope();
        var products = scope.ServiceProvider.GetRequiredService<Pos.Application.Catalog.IProductRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<Pos.Application.Abstractions.IUnitOfWork>();
        var p = Pos.Domain.Catalog.Product.Create(StoreTenant, StoreStore, $"UI-STOCK-{Guid.NewGuid():N}"[..14],
            "UI-STOCK Item", new Pos.SharedKernel.Money(50m, "KES"), Pos.Domain.Catalog.UnitOfMeasure.Each,
            null, Pos.Domain.Catalog.TaxClass.StandardRated);
        await products.AddAsync(p);
        await uow.SaveChangesAsync();
        return p.Id;
    }
}
