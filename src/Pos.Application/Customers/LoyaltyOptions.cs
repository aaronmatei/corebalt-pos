namespace Pos.Application.Customers;

/// <summary>
/// Loyalty accrual rule. One point per <see cref="CurrencyPerPoint"/> of VAT-inclusive spend (default
/// 1 point per 100 KES), floored. Bound from config "Loyalty"; per-tenant tuning lands with the wider
/// promotions module (roadmap S4). Set <see cref="Enabled"/> false to stop accrual without code changes.
/// </summary>
public sealed class LoyaltyOptions
{
    public bool Enabled { get; set; } = true;
    public decimal CurrencyPerPoint { get; set; } = 100m;

    /// <summary>Points earned on a sale of <paramref name="grandTotal"/> (0 if disabled or misconfigured).</summary>
    public int PointsFor(decimal grandTotal)
    {
        if (!Enabled || CurrencyPerPoint <= 0m || grandTotal <= 0m) return 0;
        return (int)Math.Floor(grandTotal / CurrencyPerPoint);
    }
}
