using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pos.Api.Contracts;
using Pos.Application.Catalog;

namespace Pos.Api.Endpoints;

/// <summary>
/// Tenant-scoped category master data. Reads are open to any authenticated caller (the till browses
/// by category); writes are Manager-only. All logic lives in <see cref="CategoryService"/> (shared with
/// the Blazor back-office); name clashes surface as 409 via the DomainExceptionHandler.
/// </summary>
internal static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategories(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/categories").WithTags("Catalog");                          // reads: any authenticated user
        var mgr = app.MapGroup("/categories").WithTags("Catalog").RequireAuthorization("Manager"); // writes: Manager only

        g.MapGet("/", async (CategoryService svc, CancellationToken ct, bool includeInactive = false) =>
        {
            var list = await svc.ListAsync(includeInactive, ct);
            return Results.Ok(list.Select(c => c.ToResponse()).ToList());
        });

        g.MapGet("/{id:guid}", async (Guid id, CategoryService svc, CancellationToken ct) =>
        {
            var category = await svc.GetAsync(id, ct);
            return category is null ? Results.NotFound() : Results.Ok(category.ToResponse());
        });

        mgr.MapPost("/", async (CreateCategoryRequest req, CategoryService svc, CancellationToken ct) =>
        {
            var category = await svc.CreateAsync(req.Name, req.ParentId, req.DisplayOrder, ct);
            return Results.Created($"/api/v1/categories/{category.Id}", category.ToResponse());
        });

        mgr.MapPut("/{id:guid}", async (Guid id, UpdateCategoryRequest req, CategoryService svc, CancellationToken ct) =>
        {
            var category = await svc.UpdateAsync(id, req.Name, req.ParentId, req.DisplayOrder, req.IsActive, ct);
            return category is null ? Results.NotFound() : Results.Ok(category.ToResponse());
        });

        mgr.MapPost("/{id:guid}/deactivate", async (Guid id, CategoryService svc, CancellationToken ct) =>
        {
            var category = await svc.SetActiveAsync(id, active: false, ct); // soft delete — products keep their link
            return category is null ? Results.NotFound() : Results.Ok(category.ToResponse());
        });

        return app;
    }
}
