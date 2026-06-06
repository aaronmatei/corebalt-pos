using System.Text;

namespace Pos.Application.Receipts;

/// <summary>
/// Renders a <see cref="ReceiptModel"/> to a fixed-width monospace layout for an ESC/POS thermal
/// printer. Width is configurable: 48 cols (80mm) or 32 cols (58mm). Pure function of the model —
/// deterministic, so reprints are byte-identical.
/// </summary>
public static class ReceiptTextRenderer
{
    public static string Render(ReceiptModel m, int cols)
    {
        if (cols < 24) cols = 24;
        var sb = new StringBuilder();

        // ── Header (logo placeholder reserved) ──
        Line(sb, Rule('=', cols));
        Line(sb, Center("[LOGO]", cols));
        Line(sb, Center(m.Header.LegalName, cols));
        if (m.Header.BranchName.Length > 0) Line(sb, Center(m.Header.BranchName, cols));
        foreach (var l in WrapCentered(m.Header.BranchAddress, cols)) Line(sb, l);
        Line(sb, Center($"PIN: {m.Header.KraPin}", cols));
        Line(sb, Center($"VAT: {m.Header.VatNumber}", cols));
        Line(sb, Center($"Tel: {m.Header.Phone}", cols));
        Line(sb, Rule('=', cols));

        // ── Document title (credit note / refund) ──
        if (m.DocumentTitle.Length > 0)
        {
            Line(sb, Center("*** " + m.DocumentTitle + " ***", cols));
            if (!string.IsNullOrWhiteSpace(m.AgainstReceiptNo))
                Line(sb, Center($"Against receipt: {m.AgainstReceiptNo}", cols));
            Line(sb, Rule('-', cols));
        }

        // ── Meta ── (human Receipt No is what's printed; the UUIDv7 Ref stays in the model + HTML
        // preview for support lookups — it won't fit a 58mm line, so it's not printed on the thermal).
        Line(sb, LeftRight("Receipt No:", m.Meta.ReceiptNo, cols));
        Line(sb, $"Date: {m.Meta.DateTimeEat} EAT");
        Line(sb, LeftRight($"Cashier: {m.Meta.Cashier}", $"Till: {m.Meta.Register}", cols));
        Line(sb, Rule('-', cols));

        // ── Line items ──
        foreach (var it in m.Items)
        {
            Line(sb, LeftRight(Truncate(it.Description, cols - 2), it.TaxCode, cols));
            Line(sb, LeftRight("  " + it.QtyLine, ReceiptFormat.Money(it.LineTotal), cols));
        }
        Line(sb, Rule('-', cols));

        // ── VAT breakdown ──
        Line(sb, "VAT SUMMARY");
        foreach (var v in m.Vat)
        {
            Line(sb, $" {v.TaxCode}  {v.ClassLabel}");
            Line(sb, LeftRight($"   Net {ReceiptFormat.Money(v.Taxable)}", $"VAT {ReceiptFormat.Money(v.Vat)}", cols));
        }
        Line(sb, Rule('-', cols));

        // ── Totals ──
        Line(sb, LeftRight("SUBTOTAL (excl VAT)", ReceiptFormat.Money(m.Totals.Subtotal), cols));
        Line(sb, LeftRight("TOTAL VAT", ReceiptFormat.Money(m.Totals.TotalVat), cols));
        Line(sb, LeftRight("GRAND TOTAL", $"{m.Currency} {ReceiptFormat.Money(m.Totals.GrandTotal)}", cols));
        Line(sb, Rule('-', cols));

        // ── Tenders ──
        foreach (var t in m.Tenders)
        {
            Line(sb, LeftRight(t.Type, ReceiptFormat.Money(t.Amount), cols));
            if (!string.IsNullOrWhiteSpace(t.Reference)) Line(sb, $"  Ref: {t.Reference}");
        }
        if (m.Change > 0m) Line(sb, LeftRight("CHANGE", ReceiptFormat.Money(m.Change), cols));
        Line(sb, Rule('-', cols));

        // ── Buyer PIN (only if present) ──
        if (!string.IsNullOrWhiteSpace(m.BuyerPin)) Line(sb, $"Buyer PIN: {m.BuyerPin}");

        // ── Tax legend ──
        if (m.Legend.Count > 0)
            Line(sb, "Tax: " + string.Join("  ", m.Legend.Select(l => $"{l.Code}={l.Label}")));
        Line(sb, Rule('-', cols));

        // ── Fiscal block (own method so wiring eTIMS later only fills the fields) ──
        RenderFiscal(sb, m.Fiscal, cols);

        Line(sb, Rule('=', cols));
        Line(sb, Center("Thank you / Asante sana", cols));
        sb.Append(Rule('=', cols)); // final line, no trailing newline
        return sb.ToString();
    }

    private static void RenderFiscal(StringBuilder sb, ReceiptFiscal f, int cols)
    {
        if (f.Fiscalized)
        {
            Line(sb, Center("eTIMS FISCAL RECEIPT", cols));
            Line(sb, LeftRight("CU INV:", f.Cuin ?? "", cols));
            if (!string.IsNullOrWhiteSpace(f.OriginalCuin)) Line(sb, LeftRight("Orig CU INV:", f.OriginalCuin, cols));
            if (!string.IsNullOrWhiteSpace(f.SignedAtEat)) Line(sb, $"Signed: {f.SignedAtEat} EAT");
            if (!string.IsNullOrWhiteSpace(f.SyncedAtEat)) Line(sb, $"Synced: {f.SyncedAtEat} EAT");
            // Native ESC/POS QR raster comes with the printer driver; for now print the QR payload,
            // char-wrapped so the long URL still fits the paper width.
            Line(sb, "Scan to verify (QR):");
            foreach (var chunk in CharWrap(f.QrData ?? "", cols)) Line(sb, chunk);
        }
        else if (f.Status == "NotRequired")
        {
            Line(sb, Center("*** NON-FISCAL / TRAINING ***", cols));
        }
        else
        {
            Line(sb, Center(f.StatusText, cols)); // e.g. "eTIMS: NOT FISCALIZED"
        }
    }

    // ── fixed-width helpers (LF only, for byte-identical output across OSes) ──
    private static void Line(StringBuilder sb, string s) => sb.Append(s).Append('\n');

    private static string Rule(char c, int cols) => new(c, cols);

    private static string Center(string s, int cols)
    {
        s = Truncate(s, cols);
        var left = (cols - s.Length) / 2;
        return new string(' ', left) + s;
    }

    private static string LeftRight(string left, string right, int cols)
    {
        right ??= "";
        if (right.Length >= cols) return Truncate(right, cols);
        var maxLeft = cols - right.Length - 1;
        if (left.Length > maxLeft) left = Truncate(left, maxLeft);
        return left + new string(' ', cols - left.Length - right.Length) + right;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..Math.Max(0, max)];

    private static IEnumerable<string> CharWrap(string s, int cols)
    {
        if (string.IsNullOrEmpty(s)) yield break;
        for (var i = 0; i < s.Length; i += cols)
            yield return s.Substring(i, Math.Min(cols, s.Length - i));
    }

    private static IEnumerable<string> WrapCentered(string s, int cols)
    {
        if (string.IsNullOrWhiteSpace(s)) yield break;
        var line = new StringBuilder();
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
}
