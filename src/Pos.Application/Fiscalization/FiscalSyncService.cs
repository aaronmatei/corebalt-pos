using Pos.Application.Abstractions;
using Pos.Application.Receipts;
using Pos.Application.Sales;
using Pos.Domain.Sales;

namespace Pos.Application.Fiscalization;

/// <summary>
/// One pass of the eTIMS sync seam: find Signed-but-not-Synced sales, transmit each to KRA via
/// SyncAsync, and flip to Synced on success — or count the failure and flip to Failed once the
/// attempt budget is spent. The BackgroundService just calls this on an interval (which provides the
/// retry backoff); extracted so tests can drive it deterministically. Fake provider = instant success.
/// </summary>
public sealed class FiscalSyncService
{
    private const int BatchSize = 50;

    private readonly ISaleRepository _sales;
    private readonly IFiscalizationProvider _provider;
    private readonly EtimsOptions _options;
    private readonly StoreInfo _store;
    private readonly IClock _clock;
    private readonly IUnitOfWork _uow;

    public FiscalSyncService(ISaleRepository sales, IFiscalizationProvider provider, EtimsOptions options,
        StoreInfo store, IClock clock, IUnitOfWork uow)
    {
        _sales = sales;
        _provider = provider;
        _options = options;
        _store = store;
        _clock = clock;
        _uow = uow;
    }

    /// <summary>Returns the number of sales transmitted in this pass.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled) return 0;

        var pending = await _sales.ListByFiscalStatusAsync(FiscalStatus.Signed, BatchSize, ct);
        var synced = 0;
        foreach (var sale in pending)
        {
            FiscalizationResult result;
            try
            {
                result = await _provider.SyncAsync(FiscalInvoice.From(sale, _store.KraPin), ct);
            }
            catch (Exception ex)
            {
                result = FiscalizationResult.Fail("sync.error", ex.Message);
            }

            if (result.Success)
            {
                sale.MarkFiscalSynced(_clock.UtcNow);
                synced++;
            }
            else
            {
                sale.RecordFiscalSyncFailure(_options.SyncMaxAttempts);
            }
            await _uow.SaveChangesAsync(ct);
        }
        return synced;
    }
}
