using Pos.Application.Abstractions;
using Pos.SharedKernel;

namespace Pos.Application.Sales.Commands;

public sealed class AddTenderHandler
{
    private readonly ICurrentContext _ctx;
    private readonly ISaleRepository _sales;
    private readonly IUnitOfWork _uow;

    public AddTenderHandler(ICurrentContext ctx, ISaleRepository sales, IUnitOfWork uow)
    { _ctx = ctx; _sales = sales; _uow = uow; }

    public async Task HandleAsync(AddTenderCommand cmd, CancellationToken ct = default)
    {
        var sale = await _sales.GetAsync(_ctx.TenantId, _ctx.StoreId, cmd.SaleId, ct)
            ?? throw new InvalidOperationException($"Sale {cmd.SaleId} not found in this store.");

        sale.AddTender(cmd.Type, new Money(cmd.Amount, sale.Currency), cmd.Reference);
        await _uow.SaveChangesAsync(ct);
    }
}
