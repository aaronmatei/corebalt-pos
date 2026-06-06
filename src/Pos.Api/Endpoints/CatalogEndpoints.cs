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
            // SKU unique, and barcode unique when present (validated within this store's catalogue).
            if (await products.FindBySkuAsync(ctx.TenantId, ctx.StoreId, req.Sku, ct) is not null)
                return Results.Problem($"A product with SKU '{req.Sku}' already exists.", statusCode: StatusCodes.Status409Conflict);
            if (!string.IsNullOrWhiteSpace(req.Barcode) &&
                await products.FindByBarcodeAsync(ctx.TenantId, ctx.StoreId, req.Barcode, ct) is not null)
                return Results.Problem($"A product with barcode '{req.Barcode}' already exists.", statusCode: StatusCodes.Status409Conflict);

            var product = Product.Create(
                ctx.TenantId, ctx.StoreId,
                req.Sku, req.Name,
                new Money(req.PriceAmount, req.PriceCurrency),
                req.UnitOfMeasure,
                req.Barcode,
                req.TaxClass);
            await products.AddAsync(product, ct);
            await uow.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/products/{product.Id}", product.ToResponse());
        });

        g.MapGet("/", async (
            ICurrentContext ctx,
            IProductRepository products,
            CancellationToken ct,
            bool includeInactive = false) =>
        {
            var list = await products.ListAsync(ctx.TenantId, ctx.StoreId, includeInactive, ct);
            return Results.Ok(list.Select(p => p.ToResponse()).ToList());
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

        g.MapGet("/barcode/{barcode}", async (
            string barcode,
            ICurrentContext ctx,
            IProductRepository products,
            CancellationToken ct) =>
        {
            var product = await products.FindByBarcodeAsync(ctx.TenantId, ctx.StoreId, barcode, ct);
            return product is null ? Results.NotFound() : Results.Ok(product.ToResponse());
        });

        g.MapPut("/{id:guid}", async (
            Guid id,
            UpdateProductRequest req,
            ICurrentContext ctx,
            IProductRepository products,
            IUnitOfWork uow,
            CancellationToken ct) =>
        {
            var product = await products.GetAsync(ctx.TenantId, ctx.StoreId, id, ct);
            if (product is null) return Results.NotFound();

            // Barcode stays unique when present (ignore the product's own row).
            if (!string.IsNullOrWhiteSpace(req.Barcode))
            {
                var byBarcode = await products.FindByBarcodeAsync(ctx.TenantId, ctx.StoreId, req.Barcode, ct);
                if (byBarcode is not null && byBarcode.Id != id)
                    return Results.Problem($"A product with barcode '{req.Barcode}' already exists.", statusCode: StatusCodes.Status409Conflict);
            }

            product.UpdateDetails(req.Name, req.Barcode, req.UnitOfMeasure, req.TaxClass);
            if (req.IsActive) product.Reactivate(); else product.Deactivate();
            await uow.SaveChangesAsync(ct);
            return Results.Ok(product.ToResponse());
        });

        g.MapPost("/{id:guid}/deactivate", async (
            Guid id,
            ICurrentContext ctx,
            IProductRepository products,
            IUnitOfWork uow,
            CancellationToken ct) =>
        {
            var product = await products.GetAsync(ctx.TenantId, ctx.StoreId, id, ct);
            if (product is null) return Results.NotFound();
            product.Deactivate(); // soft delete — never hard-delete a catalogue row
            await uow.SaveChangesAsync(ct);
            return Results.Ok(product.ToResponse());
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
            // A real change raises ProductPriceChanged → outbox (audit + central-pricing seam).
            product.Reprice(new Money(req.Amount, req.Currency), ctx.UserId);
            await uow.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return app;
    }
}
