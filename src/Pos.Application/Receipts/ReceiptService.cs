using Pos.Application.Abstractions;
using Pos.Application.Sales;
using Pos.Application.Tenancy;
using Pos.Domain.Sales;

namespace Pos.Application.Receipts;

/// <summary>
/// Builds the receipt for a sale or credit note: loads the persisted document (tenant/store-scoped),
/// projects it + the CLIENT's <see cref="StoreInfo"/> (from their DB-backed MerchantProfile, not
/// appsettings) into a <see cref="ReceiptModel"/>, and renders fixed-width text + HTML.
/// </summary>
public sealed class ReceiptService
{
    private readonly ICurrentContext _ctx;
    private readonly ISaleRepository _sales;
    private readonly ICreditNoteRepository _creditNotes;
    private readonly IMerchantProfileRepository _merchants;
    private readonly ReceiptOptions _options;

    public ReceiptService(ICurrentContext ctx, ISaleRepository sales, ICreditNoteRepository creditNotes,
        IMerchantProfileRepository merchants, ReceiptOptions options)
    {
        _ctx = ctx;
        _sales = sales;
        _creditNotes = creditNotes;
        _merchants = merchants;
        _options = options;
    }

    public async Task<ReceiptResult?> GetAsync(Guid saleId, int? columns, CancellationToken ct = default)
    {
        var sale = await _sales.GetAsync(_ctx.TenantId, _ctx.StoreId, saleId, ct);
        if (sale is null) return null;
        if (sale.Status != SaleStatus.Completed)
            throw new InvalidOperationException("A receipt is only available for a completed sale.");

        var store = await StoreAsync(ct);
        var cols = columns is > 0 ? columns.Value : _options.DefaultColumns;
        var model = ReceiptModel.From(sale, store, _options);
        return new ReceiptResult(model, ReceiptTextRenderer.Render(model, cols), ReceiptHtmlRenderer.Render(model), cols);
    }

    /// <summary>The credit-note (return/refund) receipt, or null if the credit note isn't in this store.</summary>
    public async Task<ReceiptResult?> GetReturnAsync(Guid creditNoteId, int? columns, CancellationToken ct = default)
    {
        var note = await _creditNotes.GetAsync(_ctx.TenantId, _ctx.StoreId, creditNoteId, ct);
        if (note is null) return null;

        var store = await StoreAsync(ct);
        var cols = columns is > 0 ? columns.Value : _options.DefaultColumns;
        var model = ReceiptModel.FromCreditNote(note, store, _options);
        return new ReceiptResult(model, ReceiptTextRenderer.Render(model, cols), ReceiptHtmlRenderer.Render(model), cols);
    }

    private async Task<StoreInfo> StoreAsync(CancellationToken ct)
    {
        var profile = await _merchants.GetAsync(_ctx.TenantId, ct);
        return profile is null ? StoreInfo.Unconfigured() : StoreInfo.From(profile, _ctx.StoreId);
    }
}

public sealed record ReceiptResult(ReceiptModel Model, string Text, string Html, int Columns);
