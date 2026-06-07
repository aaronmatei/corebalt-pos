using Pos.Domain.Notifications;

namespace Pos.Application.Notifications;

/// <summary>A rendered alert handed to each delivery channel. Channel-agnostic — the in-app channel
/// persists it to the feed; Email/SMS channels (stubs for now) would format + send it.</summary>
public sealed record NotificationMessage(
    Guid TenantId,
    Guid StoreId,
    NotificationType Type,
    string Title,
    string Body,
    Guid? ProductId,
    Guid SourceMessageId);

/// <summary>
/// A pluggable delivery channel for back-office notifications. IN-APP is implemented now (writes the
/// feed); Email and SMS are stubs wired later from per-client settings (SMTP / an SMS gateway). The
/// dispatcher fans each pending event out to every registered channel.
/// </summary>
public interface INotificationChannel
{
    /// <summary>Stable channel id ("InApp", "Email", "Sms") — used in logs and (later) per-channel routing.</summary>
    string Channel { get; }

    /// <summary>Is this channel switched on? Stubs return false until real credentials are configured,
    /// so the dispatcher can skip them cleanly.</summary>
    bool IsEnabled { get; }

    Task SendAsync(NotificationMessage message, CancellationToken ct = default);
}
