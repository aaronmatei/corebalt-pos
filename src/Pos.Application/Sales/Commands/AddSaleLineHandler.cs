using Pos.Application.Abstractions;
using Pos.SharedKernel;

namespace Pos.Application.Sales.Commands;

public sealed class AddSaleLineHandler
{
    private readonly ICurrentContext _ctx;
    private readonly ISaleRepository _sales;
    private readonly IUnitOfWork _uow;

    public AddSaleLineHandler(ICurrentContext ctx, ISaleRepository sales, IUnitOfWork uow)
    { _ctx = ctx; _sales = sales; _uow = uow; }

    public async Task HandleAsync(AddSaleLineCommand cmd, CancellationToken ct = default)
    {
        var sale = await _sales.GetAsync(_ctx.TenantId, _ctx.StoreId, cmd.SaleId, ct)
            ?? throw new InvalidOperationException($"Sale {cmd.SaleId} not found in this store.");

        sale.AddLine(cmd.ProductId, cmd.Description, cmd.Quantity, new Money(cmd.UnitPrice, sale.Currency));
        await _uow.SaveChangesAsync(ct);
    }
}
