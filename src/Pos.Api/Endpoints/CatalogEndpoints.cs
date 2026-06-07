using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pos.Api.Contracts;
using Pos.Application.Abstractions;
using Pos.Application.Catalog;
using Pos.SharedKernel;

namespace Pos.Api.Endpoints;

internal static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalog(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/products").WithTags("Catalog");                          // reads: any authenticated user
        var mgr = app.MapGroup("/products").WithTags("Catalog").RequireAuthorization("Manager"); // writes: Manager only

        // All product write logic lives in ProductService (shared with the Blazor back-office); these
        // endpoints just map HTTP. Conflicts surface as 409 via the DomainExceptionHandler.
        mgr.MapPost("/", async (CreateProductRequest req, ProductService svc, CancellationToken ct) =>
        {
            var product = await svc.CreateAsync(
                req.Sku, req.Name, new Money(req.PriceAmount, req.PriceCurrency),
                req.UnitOfMeasure, req.Barcode, req.TaxClass, req.CategoryId, ct);
            return Results.Created($"/api/v1/products/{product.Id}", product.ToResponse());
        });

        // ?categoryId=<guid> narrows to one category; ?categoryId=00000000-…-0000 (Guid.Empty) → uncategorized.
        g.MapGet("/", async (ProductService svc, CancellationToken ct, bool includeInactive = false, Guid? categoryId = null) =>
        {
            var list = await svc.ListAsync(includeInactive, categoryId, ct);
            return Results.Ok(list.Select(p => p.ToResponse()).ToList());
        });

        g.MapGet("/{id:guid}", async (Guid id, ProductService svc, CancellationToken ct) =>
        {
            var product = await svc.GetAsync(id, ct);
            return product is null ? Results.NotFound() : Results.Ok(product.ToResponse());
        });

        g.MapGet("/by-sku/{sku}", async (string sku, ICurrentContext ctx, IProductRepository products, CancellationToken ct) =>
        {
            var product = await products.FindBySkuAsync(ctx.TenantId, ctx.StoreId, sku, ct);
            return product is null ? Results.NotFound() : Results.Ok(product.ToResponse());
        });

        g.MapGet("/barcode/{barcode}", async (string barcode, ICurrentContext ctx, IProductRepository products, CancellationToken ct) =>
        {
            var product = await products.FindByBarcodeAsync(ctx.TenantId, ctx.StoreId, barcode, ct);
            return product is null ? Results.NotFound() : Results.Ok(product.ToResponse());
        });

        mgr.MapPut("/{id:guid}", async (Guid id, UpdateProductRequest req, ProductService svc, CancellationToken ct) =>
        {
            var product = await svc.UpdateAsync(id, req.Name, req.Barcode, req.UnitOfMeasure, req.TaxClass, req.IsActive, req.CategoryId, ct);
            return product is null ? Results.NotFound() : Results.Ok(product.ToResponse());
        });

        mgr.MapPost("/{id:guid}/deactivate", async (Guid id, ProductService svc, CancellationToken ct) =>
        {
            var product = await svc.SetActiveAsync(id, active: false, ct); // soft delete — never hard-delete
            return product is null ? Results.NotFound() : Results.Ok(product.ToResponse());
        });

        mgr.MapPut("/{id:guid}/price", async (Guid id, RepriceProductRequest req, ProductService svc, CancellationToken ct) =>
        {
            var product = await svc.RepriceAsync(id, new Money(req.Amount, req.Currency), ct);
            return product is null ? Results.NotFound() : Results.NoContent();
        });

        return app;
    }
}
