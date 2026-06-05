using Pos.SharedKernel;

namespace Pos.Domain.Sales;

public sealed class Tender : Entity
{
    public TenderType Type { get; private set; }
    public Money Amount { get; private set; }
    public string? Reference { get; private set; } // e.g. the M-Pesa confirmation code

    private Tender() { Amount = Money.Zero(); } // EF

    internal Tender(Guid id, TenderType type, Money amount, string? reference)
        : base(id)
    {
        Type = type;
        Amount = amount;
        Reference = reference;
    }
}
