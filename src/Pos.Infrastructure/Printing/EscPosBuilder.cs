using System.Text;
using Pos.Application.Printing;
using Pos.Application.Receipts;
using Pos.Domain.Tenancy;

namespace Pos.Infrastructure.Printing;

/// <summary>
/// Builds a raw ESC/POS byte stream from a persisted ReceiptModel + the register's PrinterProfile.
/// Header logo (client's) + QR are rastered/native per the flags; line items/VAT/totals/tenders are
/// fixed-width text; cut, drawer-kick and QR all HONOUR the capability flags. Deterministic given the
/// same model → reprints are byte-identical.
/// </summary>
public sealed class EscPosBuilder : IEscPosBuilder
{
    private static readonly Encoding Cp = Encoding.Latin1; // ESC/POS-friendly single-byte

    // Command constants (exposed for tests).
    public static readonly byte[] Init = { 0x1B, 0x40 };          // ESC @
    public static readonly byte[] AlignLeft = { 0x1B, 0x61, 0x00 };
    public static readonly byte[] AlignCenter = { 0x1B, 0x61, 0x01 };
    public static readonly byte[] BoldOn = { 0x1B, 0x45, 0x01 };
    public static readonly byte[] BoldOff = { 0x1B, 0x45, 0x00 };
    public static readonly byte[] Cut = { 0x1D, 0x56, 0x00 };     // GS V 0 (full cut)
    public static readonly byte[] DrawerKick = { 0x1B, 0x70, 0x00, 0x19, 0xFA }; // ESC p 0

    public byte[] Build(ReceiptModel m, PrinterProfile profile, byte[]? clientLogo = null, byte[]? footerMark = null)
    {
        using var s = new MemoryStream();
        Write(s, Init);
        return BuildBody(s, m, profile, clientLogo, footerMark);
    }

    /// <summary>Wrap pre-formatted fixed-width text (X/Z report) as ESC/POS: init, text, feed, cut.</summary>
    public byte[] BuildText(string body, PrinterProfile profile)
    {
        using var s = new MemoryStream();
        Write(s, Init);
        Write(s, AlignLeft);
        foreach (var line in (body ?? "").Replace("\r\n", "\n").Split('\n')) Line(s, line);
        Feed(s, profile.HasCutter ? 3 : 4);
        if (profile.HasCutter) Write(s, Cut);
        return s.ToArray();
    }

    private byte[] BuildBody(MemoryStream s, ReceiptModel m, PrinterProfile profile, byte[]? clientLogo, byte[]? footerMark)
    {
        var cols = profile.Columns;

        // ── Header: client logo (raster) OR bold legal name, then centered text lines ──
        Write(s, AlignCenter);
        if (clientLogo is { Length: > 0 })
        {
            Write(s, MonoBitmap.FromImage(clientLogo, profile.DotWidth).ToEscPosRaster());
            Lf(s);
        }
        else
        {
            Write(s, BoldOn); Line(s, m.Header.LegalName); Write(s, BoldOff);
        }
        if (m.Header.BranchName.Length > 0) Line(s, m.Header.BranchName);
        if (m.Header.BranchAddress.Length > 0) Line(s, m.Header.BranchAddress);
        Line(s, $"PIN: {m.Header.KraPin}");
        Line(s, $"VAT: {m.Header.VatNumber}");
        Line(s, $"Tel: {m.Header.Phone}");

        Write(s, AlignLeft);
        Line(s, ReceiptText.Rule('=', cols));

        // ── Document title (credit note) ──
        if (m.DocumentTitle.Length > 0)
        {
            Write(s, AlignCenter);
            Write(s, BoldOn); Line(s, $"*** {m.DocumentTitle} ***"); Write(s, BoldOff);
            if (!string.IsNullOrWhiteSpace(m.AgainstReceiptNo)) Line(s, $"Against receipt: {m.AgainstReceiptNo}");
            Write(s, AlignLeft);
            Line(s, ReceiptText.Rule('-', cols));
        }

        // ── Meta ──
        Line(s, ReceiptText.LeftRight("Receipt No:", m.Meta.ReceiptNo, cols));
        Line(s, $"Date: {m.Meta.DateTimeEat} EAT");
        Line(s, ReceiptText.LeftRight($"Cashier: {m.Meta.Cashier}", $"Till: {m.Meta.Register}", cols));
        Line(s, ReceiptText.Rule('-', cols));

        // ── Items ──
        foreach (var it in m.Items)
        {
            Line(s, ReceiptText.LeftRight(ReceiptText.Truncate(it.Description, cols - 2), it.TaxCode, cols));
            Line(s, ReceiptText.LeftRight("  " + it.QtyLine, ReceiptText.Money(it.LineTotal), cols));
        }
        Line(s, ReceiptText.Rule('-', cols));

        // ── VAT summary ──
        Line(s, "VAT SUMMARY");
        foreach (var v in m.Vat)
        {
            Line(s, $" {v.TaxCode}  {v.ClassLabel}");
            Line(s, ReceiptText.LeftRight($"   Net {ReceiptText.Money(v.Taxable)}", $"VAT {ReceiptText.Money(v.Vat)}", cols));
        }
        Line(s, ReceiptText.Rule('-', cols));

        // ── Totals ──
        Line(s, ReceiptText.LeftRight("SUBTOTAL (excl VAT)", ReceiptText.Money(m.Totals.Subtotal), cols));
        Line(s, ReceiptText.LeftRight("TOTAL VAT", ReceiptText.Money(m.Totals.TotalVat), cols));
        Write(s, BoldOn);
        Line(s, ReceiptText.LeftRight("GRAND TOTAL", $"{m.Currency} {ReceiptText.Money(m.Totals.GrandTotal)}", cols));
        Write(s, BoldOff);
        Line(s, ReceiptText.Rule('-', cols));

        // ── Tenders ──
        foreach (var t in m.Tenders)
        {
            Line(s, ReceiptText.LeftRight(t.Type, ReceiptText.Money(t.Amount), cols));
            if (!string.IsNullOrWhiteSpace(t.Reference)) Line(s, $"  Ref: {t.Reference}");
        }
        if (m.Change > 0m) Line(s, ReceiptText.LeftRight("CHANGE", ReceiptText.Money(m.Change), cols));
        Line(s, ReceiptText.Rule('-', cols));

        if (m.Legend.Count > 0)
            Line(s, "Tax: " + string.Join("  ", m.Legend.Select(l => $"{l.Code}={l.Label}")));

        // ── Fiscal block + QR ──
        RenderFiscal(s, m, profile);

        Line(s, ReceiptText.Rule('=', cols));

        // ── Footer + optional vendor mark ──
        Write(s, AlignCenter);
        Line(s, "Thank you / Asante sana");
        if (!string.IsNullOrWhiteSpace(m.Footer))
            foreach (var l in ReceiptText.WrapCentered(m.Footer, cols)) Line(s, l);
        if (m.ShowPoweredBy)
        {
            if (footerMark is { Length: > 0 })
            {
                Write(s, MonoBitmap.FromImage(footerMark, profile.DotWidth / 2).ToEscPosRaster());
                Lf(s);
            }
            Line(s, "Powered by Corebalt POS");
        }
        Write(s, AlignLeft);

        // ── Feed + cut (only with a cutter) ──
        Feed(s, profile.HasCutter ? 3 : 4);
        if (profile.HasCutter) Write(s, Cut);

        // ── Cash-drawer kick: only with a drawer AND a cash tender ──
        if (profile.HasCashDrawer && m.Tenders.Any(t => t.Type.Contains("Cash", StringComparison.OrdinalIgnoreCase)))
            Write(s, DrawerKick);

        return s.ToArray();
    }

    private static void RenderFiscal(MemoryStream s, ReceiptModel m, PrinterProfile profile)
    {
        var cols = profile.Columns;
        var f = m.Fiscal;
        if (f.Fiscalized)
        {
            Write(s, AlignCenter); Line(s, "eTIMS FISCAL RECEIPT"); Write(s, AlignLeft);
            Line(s, ReceiptText.LeftRight("CU INV:", f.Cuin ?? "", cols));
            if (!string.IsNullOrWhiteSpace(f.OriginalCuin)) Line(s, ReceiptText.LeftRight("Orig CU INV:", f.OriginalCuin, cols));
            if (!string.IsNullOrWhiteSpace(f.SignedAtEat)) Line(s, $"Signed: {f.SignedAtEat} EAT");
            if (!string.IsNullOrWhiteSpace(f.SyncedAtEat)) Line(s, $"Synced: {f.SyncedAtEat} EAT");
            if (!string.IsNullOrWhiteSpace(f.QrData))
            {
                Write(s, AlignCenter);
                if (profile.NativeQrSupported) Write(s, NativeQr(f.QrData!));
                else Write(s, MonoBitmap.FromQr(f.QrData!, profile.DotWidth).ToEscPosRaster());
                Lf(s);
                Write(s, AlignLeft);
            }
        }
        else if (f.Status == "NotRequired")
        {
            Write(s, AlignCenter); Line(s, "*** NON-FISCAL / TRAINING ***"); Write(s, AlignLeft);
        }
        else
        {
            Write(s, AlignCenter); Line(s, f.StatusText); Write(s, AlignLeft);
        }
    }

    /// <summary>Native ESC/POS QR (GS ( k): set model, module size, error correction, store, print.</summary>
    public static byte[] NativeQr(string data)
    {
        var bytes = Cp.GetBytes(data);
        var store = new byte[bytes.Length + 3];
        var len = bytes.Length + 3;
        using var s = new MemoryStream();
        Write(s, new byte[] { 0x1D, 0x28, 0x6B, 0x04, 0x00, 0x31, 0x41, 0x32, 0x00 }); // model 2
        Write(s, new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, 0x06 });       // module size 6
        Write(s, new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, 0x31 });       // EC level M
        Write(s, new byte[] { 0x1D, 0x28, 0x6B, (byte)(len & 0xFF), (byte)(len >> 8), 0x31, 0x50, 0x30 }); // store
        Write(s, bytes);
        Write(s, new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30 });       // print
        return s.ToArray();
    }

    private static void Write(MemoryStream s, byte[] b) => s.Write(b, 0, b.Length);
    private static void Lf(MemoryStream s) => s.WriteByte(0x0A);
    private static void Line(MemoryStream s, string text) { var b = Cp.GetBytes(text); s.Write(b, 0, b.Length); s.WriteByte(0x0A); }
    private static void Feed(MemoryStream s, int lines) { for (var i = 0; i < lines; i++) s.WriteByte(0x0A); }
}
