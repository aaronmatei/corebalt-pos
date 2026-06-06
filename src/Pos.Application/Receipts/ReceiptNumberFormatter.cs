using System.Globalization;

namespace Pos.Application.Receipts;

/// <summary>
/// The ONE place receipt numbers are formatted for display: a branch prefix + zero-padded sequence,
/// e.g. "MB-000123". Branch code + pad width are config-driven.
/// </summary>
public sealed class ReceiptNumberFormatter
{
    private readonly string _branchCode;
    private readonly int _padWidth;

    public ReceiptNumberFormatter(string branchCode, int padWidth = 6)
    {
        _branchCode = string.IsNullOrWhiteSpace(branchCode) ? "POS" : branchCode.Trim();
        _padWidth = padWidth < 1 ? 6 : padWidth;
    }

    public string Format(long sequence) =>
        $"{_branchCode}-{sequence.ToString(CultureInfo.InvariantCulture).PadLeft(_padWidth, '0')}";
}
