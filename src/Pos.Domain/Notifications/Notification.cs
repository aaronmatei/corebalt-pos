using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Notifications;

/// <summary>
/// An in-app back-office notification (the IN-APP channel's store). Tenant+store scoped. Created by the
/// notification dispatcher from an outbox event — NOT an aggregate that raises its own events, so it never
/// re-enters the outbox. <see cref="SourceMessageId"/> is the originating outbox row; it makes dispatch
/// idempotent (one notification per source message) without touching the outbox's own processed state.
/// </summary>
public sealed class Notification : Entity, ITenantScoped, IStoreScoped
{
    public Guid TenantId { get; private set; }
    public Guid StoreId { get; private set; }
    public NotificationType Type { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;

    /// <summary>The product the alert is about (for low-stock); null for types without a subject product.</summary>
    public Guid? ProductId { get; private set; }

    /// <summary>The outbox message this was produced from — the dedup key for at-least-once dispatch.</summary>
    public Guid SourceMessageId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public bool IsRead { get; private set; }

    private Notification() { } // EF

    public static Notification Create(Guid tenantId, Guid storeId, NotificationType type, string title,
        string message, Guid? productId, Guid sourceMessageId, DateTimeOffset now) => new()
    {
        Id = Uuid7.NewGuid(),
        TenantId = tenantId,
        StoreId = storeId,
        Type = type,
        Title = title,
        Message = message,
        ProductId = productId,
        SourceMessageId = sourceMessageId,
        CreatedAtUtc = now,
        IsRead = false
    };

    public void MarkRead() => IsRead = true;
}
