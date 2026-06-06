using Pos.Application.Abstractions;

namespace Pos.Application.Inventory.Queries;

public sealed class GetStockOnHandHandler
{
    private readonly ICurrentContext _ctx;
    private readonly IStockMovementRepository _stock;

    public GetStockOnHandHandler(ICurrentContext ctx, IStockMovementRepository stock)
    { _ctx = ctx; _stock = stock; }

    public async Task<GetStockOnHandResult> HandleAsync(GetStockOnHandQuery q, CancellationToken ct = default)
    {
        var onHand = await _stock.GetOnHandAsync(_ctx.TenantId, _ctx.StoreId, q.ProductId, ct);
        return new GetStockOnHandResult(q.ProductId, onHand);
    }
}
