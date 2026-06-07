using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pos.Api.Contracts;
using Pos.Application.Notifications;
using Pos.Domain.Notifications;

namespace Pos.Api.Endpoints;

/// <summary>
/// The in-app notification feed (low-stock alerts today). Reads + acknowledge for any authenticated
/// caller in the store; the unread count drives the back-office badge.
/// </summary>
internal static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotifications(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/notifications").WithTags("Notifications");

        g.MapGet("/", async (NotificationFeedService feed, CancellationToken ct, bool unreadOnly = false, int limit = 50) =>
        {
            var items = await feed.ListAsync(unreadOnly, limit, ct);
            var unread = await feed.UnreadCountAsync(ct);
            return Results.Ok(new NotificationFeedResponse(unread, items.Select(ToResponse).ToList()));
        });

        g.MapGet("/count", async (NotificationFeedService feed, CancellationToken ct) =>
            Results.Ok(new { unreadCount = await feed.UnreadCountAsync(ct) }));

        g.MapPost("/{id:guid}/read", async (Guid id, NotificationFeedService feed, CancellationToken ct) =>
            await feed.MarkReadAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        g.MapPost("/read-all", async (NotificationFeedService feed, CancellationToken ct) =>
        {
            await feed.MarkAllReadAsync(ct);
            return Results.NoContent();
        });

        return app;
    }

    private static NotificationResponse ToResponse(Notification n) => new(
        n.Id, n.Type, n.Title, n.Message, n.ProductId,
        n.CreatedAtUtc.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        n.IsRead);
}
