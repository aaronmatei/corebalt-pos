using Pos.Application.Abstractions;
using Pos.Application.Catalog;
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
    private readonly IProductRepository _products;

    public SaleCompletion(IReceiptNumberSequence sequence, ReceiptNumberFormatter formatter,
        IStockMovementRepository stock, IProductRepository products)
    {
        _sequence = sequence;
        _formatter = formatter;
        _stock = stock;
        _products = products;
    }

    public async Task FinalizeAsync(Sale sale, CancellationToken ct = default)
    {
        var seq = await _sequence.NextAsync(sale.TenantId, sale.StoreId, ct);
        sale.AssignReceiptNumber(_formatter.Format(seq));
        sale.Complete();

        var movements = sale.Lines.Select(l => StockMovement.Record(
            sale.TenantId, sale.StoreId, l.ProductId, -l.Quantity, StockMovementReason.Sale, sourceRef: sale.Id));
        await _stock.AddRangeAsync(movements, ct);

        // Reorder check, in the SAME transaction as the movements: per product, on-hand AFTER this sale =
        // current committed on-hand − quantity sold here (the movements above aren't flushed yet). A
        // downward crossing to/below the reorder level raises ProductLowStock on the (tracked) product,
        // which the caller's SaveChanges drains to the outbox alongside the sale — never blocking checkout.
        var soldByProduct = sale.Lines
            .GroupBy(l => l.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        foreach (var (productId, quantitySold) in soldByProduct)
        {
            var product = await _products.GetAsync(sale.TenantId, sale.StoreId, productId, ct);
            if (product is null) continue;
            var onHandBefore = await _stock.GetOnHandAsync(sale.TenantId, sale.StoreId, productId, ct);
            product.EvaluateReorder(onHandBefore - quantitySold);
        }
    }
}
