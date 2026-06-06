using Pos.Application.Abstractions;
using Pos.Application.Sales;
using Pos.Application.Tenancy;
using Pos.Domain.Sales;

namespace Pos.Application.Fiscalization;

/// <summary>
/// One pass of the eTIMS sync seam: find Signed-but-not-Synced sales and transmit each via SyncAsync,
/// per the SALE'S TENANT eTIMS settings (the worker has no request context). Flips to Synced on
/// success, or counts the failure and flips to Failed once the attempt budget is spent.
/// </summary>
public sealed class FiscalSyncService
{
    private const int BatchSize = 50;

    private readonly ISaleRepository _sales;
    private readonly IFiscalizationProvider _provider;
    private readonly IEtimsSettingsRepository _etims;
    private readonly IMerchantProfileRepository _merchants;
    private readonly EtimsWorkerOptions _worker;
    private readonly IClock _clock;
    private readonly IUnitOfWork _uow;

    public FiscalSyncService(ISaleRepository sales, IFiscalizationProvider provider, IEtimsSettingsRepository etims,
        IMerchantProfileRepository merchants, EtimsWorkerOptions worker, IClock clock, IUnitOfWork uow)
    {
        _sales = sales;
        _provider = provider;
        _etims = etims;
        _merchants = merchants;
        _worker = worker;
        _clock = clock;
        _uow = uow;
    }

    /// <summary>Returns the number of sales transmitted in this pass.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var pending = await _sales.ListByFiscalStatusAsync(FiscalStatus.Signed, BatchSize, ct);
        var synced = 0;
        foreach (var sale in pending)
        {
            var etims = await _etims.GetAsync(sale.TenantId, ct);
            if (etims is null || !etims.Enabled) continue;

            var sellerPin = (await _merchants.GetAsync(sale.TenantId, ct))?.KraPin ?? "";
            FiscalizationResult result;
            try
            {
                result = await _provider.SyncAsync(FiscalInvoice.From(sale, sellerPin), ct);
            }
            catch (Exception ex)
            {
                result = FiscalizationResult.Fail("sync.error", ex.Message);
            }

            if (result.Success) { sale.MarkFiscalSynced(_clock.UtcNow); synced++; }
            else sale.RecordFiscalSyncFailure(_worker.MaxAttempts);
            await _uow.SaveChangesAsync(ct);
        }
        return synced;
    }
}
