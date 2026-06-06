using Pos.Application.Abstractions;
using Pos.Domain.Sales;

namespace Pos.Application.Sales.Commands;

public sealed class StartSaleHandler
{
    private readonly ICurrentContext _ctx;
    private readonly ISaleRepository _sales;
    private readonly IUnitOfWork _uow;

    public StartSaleHandler(ICurrentContext ctx, ISaleRepository sales, IUnitOfWork uow)
    { _ctx = ctx; _sales = sales; _uow = uow; }

    public async Task<StartSaleResult> HandleAsync(StartSaleCommand cmd, CancellationToken ct = default)
    {
        var sale = Sale.Start(_ctx.TenantId, _ctx.StoreId, cmd.RegisterId, _ctx.UserId, cmd.Currency);
        await _sales.AddAsync(sale, ct);
        await _uow.SaveChangesAsync(ct);
        return new StartSaleResult(sale.Id);
    }
}
