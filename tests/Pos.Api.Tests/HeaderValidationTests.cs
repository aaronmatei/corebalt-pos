using System.Net;
using FluentAssertions;
using Pos.Api.Auth;
using Pos.SharedKernel.Ids;
using Xunit;

namespace Pos.Api.Tests;

[Collection(PosApiCollection.Name)]
public sealed class HeaderValidationTests(PosApiFixture fx)
{
    [Fact]
    public async Task Missing_tenant_header_returns_401()
    {
        var client = fx.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderCurrentContext.StoreHeader, Uuid7.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(HeaderCurrentContext.UserHeader,  Uuid7.NewGuid().ToString());

        var resp = await client.GetAsync($"/api/v1/products/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Garbage_tenant_header_returns_401()
    {
        var client = fx.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderCurrentContext.TenantHeader, "not-a-guid");
        client.DefaultRequestHeaders.Add(HeaderCurrentContext.StoreHeader,  Uuid7.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(HeaderCurrentContext.UserHeader,   Uuid7.NewGuid().ToString());

        var resp = await client.GetAsync($"/api/v1/products/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Healthz_does_not_require_headers()
    {
        var client = fx.Factory.CreateClient();
        var resp = await client.GetAsync("/healthz");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
