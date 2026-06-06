using Pos.Domain.Catalog;

namespace Pos.Application.Receipts;

/// <summary>
/// Receipt rendering options. Width is configurable (48 cols = 80mm, 32 cols = 58mm). The tax-code
/// letter per class is configurable too — these are PROVISIONAL placeholders; confirm the exact KRA
/// eTIMS tax codes during eTIMS integration.
/// </summary>
public sealed class ReceiptOptions
{
    public int DefaultColumns { get; set; } = 48;

    public IReadOnlyDictionary<TaxClass, string> TaxCodes { get; set; } = new Dictionary<TaxClass, string>
    {
        [TaxClass.StandardRated] = "A",
        [TaxClass.ZeroRated] = "B",
        [TaxClass.Exempt] = "C",
    };

    public string TaxCode(TaxClass taxClass) => TaxCodes.TryGetValue(taxClass, out var c) ? c : "?";

    public static string ClassLabel(TaxClass taxClass) => taxClass switch
    {
        TaxClass.StandardRated => $"{KraVat.StandardRatePercent:0}%",
        TaxClass.ZeroRated => "0%",
        TaxClass.Exempt => "Exempt",
        _ => taxClass.ToString(),
    };
}
