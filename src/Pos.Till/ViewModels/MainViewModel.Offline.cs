using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Pos.Till.Api;

namespace Pos.Till.ViewModels;

/// <summary>
/// Offline-first behaviour for the till: the connection indicator, the offline cash-sale queue, and the
/// background loop that drains the queue back to the store server once it is reachable again. Selling a
/// cash sale while offline writes it to <see cref="LocalStore"/> with its edge-generated UUIDv7; the drain
/// re-POSTs it to the idempotent <c>/sales/checkout</c>, so a sale is never lost and never double-charged.
/// M-Pesa is online-only (an STK push needs the network), so the offline path is cash-only.
/// </summary>
public partial class MainViewModel
{
    // Must match how PosApiClient serialises the wire DTOs (Web casing + string enums) so the queued
    // payload round-trips TenderType etc. exactly.
    private static readonly JsonSerializerOptions QueueJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private CancellationTokenSource? _drainCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionLabel))]
    [NotifyCanExecuteChangedFor(nameof(PayWithMpesaCommand))]
    private bool _isOnline = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionLabel))]
    private int _queuedCount;

    /// <summary>Header pill text: connection state plus how many offline sales are waiting to sync.</summary>
    public string ConnectionLabel => IsOnline
        ? (QueuedCount > 0 ? $"● Online — syncing {QueuedCount}" : "● Online")
        : (QueuedCount > 0 ? $"● Offline — {QueuedCount} to sync" : "● Offline");

    private void OnConnectivityChanged(bool online) => Post(() => IsOnline = online);

    /// <summary>Persist a cash sale taken while offline. Refuses an underpaid sale up front (the server
    /// would 409 on replay and it would get stuck), so only fully-tendered cash sales are queued.</summary>
    private async Task QueueOfflineSaleAsync(CheckoutRequestDto request, decimal subtotal)
    {
        if (CashAmount < subtotal)
        {
            StatusMessage = $"Offline: tender the full {_options.Currency} {subtotal:0.00} in cash " +
                            "(card/M-Pesa need the network).";
            return;
        }

        var payload = JsonSerializer.Serialize(request, QueueJson);
        await _local.EnqueueSaleAsync(request.SaleId, payload, DateTimeOffset.UtcNow);
        QueuedCount = await _local.QueuedCountAsync();

        var change = CashAmount - subtotal;
        LastSaleSummary =
            $"⏳ Offline sale {request.SaleId}\n" +
            $"   Total  {_options.Currency} {subtotal:0.00}\n" +
            $"   Change {_options.Currency} {change:0.00}\n" +
            "   Will sync + print when reconnected.";
        StatusMessage = $"Saved offline ({QueuedCount} to sync). Hand the customer their change; the receipt prints on sync.";
        ClearSaleAfterSuccess();
    }

    private void StartDrainLoop()
    {
        _drainCts?.Cancel();
        _drainCts = new CancellationTokenSource();
        _ = DrainLoopAsync(_drainCts.Token);
    }

    /// <summary>Every few seconds, if anything is queued, probe the server and drain when it answers.</summary>
    private async Task DrainLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
            catch (TaskCanceledException) { return; }

            int queued;
            try { queued = await _local.QueuedCountAsync(); }
            catch { continue; }
            if (queued == 0) continue;

            var online = await _api.PingAsync(ct);
            Post(() => _net.Report(online));
            if (!online) continue;

            await DrainQueueAsync(ct);
        }
    }

    private async Task DrainQueueAsync(CancellationToken ct)
    {
        foreach (var q in await _local.GetQueuedSalesAsync())
        {
            if (ct.IsCancellationRequested) return;

            CheckoutRequestDto? req;
            try { req = JsonSerializer.Deserialize<CheckoutRequestDto>(q.Payload, QueueJson); }
            catch { await _local.RemoveQueuedSaleAsync(q.SaleId); continue; } // corrupt row — drop it
            if (req is null) { await _local.RemoveQueuedSaleAsync(q.SaleId); continue; }

            var result = await _api.CheckoutAsync(req, ct);
            if (result.Ok)
            {
                // Idempotent on the sale id: safe to remove even if a prior attempt committed but the
                // response was lost (the server just returns the existing sale).
                await _local.RemoveQueuedSaleAsync(q.SaleId);
            }
            else if (result.StatusCode == 0)
            {
                Post(() => _net.Report(false)); // dropped again mid-drain — retry on the next tick
                break;
            }
            // else: a real rejection on replay (e.g. the shift was closed -> 409). Leave it queued and
            // visible ("N to sync") for a manager to reconcile; skip it so good sales still drain.
        }

        var remaining = await _local.QueuedCountAsync();
        Post(() => QueuedCount = remaining);
    }

    /// <summary>Marshal a state mutation onto the UI thread (the drain loop runs on the thread pool).</summary>
    private static void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
