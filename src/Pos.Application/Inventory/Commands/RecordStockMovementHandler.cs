using Pos.Application.Abstractions;
using Pos.Domain.Inventory;

namespace Pos.Application.Inventory.Commands;

public sealed class RecordStockMovementHandler
{
    private readonly ICurrentContext _ctx;
    private readonly IStockMovementRepository _stock;
    private readonly IUnitOfWork _uow;

    public RecordStockMovementHandler(ICurrentContext ctx, IStockMovementRepository stock, IUnitOfWork uow)
    { _ctx = ctx; _stock = stock; _uow = uow; }

    public async Task<RecordStockMovementResult> HandleAsync(RecordStockMovementCommand cmd, CancellationToken ct = default)
    {
        var movement = StockMovement.Record(
            _ctx.TenantId, _ctx.StoreId, cmd.ProductId,
            cmd.QuantityDelta, cmd.Reason, cmd.SourceRef);

        await _stock.AddAsync(movement, ct);
        await _uow.SaveChangesAsync(ct);
        return new RecordStockMovementResult(movement.Id);
    }
}
