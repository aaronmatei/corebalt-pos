using Pos.Application.Abstractions;
using Pos.Domain.Notifications;

namespace Pos.Application.Notifications;

/// <summary>
/// Read + acknowledge side of the in-app notification feed, shared by the back-office and the JSON API.
/// The unread count is the live badge on the back-office home. Scoped to the caller's tenant/store.
/// </summary>
public sealed class NotificationFeedService
{
    private readonly ICurrentContext _ctx;
    private readonly INotificationRepository _repo;
    private readonly IUnitOfWork _uow;

    public NotificationFeedService(ICurrentContext ctx, INotificationRepository repo, IUnitOfWork uow)
    {
        _ctx = ctx;
        _repo = repo;
        _uow = uow;
    }

    public Task<IReadOnlyList<Notification>> ListAsync(bool unreadOnly = false, int limit = 50, CancellationToken ct = default) =>
        _repo.ListAsync(_ctx.TenantId, _ctx.StoreId, unreadOnly, limit, ct);

    public Task<int> UnreadCountAsync(CancellationToken ct = default) =>
        _repo.UnreadCountAsync(_ctx.TenantId, _ctx.StoreId, ct);

    public async Task<bool> MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        var n = await _repo.GetAsync(_ctx.TenantId, _ctx.StoreId, id, ct);
        if (n is null) return false;
        n.MarkRead();
        await _uow.SaveChangesAsync(ct);
        return true;
    }

    public async Task MarkAllReadAsync(CancellationToken ct = default)
    {
        var unread = await _repo.ListAsync(_ctx.TenantId, _ctx.StoreId, unreadOnly: true, limit: int.MaxValue, ct);
        foreach (var n in unread) n.MarkRead();
        await _uow.SaveChangesAsync(ct);
    }
}
