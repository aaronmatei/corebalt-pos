namespace Pos.Domain.Catalog;

/// <summary>
/// KRA VAT treatment of a product. Kenyan retail prices are VAT-INCLUSIVE, so for a
/// <see cref="StandardRated"/> line the shelf/unit price already contains 16% VAT (it is backed out
/// at checkout, never added on top). <see cref="ZeroRated"/> and <see cref="Exempt"/> carry no VAT.
/// </summary>
public enum TaxClass
{
    StandardRated = 0, // 16%
    ZeroRated = 1,     // 0%
    Exempt = 2
}

/// <summary>KRA VAT rates. The standard rate is a domain constant until per-class rates become configurable.</summary>
public static class KraVat
{
    public const decimal StandardRatePercent = 16m;

    public static decimal RatePercent(TaxClass taxClass) =>
        taxClass == TaxClass.StandardRated ? StandardRatePercent : 0m;
}
