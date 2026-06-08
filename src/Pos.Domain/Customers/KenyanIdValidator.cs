using System.Text.RegularExpressions;

namespace Pos.Domain.Customers;

/// <summary>
/// Lightweight format checks for Kenyan KYC fields captured on a <see cref="Customer"/>. Format only —
/// it does NOT verify the values exist with IPRS/KRA (that's an online integration for later). Used to
/// reject obviously-malformed input at the edge so reports/receipts stay clean.
/// </summary>
public static partial class KenyanIdValidator
{
    // KRA PIN: a leading A (individual) or P (company), 9 digits, then a check letter — e.g. A001234567Z.
    [GeneratedRegex("^[AP]\\d{9}[A-Z]$", RegexOptions.IgnoreCase)]
    private static partial Regex KraPinRegex();

    // National ID: digits only, historically up to 8 (allow 6–10 to be future-proof, e.g. Maisha numbers).
    [GeneratedRegex("^\\d{6,10}$")]
    private static partial Regex NationalIdRegex();

    public static bool IsValidKraPin(string? pin) => !string.IsNullOrWhiteSpace(pin) && KraPinRegex().IsMatch(pin.Trim());

    public static bool IsValidNationalId(string? id) => !string.IsNullOrWhiteSpace(id) && NationalIdRegex().IsMatch(id.Trim());

    /// <summary>Normalize a Kenyan mobile number to the canonical 2547######## / 2541######## form when possible.
    /// Falls back to the trimmed input for non-Kenyan or already-odd numbers (never throws).</summary>
    public static string NormalizePhone(string phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return (phone ?? "").Trim();
        if (digits.StartsWith("0") && digits.Length == 10) return "254" + digits[1..];   // 07.. / 01.. -> 2547.. / 2541..
        if (digits.StartsWith("254")) return digits;
        if (digits.Length == 9 && (digits[0] is '7' or '1')) return "254" + digits;        // 7######## / 1########
        return digits;
    }
}
