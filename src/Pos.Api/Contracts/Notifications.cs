using Pos.Domain.Notifications;

namespace Pos.Api.Contracts;

public sealed record NotificationResponse(
    Guid Id,
    NotificationType Type,
    string Title,
    string Message,
    Guid? ProductId,
    string CreatedAtEat,
    bool IsRead);

public sealed record NotificationFeedResponse(int UnreadCount, IReadOnlyList<NotificationResponse> Items);
