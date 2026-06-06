using Pos.Application.Abstractions;
using Pos.Application.Inventory;
using Pos.Application.Receipts;
using Pos.Domain.Inventory;
using Pos.Domain.Sales;

namespace Pos.Application.Sales;

/// <summary>
/// The shared finalization step for every checkout path (atomic cash, incremental, M-Pesa): stamp the
/// store-authoritative receipt number, complete the sale (freezing VAT/totals), and queue the
/// negative-delta stock movements. MUST be invoked inside the caller's transaction
/// (<see cref="IUnitOfWork.ExecuteInTransactionAsync"/>) so the receipt-number increment commits
/// atomically with the sale.
/// </summary>
public sealed class SaleCompletion
{
    private readonly IReceiptNumberSequence _sequence;
    private readonly ReceiptNumberFormatter _formatter;
    private readonly IStockMovementRepository _stock;

    public SaleCompletion(IReceiptNumberSequence sequence, ReceiptNumberFormatter formatter, IStockMovementRepository stock)
    {
        _sequence = sequence;
        _formatter = formatter;
        _stock = stock;
    }

    public async Task FinalizeAsync(Sale sale, CancellationToken ct = default)
    {
        var seq = await _sequence.NextAsync(sale.TenantId, sale.StoreId, ct);
        sale.AssignReceiptNumber(_formatter.Format(seq));
        sale.Complete();

        var movements = sale.Lines.Select(l => StockMovement.Record(
            sale.TenantId, sale.StoreId, l.ProductId, -l.Quantity, StockMovementReason.Sale, sourceRef: sale.Id));
        await _stock.AddRangeAsync(movements, ct);
    }
}
