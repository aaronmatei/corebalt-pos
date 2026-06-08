using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pos.Api.Contracts;
using Pos.Application.Customers;
using Pos.Domain.Customers;

namespace Pos.Api.Endpoints;

/// <summary>
/// Customers / loyalty members. Reads + the phone lookup are open to any cashier (the till attaches a
/// member at checkout); writes (create/update/deactivate, point adjustments) are Manager-gated.
/// </summary>
internal static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomers(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/customers").WithTags("Customers");

        // Search by name/phone (Cashier+ — the till needs it). ?q= substring, ?includeInactive=true.
        g.MapGet("/", async (string? q, bool? includeInactive, CustomerService customers, CancellationToken ct) =>
        {
            var list = await customers.SearchAsync(q, includeInactive ?? false, ct: ct);
            return Results.Ok(list.Select(Map));
        }).RequireAuthorization("CashierOrAbove");

        // Fast attach-at-checkout: exact phone lookup (Cashier+).
        g.MapGet("/by-phone/{phone}", async (string phone, CustomerService customers, CancellationToken ct) =>
        {
            var customer = await customers.FindByPhoneAsync(phone, ct);
            return customer is null ? Results.NotFound() : Results.Ok(Map(customer));
        }).RequireAuthorization("CashierOrAbove");

        g.MapGet("/{id:guid}", async (Guid id, CustomerService customers, CancellationToken ct) =>
        {
            var customer = await customers.GetAsync(id, ct);
            return customer is null ? Results.NotFound() : Results.Ok(Map(customer));
        }).RequireAuthorization("CashierOrAbove");

        g.MapPost("/", async (CreateCustomerRequest req, CustomerService customers, CancellationToken ct) =>
        {
            var customer = await customers.CreateAsync(req.Name, req.Phone, req.Email, req.KraPin, req.NationalId, ct);
            return Results.Created($"/api/v1/customers/{customer.Id}", Map(customer));
        }).RequireAuthorization("Manager");

        g.MapPut("/{id:guid}", async (Guid id, UpdateCustomerRequest req, CustomerService customers, CancellationToken ct) =>
        {
            var customer = await customers.UpdateAsync(id, req.Name, req.Phone, req.Email, req.KraPin, req.NationalId, req.IsActive, ct);
            return customer is null ? Results.NotFound() : Results.Ok(Map(customer));
        }).RequireAuthorization("Manager");

        // Manual loyalty adjustment (e.g. goodwill, correction) — positive or negative.
        g.MapPost("/{id:guid}/points", async (Guid id, AdjustPointsRequest req, CustomerService customers, CancellationToken ct) =>
        {
            var customer = await customers.AdjustPointsAsync(id, req.Delta, ct);
            return customer is null ? Results.NotFound() : Results.Ok(Map(customer));
        }).RequireAuthorization("Manager");

        return app;
    }

    private static CustomerResponse Map(Customer c) =>
        new(c.Id, c.Name, c.Phone, c.Email, c.KraPin, c.NationalId, c.LoyaltyPoints, c.IsActive);
}
