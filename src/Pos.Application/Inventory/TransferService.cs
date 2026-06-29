using Pos.Application.Abstractions;
using Pos.Application.Catalog;
using Pos.Domain.Inventory;

namespace Pos.Application.Inventory;

public sealed record TransferLineInput(Guid ProductId, decimal Quantity);

/// <summary>
/// Source-branch side of inter-branch transfers (M3). Dispatching writes the immutable
/// <see cref="StockTransfer"/> + a reversing <see cref="StockMovementReason.TransferOut"/> movement per
/// line, in one transaction — so on-hand at the source drops and the event is queued to ship to HQ. The
/// destination records its own TransferIn when it pulls and receives (never written from here).
/// </summary>
public sealed class TransferService
{
    private readonly ICurrentContext _ctx;
    private readonly IProductRepository _products;
    private readonly ITransferRepository _transfers;
    private readonly IStockMovementRepository _movements;
    private readonly IUnitOfWork _uow;

    public TransferService(ICurrentContext ctx, IProductRepository products, ITransferRepository transfers,
        IStockMovementRepository movements, IUnitOfWork uow)
    {
        _ctx = ctx;
        _products = products;
        _transfers = transfers;
        _movements = movements;
        _uow = uow;
    }

    public async Task<StockTransfer> DispatchAsync(Guid toStoreId, string toStoreName,
        IReadOnlyList<TransferLineInput> lines, string? note, CancellationToken ct = default)
    {
        var tenant = _ctx.TenantId;
        var from = _ctx.StoreId;

        var resolved = new List<(Guid, string, string, decimal)>(lines.Count);
        foreach (var l in lines)
        {
            var p = await _products.GetAsync(tenant, from, l.ProductId, ct)
                ?? throw new InvalidOperationException($"Product {l.ProductId} is not in this branch's catalogue.");
            resolved.Add((p.Id, p.Sku, p.Name, l.Quantity));
        }

        var transfer = StockTransfer.Dispatch(tenant, from, toStoreId, toStoreName, _ctx.UserId, _ctx.UserName, resolved, note);
        await _transfers.AddAsync(transfer, ct);

        var movements = transfer.Lines.Select(l =>
            StockMovement.Record(tenant, from, l.ProductId, -l.Quantity, StockMovementReason.TransferOut, sourceRef: transfer.Id));
        await _movements.AddRangeAsync(movements, ct);

        await _uow.SaveChangesAsync(ct);
        return transfer;
    }

    public Task<IReadOnlyList<StockTransfer>> ListRecentAsync(int take = 50, CancellationToken ct = default) =>
        _transfers.ListRecentAsync(_ctx.TenantId, _ctx.StoreId, take, ct);
}
