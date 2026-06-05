namespace Pos.SharedKernel;

/// <summary>Money as a value object. Defaults to KES; rounds to 2 dp (banker's rounding).</summary>
public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency = "KES")
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new ArgumentException("Currency must be a 3-letter ISO code.", nameof(currency));
        Amount = decimal.Round(amount, 2, MidpointRounding.ToEven);
        Currency = currency.ToUpperInvariant();
    }

    public static Money Zero(string currency = "KES") => new(0m, currency);

    public Money Add(Money other)      { Ensure(other); return new(Amount + other.Amount, Currency); }
    public Money Subtract(Money other) { Ensure(other); return new(Amount - other.Amount, Currency); }
    public Money Multiply(decimal qty) => new(Amount * qty, Currency);

    private void Ensure(Money o)
    {
        if (o.Currency != Currency)
            throw new InvalidOperationException($"Currency mismatch: {Currency} vs {o.Currency}.");
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Currency} {Amount:0.00}";
}
