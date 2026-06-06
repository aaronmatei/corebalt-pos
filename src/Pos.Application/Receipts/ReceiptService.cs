using Pos.Application.Abstractions;
using Pos.Application.Sales;
using Pos.Domain.Sales;

namespace Pos.Application.Receipts;

/// <summary>
/// Builds the receipt for a completed sale: loads the persisted Sale (tenant/store-scoped), projects
/// it + Store config into a <see cref="ReceiptModel"/>, and renders the fixed-width text + HTML.
/// Reads only persisted values — VAT/totals are never recomputed here.
/// </summary>
public sealed class ReceiptService
{
    private readonly ICurrentContext _ctx;
    private readonly ISaleRepository _sales;
    private readonly StoreInfo _store;
    private readonly ReceiptOptions _options;

    public ReceiptService(ICurrentContext ctx, ISaleRepository sales, StoreInfo store, ReceiptOptions options)
    {
        _ctx = ctx;
        _sales = sales;
        _store = store;
        _options = options;
    }

    /// <summary>Returns the receipt, or null if the sale doesn't exist in this store.</summary>
    public async Task<ReceiptResult?> GetAsync(Guid saleId, int? columns, CancellationToken ct = default)
    {
        var sale = await _sales.GetAsync(_ctx.TenantId, _ctx.StoreId, saleId, ct);
        if (sale is null) return null;
        if (sale.Status != SaleStatus.Completed)
            throw new InvalidOperationException("A receipt is only available for a completed sale.");

        var cols = columns is > 0 ? columns.Value : _options.DefaultColumns;
        var model = ReceiptModel.From(sale, _store, _options);
        return new ReceiptResult(model, ReceiptTextRenderer.Render(model, cols), ReceiptHtmlRenderer.Render(model), cols);
    }
}

public sealed record ReceiptResult(ReceiptModel Model, string Text, string Html, int Columns);
