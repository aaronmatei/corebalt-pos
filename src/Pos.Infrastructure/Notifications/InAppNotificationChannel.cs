using Pos.Application.Abstractions;
using Pos.Application.Notifications;
using Pos.Domain.Notifications;

namespace Pos.Infrastructure.Notifications;

/// <summary>
/// The IN-APP channel: persists each alert as a <see cref="Notification"/> row (the back-office feed +
/// unread badge). Idempotent via the source outbox message id — a duplicate dispatch is a no-op. Always
/// enabled (it's the baseline channel that needs no external credentials).
/// </summary>
internal sealed class InAppNotificationChannel : INotificationChannel
{
    private readonly INotificationRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public InAppNotificationChannel(INotificationRepository repo, IUnitOfWork uow, IClock clock)
    {
        _repo = repo;
        _uow = uow;
        _clock = clock;
    }

    public string Channel => "InApp";
    public bool IsEnabled => true;

    public async Task SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        if (await _repo.ExistsForSourceAsync(message.TenantId, message.StoreId, message.SourceMessageId, ct))
            return; // already in the feed — don't duplicate

        var notification = Notification.Create(
            message.TenantId, message.StoreId, message.Type, message.Title, message.Body,
            message.ProductId, message.SourceMessageId, _clock.UtcNow);

        await _repo.AddAsync(notification, ct);
        await _uow.SaveChangesAsync(ct);
    }
}
