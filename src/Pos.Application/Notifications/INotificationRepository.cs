using Pos.Domain.Notifications;

namespace Pos.Application.Notifications;

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken ct = default);

    /// <summary>Has a notification already been produced from this outbox message? (Idempotent dispatch.)</summary>
    Task<bool> ExistsForSourceAsync(Guid tenantId, Guid storeId, Guid sourceMessageId, CancellationToken ct = default);

    /// <summary>Feed for the back-office, newest first. unreadOnly limits to unread; limit caps the page.</summary>
    Task<IReadOnlyList<Notification>> ListAsync(Guid tenantId, Guid storeId, bool unreadOnly, int limit, CancellationToken ct = default);

    Task<int> UnreadCountAsync(Guid tenantId, Guid storeId, CancellationToken ct = default);

    Task<Notification?> GetAsync(Guid tenantId, Guid storeId, Guid id, CancellationToken ct = default);
}
