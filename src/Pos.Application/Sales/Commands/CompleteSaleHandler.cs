using Pos.Application.Abstractions;
using Pos.Application.Inventory;
using Pos.Domain.Inventory;

namespace Pos.Application.Sales.Commands;

/// <summary>
/// Completing a sale also writes one negative-delta stock movement per line, in the SAME
/// unit of work. Atomic write is what lets the append-only inventory invariant survive
/// crash-during-checkout: either the sale + its movements are both committed, or neither.
/// </summary>
public sealed class CompleteSaleHandler
{
    private readonly ICurrentContext _ctx;
    private readonly ISaleRepository _sales;
    private readonly IStockMovementRepository _stock;
    private readonly IUnitOfWork _uow;

    public CompleteSaleHandler(ICurrentContext ctx, ISaleRepository sales,
        IStockMovementRepository stock, IUnitOfWork uow)
    { _ctx = ctx; _sales = sales; _stock = stock; _uow = uow; }

    public async Task<CompleteSaleResult> HandleAsync(CompleteSaleCommand cmd, CancellationToken ct = default)
    {
        var sale = await _sales.GetAsync(_ctx.TenantId, _ctx.StoreId, cmd.SaleId, ct)
            ?? throw new InvalidOperationException($"Sale {cmd.SaleId} not found in this store.");

        sale.Complete();

        var movements = sale.Lines.Select(line => StockMovement.Record(
            _ctx.TenantId, _ctx.StoreId, line.ProductId,
            -line.Quantity, StockMovementReason.Sale, sourceRef: sale.Id));
        await _stock.AddRangeAsync(movements, ct);

        await _uow.SaveChangesAsync(ct);

        var change = sale.BalanceDue.Amount < 0 ? -sale.BalanceDue.Amount : 0m;
        return new CompleteSaleResult(sale.Id, sale.Subtotal.Amount, change, sale.Currency);
    }
}
