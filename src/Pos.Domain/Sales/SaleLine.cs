using Pos.SharedKernel;

namespace Pos.Domain.Sales;

public sealed class SaleLine : Entity
{
    public Guid ProductId { get; private set; }
    public string Description { get; private set; }
    public decimal Quantity { get; private set; }   // decimal: supports weighed goods (e.g. 0.450 kg)
    public Money UnitPrice { get; private set; }
    public Money LineTotal => UnitPrice.Multiply(Quantity);

    private SaleLine() { Description = string.Empty; UnitPrice = Money.Zero(); } // EF

    internal SaleLine(Guid id, Guid productId, string description, decimal quantity, Money unitPrice)
        : base(id)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        ProductId = productId;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }
}
