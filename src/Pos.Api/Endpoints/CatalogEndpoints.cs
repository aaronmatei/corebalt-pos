using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pos.Api.Contracts;
using Pos.Application.Abstractions;
using Pos.Application.Catalog;
using Pos.Domain.Catalog;
using Pos.SharedKernel;

namespace Pos.Api.Endpoints;

internal static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalog(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/products").WithTags("Catalog");

        g.MapPost("/", async (
            CreateProductRequest req,
            ICurrentContext ctx,
            IProductRepository products,
            IUnitOfWork uow,
            CancellationToken ct) =>
        {
            var product = Product.Create(
                ctx.TenantId, ctx.StoreId,
                req.Sku, req.Name,
                new Money(req.PriceAmount, req.PriceCurrency),
                req.UnitOfMeasure);
            await products.AddAsync(product, ct);
            await uow.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/products/{product.Id}", product.ToResponse());
        });

        g.MapGet("/{id:guid}", async (
            Guid id,
            ICurrentContext ctx,
            IProductRepository products,
            CancellationToken ct) =>
        {
            var product = await products.GetAsync(ctx.TenantId, ctx.StoreId, id, ct);
            return product is null ? Results.NotFound() : Results.Ok(product.ToResponse());
        });

        g.MapGet("/by-sku/{sku}", async (
            string sku,
            ICurrentContext ctx,
            IProductRepository products,
            CancellationToken ct) =>
        {
            var product = await products.FindBySkuAsync(ctx.TenantId, ctx.StoreId, sku, ct);
            return product is null ? Results.NotFound() : Results.Ok(product.ToResponse());
        });

        g.MapPut("/{id:guid}/price", async (
            Guid id,
            RepriceProductRequest req,
            ICurrentContext ctx,
            IProductRepository products,
            IUnitOfWork uow,
            CancellationToken ct) =>
        {
            var product = await products.GetAsync(ctx.TenantId, ctx.StoreId, id, ct);
            if (product is null) return Results.NotFound();
            product.Reprice(new Money(req.Amount, req.Currency));
            await uow.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return app;
    }
}
