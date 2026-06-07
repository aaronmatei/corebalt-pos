using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Application.Notifications;
using Pos.Domain.Catalog;
using Pos.Domain.Catalog.Events;
using Pos.Domain.Notifications;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Notifications;

/// <summary>
/// Reads pending <see cref="ProductLowStock"/> rows from the outbox and fans each out to every enabled
/// notification channel. Idempotent: a message is "pending" only until a notification exists for it
/// (the in-app channel is the dedup marker), so re-runs never duplicate the feed. Driven by a background
/// worker in the host and directly by tests. Note: this reads the outbox WITHOUT touching its own
/// processed_at_utc (that belongs to the HQ-sync dispatcher) — the two consumers don't interfere.
/// </summary>
internal sealed class LowStockNotificationDispatcher : INotificationDispatcher
{
    private const int BatchSize = 200;
    private static readonly string LowStockEventType = typeof(ProductLowStock).FullName!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly PosDbContext _db;
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly ILogger<LowStockNotificationDispatcher> _log;

    public LowStockNotificationDispatcher(PosDbContext db, IEnumerable<INotificationChannel> channels,
        ILogger<LowStockNotificationDispatcher> log)
    {
        _db = db;
        _channels = channels;
        _log = log;
    }

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        // Low-stock outbox rows that haven't produced a notification yet (NOT EXISTS on source id).
        var pending = await _db.OutboxMessages
            .Where(m => m.EventType == LowStockEventType)
            .Where(m => !_db.Notifications.Any(n =>
                n.SourceMessageId == m.Id && n.TenantId == m.TenantId && n.StoreId == m.StoreId))
            .OrderBy(m => m.OccurredAtUtc).ThenBy(m => m.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        var channels = _channels.Where(c => c.IsEnabled).ToList();
        var dispatched = 0;

        foreach (var msg in pending)
        {
            var payload = JsonSerializer.Deserialize<LowStockPayload>(msg.Payload, Json);
            if (payload is null)
            {
                _log.LogWarning("notification.lowstock.skip unparseable payload msg={Id}", msg.Id);
                continue;
            }

            var message = new NotificationMessage(
                msg.TenantId, msg.StoreId, NotificationType.LowStock,
                Title: $"Low stock: {payload.Name}",
                Body: BuildBody(payload),
                ProductId: payload.ProductId,
                SourceMessageId: msg.Id);

            foreach (var channel in channels)
            {
                try { await channel.SendAsync(message, ct); }
                catch (Exception ex)
                {
                    _log.LogError(ex, "notification.channel.failed channel={Channel} msg={Id}", channel.Channel, msg.Id);
                }
            }
            dispatched++;
        }

        return dispatched;
    }

    private static string BuildBody(LowStockPayload p)
    {
        var unit = p.UnitOfMeasure == UnitOfMeasure.Kg ? " kg" : "";
        var body = $"On hand {Num(p.OnHand)}{unit}, at/below reorder level {Num(p.ReorderLevel)}{unit}.";
        if (p.ReorderQuantity is { } q)
            body += $" Suggested order: {Num(q)}{unit}.";
        return body;
    }

    private static string Num(decimal d) => d.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Just the fields we render — deserialized from the outbox JSON (numeric enums by web default).</summary>
    private sealed record LowStockPayload(
        Guid ProductId, string Sku, string Name, decimal OnHand,
        decimal ReorderLevel, decimal? ReorderQuantity, UnitOfMeasure UnitOfMeasure);
}
