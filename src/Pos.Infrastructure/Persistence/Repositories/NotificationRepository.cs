using Microsoft.EntityFrameworkCore;
using Pos.Application.Notifications;
using Pos.Domain.Notifications;

namespace Pos.Infrastructure.Persistence.Repositories;

internal sealed class NotificationRepository : INotificationRepository
{
    private readonly PosDbContext _db;
    public NotificationRepository(PosDbContext db) => _db = db;

    public async Task AddAsync(Notification notification, CancellationToken ct = default) =>
        await _db.Notifications.AddAsync(notification, ct);

    public Task<bool> ExistsForSourceAsync(Guid tenantId, Guid storeId, Guid sourceMessageId, CancellationToken ct = default) =>
        _db.Notifications.AnyAsync(n => n.TenantId == tenantId && n.StoreId == storeId && n.SourceMessageId == sourceMessageId, ct);

    public async Task<IReadOnlyList<Notification>> ListAsync(Guid tenantId, Guid storeId, bool unreadOnly, int limit, CancellationToken ct = default) =>
        await _db.Notifications
            .Where(n => n.TenantId == tenantId && n.StoreId == storeId && (!unreadOnly || !n.IsRead))
            .OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id)
            .Take(limit)
            .ToListAsync(ct);

    public Task<int> UnreadCountAsync(Guid tenantId, Guid storeId, CancellationToken ct = default) =>
        _db.Notifications.CountAsync(n => n.TenantId == tenantId && n.StoreId == storeId && !n.IsRead, ct);

    public Task<Notification?> GetAsync(Guid tenantId, Guid storeId, Guid id, CancellationToken ct = default) =>
        _db.Notifications.FirstOrDefaultAsync(n => n.TenantId == tenantId && n.StoreId == storeId && n.Id == id, ct);
}
