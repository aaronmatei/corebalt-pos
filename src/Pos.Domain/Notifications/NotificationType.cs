namespace Pos.Domain.Notifications;

/// <summary>The kind of back-office notification. Only LowStock today; the enum leaves room for more
/// (price-change approvals, sync failures, …) without reshaping the feed.</summary>
public enum NotificationType
{
    LowStock = 0
}
