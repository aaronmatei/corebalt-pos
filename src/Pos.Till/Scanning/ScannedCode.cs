namespace Pos.Till.Scanning;

public enum ScanKind
{
    /// <summary>Not a recognised symbology (e.g. a typed SKU or partial input).</summary>
    Unknown,

    /// <summary>A normal product barcode (GTIN-13/EAN-13/UPC) — look it up as-is.</summary>
    Gtin,

    /// <summary>
    /// An in-store / price-embedded EAN-13 (number-system digit 2). Supermarket scales print
    /// these for weighed goods: they encode a PLU and an embedded price-or-weight rather than a
    /// catalogue barcode. We don't decode them yet (that arrives with S2 weighed-goods + scales);
    /// today we surface the kind so the till can branch instead of doing a doomed GTIN lookup.
    /// </summary>
    PriceEmbeddedEan13
}

/// <summary>
/// Classifies a scanned/typed code so the till's scan handler can branch on it. Kept tiny and
/// pure (no I/O) so it's trivially testable and so the price-embedded path can grow into a full
/// PLU+weight decoder later without touching the view-model.
/// </summary>
public sealed record ScannedCode(string Raw, ScanKind Kind)
{
    /// <summary>For a price-embedded EAN-13, the 5-digit item/PLU block (positions 2-6). Null otherwise.</summary>
    public string? EmbeddedItemCode { get; init; }

    public static ScannedCode Parse(string? input)
    {
        var raw = (input ?? string.Empty).Trim();
        if (raw.Length == 0) return new ScannedCode(raw, ScanKind.Unknown);

        var isEan13 = raw.Length == 13 && raw.All(char.IsDigit);
        if (!isEan13) return new ScannedCode(raw, ScanKind.Unknown);

        // Number-system digit 2 ⇒ in-store / price-embedded range.
        if (raw[0] == '2')
        {
            return new ScannedCode(raw, ScanKind.PriceEmbeddedEan13)
            {
                EmbeddedItemCode = raw.Substring(1, 5)
            };
        }

        return new ScannedCode(raw, ScanKind.Gtin);
    }
}
