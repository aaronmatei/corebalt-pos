using Pos.Application.Abstractions;
using Pos.Application.Cash;
using Pos.Application.Receipts;
using Pos.Application.Tenancy;
using Pos.Domain.Tenancy;

namespace Pos.Application.Printing;

/// <summary>
/// Ties the receipt pipeline together: load the persisted ReceiptModel + the register's PrinterProfile
/// + the client's logo, build ESC/POS, send to the configured printer, and render the PNG preview.
/// Printing failures never break the sale/return (logged, swallowed). Reprints use the persisted model
/// → byte-identical.
/// </summary>
public sealed class ReceiptOutputService
{
    private readonly ICurrentContext _ctx;
    private readonly ReceiptService _receipts;
    private readonly IPrinterProfileRepository _profiles;
    private readonly IMerchantProfileRepository _merchants;
    private readonly IEscPosBuilder _builder;
    private readonly IReceiptPreviewRenderer _preview;
    private readonly IReceiptPrinter _printer;
    private readonly BrandAssets _brand;

    public ReceiptOutputService(ICurrentContext ctx, ReceiptService receipts, IPrinterProfileRepository profiles,
        IMerchantProfileRepository merchants, IEscPosBuilder builder, IReceiptPreviewRenderer preview,
        IReceiptPrinter printer, BrandAssets brand)
    {
        _ctx = ctx;
        _receipts = receipts;
        _profiles = profiles;
        _merchants = merchants;
        _builder = builder;
        _preview = preview;
        _printer = printer;
        _brand = brand;
    }

    public async Task PrintSaleAsync(Guid registerId, Guid saleId, CancellationToken ct = default)
    {
        var r = await _receipts.GetAsync(saleId, null, ct);
        if (r is not null) await PrintAsync(registerId, r.Model, ct);
    }

    public async Task PrintReturnAsync(Guid registerId, Guid creditNoteId, CancellationToken ct = default)
    {
        var r = await _receipts.GetReturnAsync(creditNoteId, null, ct);
        if (r is not null) await PrintAsync(registerId, r.Model, ct);
    }

    public async Task<byte[]?> PreviewSaleAsync(Guid saleId, PaperWidth paper, CancellationToken ct = default)
    {
        var r = await _receipts.GetAsync(saleId, null, ct);
        return r is null ? null : _preview.RenderPng(r.Model, ProfileFor(paper), await LoadLogoAsync(ct));
    }

    public async Task<byte[]?> PreviewReturnAsync(Guid creditNoteId, PaperWidth paper, CancellationToken ct = default)
    {
        var r = await _receipts.GetReturnAsync(creditNoteId, null, ct);
        return r is null ? null : _preview.RenderPng(r.Model, ProfileFor(paper), await LoadLogoAsync(ct));
    }

    /// <summary>Print an X/Z report on the register's printer (reuses the ESC/POS + printer pipeline).</summary>
    public async Task PrintShiftReportAsync(Guid registerId, ShiftReport report, CancellationToken ct = default)
    {
        var profile = await _profiles.GetByRegisterAsync(_ctx.TenantId, registerId, ct)
            ?? PrinterProfile.Default(_ctx.TenantId, registerId);
        try
        {
            var text = ShiftReportTextRenderer.Render(report, profile.Columns);
            await _printer.PrintAsync(_builder.BuildText(text, profile), profile, ct);
        }
        catch
        {
            // A printer/transport failure must never break the cash-up — swallow it.
        }
    }

    private async Task PrintAsync(Guid registerId, ReceiptModel model, CancellationToken ct)
    {
        var profile = await _profiles.GetByRegisterAsync(_ctx.TenantId, registerId, ct)
            ?? PrinterProfile.Default(_ctx.TenantId, registerId);
        try
        {
            var bytes = _builder.Build(model, profile, await LoadLogoAsync(ct), _brand.PoweredByMark);
            await _printer.PrintAsync(bytes, profile, ct);
        }
        catch
        {
            // A printer/transport failure must never break the sale or return — swallow it.
        }
    }

    private PrinterProfile ProfileFor(PaperWidth paper)
    {
        var p = PrinterProfile.Create(_ctx.TenantId, _ctx.StoreId);
        p.Configure(PrinterTransport.Null, null, 9100, null, paper, hasCutter: false, hasCashDrawer: false, nativeQrSupported: false);
        return p;
    }

    /// <summary>The CLIENT's logo from MerchantProfile, if it points at a readable local file; else null
    /// (degrade to a text header). Corebalt's mark is NEVER used here.</summary>
    private async Task<byte[]?> LoadLogoAsync(CancellationToken ct)
    {
        var url = (await _merchants.GetAsync(_ctx.TenantId, ct))?.LogoUrl;
        if (!string.IsNullOrWhiteSpace(url) && File.Exists(url))
        {
            try { return await File.ReadAllBytesAsync(url, ct); }
            catch { return null; }
        }
        return null;
    }
}
