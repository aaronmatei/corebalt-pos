using Pos.Application.Abstractions;
using Pos.Application.Sales;
using Pos.Application.Tenancy;
using Pos.Domain.Sales;

namespace Pos.Application.Fiscalization;

/// <summary>
/// Fiscalizes a completed sale using the SALE'S TENANT settings (loaded by explicit tenant id, so this
/// works on the unauthenticated M-Pesa callback path too). If the tenant's eTIMS is enabled, sign via
/// the provider and persist the CUIN/QR; idempotent (never re-signs). Run AFTER the sale commits.
/// </summary>
public sealed class FiscalizationService
{
    private readonly ISaleRepository _sales;
    private readonly IFiscalizationProvider _provider;
    private readonly IEtimsSettingsRepository _etims;
    private readonly IMerchantProfileRepository _merchants;
    private readonly IUnitOfWork _uow;

    public FiscalizationService(ISaleRepository sales, IFiscalizationProvider provider,
        IEtimsSettingsRepository etims, IMerchantProfileRepository merchants, IUnitOfWork uow)
    {
        _sales = sales;
        _provider = provider;
        _etims = etims;
        _merchants = merchants;
        _uow = uow;
    }

    public async Task FiscalizeAsync(Guid tenantId, Guid storeId, Guid saleId, CancellationToken ct = default)
    {
        var etims = await _etims.GetAsync(tenantId, ct);
        if (etims is null || !etims.Enabled) return; // disabled → sale stays NotRequired

        var sale = await _sales.GetAsync(tenantId, storeId, saleId, ct);
        if (sale is null || sale.Status != SaleStatus.Completed) return;
        if (sale.IsFiscalized) return; // idempotent: already signed → never re-sign on reprint

        var sellerPin = (await _merchants.GetAsync(tenantId, ct))?.KraPin ?? "";
        var result = await _provider.SignAsync(FiscalInvoice.From(sale, sellerPin), ct);

        if (result.Success && result.Cuin is not null && result.SignedAtUtc is not null)
            sale.ApplyFiscalSignature(result.Cuin, result.Signature ?? "", result.QrData ?? "", result.SignedAtUtc.Value);
        else
            sale.MarkFiscalFailed();

        await _uow.SaveChangesAsync(ct);
    }
}
