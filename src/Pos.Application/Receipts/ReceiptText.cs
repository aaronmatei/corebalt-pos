using System.Globalization;

namespace Pos.Application.Receipts;

/// <summary>
/// Fixed-width monospace layout helpers shared by the thermal text renderer, the ESC/POS builder, and
/// the visual preview — so all three lay the paper out identically. Pure functions (deterministic).
/// </summary>
public static class ReceiptText
{
    public static string Rule(char c, int cols) => new(c, cols);

    public static string Center(string s, int cols)
    {
        s = Truncate(s, cols);
        var left = (cols - s.Length) / 2;
        return new string(' ', left) + s;
    }

    public static string LeftRight(string left, string right, int cols)
    {
        right ??= "";
        if (right.Length >= cols) return Truncate(right, cols);
        var maxLeft = cols - right.Length - 1;
        if (left.Length > maxLeft) left = Truncate(left, maxLeft);
        return left + new string(' ', cols - left.Length - right.Length) + right;
    }

    public static string Truncate(string s, int max) => s.Length <= max ? s : s[..Math.Max(0, max)];

    public static IEnumerable<string> CharWrap(string s, int cols)
    {
        if (string.IsNullOrEmpty(s)) yield break;
        for (var i = 0; i < s.Length; i += cols)
            yield return s.Substring(i, Math.Min(cols, s.Length - i));
    }

    public static IEnumerable<string> WrapCentered(string s, int cols)
    {
        if (string.IsNullOrWhiteSpace(s)) yield break;
        var line = new System.Text.StringBuilder();
        foreach (var w in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + w.Length > cols)
            {
                yield return Center(line.ToString(), cols);
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(w);
        }
        if (line.Length > 0) yield return Center(line.ToString(), cols);
    }

    public static string Money(decimal amount) => amount.ToString("#,##0.00", CultureInfo.InvariantCulture);
}
