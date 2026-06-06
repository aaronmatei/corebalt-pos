using Pos.Application.Abstractions;
using Pos.Application.Receipts;
using Pos.Application.Sales;
using Pos.Domain.Sales;

namespace Pos.Application.Fiscalization;

/// <summary>
/// Fiscalizes a completed sale: if eTIMS is enabled, project the persisted sale into a FiscalInvoice,
/// SignAsync via the provider, and persist the CUIN/signature/QR + FiscalStatus=Signed (so a receipt
/// fetched right after has the fiscal block filled). If disabled, the sale stays NotRequired (the
/// default). Idempotent — never re-signs (reprints don't fiscalize). Run AFTER the sale is committed;
/// signing is a provider call and must not hold the checkout transaction.
/// </summary>
public sealed class FiscalizationService
{
    private readonly ISaleRepository _sales;
    private readonly IFiscalizationProvider _provider;
    private readonly EtimsOptions _options;
    private readonly StoreInfo _store;
    private readonly IClock _clock;
    private readonly IUnitOfWork _uow;

    public FiscalizationService(ISaleRepository sales, IFiscalizationProvider provider, EtimsOptions options,
        StoreInfo store, IClock clock, IUnitOfWork uow)
    {
        _sales = sales;
        _provider = provider;
        _options = options;
        _store = store;
        _clock = clock;
        _uow = uow;
    }

    public async Task FiscalizeAsync(Guid tenantId, Guid storeId, Guid saleId, CancellationToken ct = default)
    {
        if (!_options.Enabled) return; // sale stays NotRequired (default) — nothing to persist

        var sale = await _sales.GetAsync(tenantId, storeId, saleId, ct);
        if (sale is null || sale.Status != SaleStatus.Completed) return;
        if (sale.IsFiscalized) return; // idempotent: already signed → never re-sign on reprint

        var invoice = FiscalInvoice.From(sale, _store.KraPin);
        var result = await _provider.SignAsync(invoice, ct);

        if (result.Success && result.Cuin is not null && result.SignedAtUtc is not null)
            sale.ApplyFiscalSignature(result.Cuin, result.Signature ?? "", result.QrData ?? "", result.SignedAtUtc.Value);
        else
            sale.MarkFiscalFailed();

        await _uow.SaveChangesAsync(ct);
    }
}
