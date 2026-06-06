using System.Net;
using System.Text;

namespace Pos.Application.Receipts;

/// <summary>
/// Renders the same <see cref="ReceiptModel"/> as a simple, self-contained HTML fragment for the
/// till to display on screen. Pure function of the model (deterministic); all text is HTML-encoded.
/// </summary>
public static class ReceiptHtmlRenderer
{
    public static string Render(ReceiptModel m)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"pos-receipt\" style=\"font-family:'Courier New',monospace;font-size:12px;max-width:380px;line-height:1.35\">");

        // Header
        sb.Append("<div style=\"text-align:center\">");
        sb.Append("<div>[LOGO]</div>");
        sb.Append(Strong(m.Header.LegalName));
        Append(sb, m.Header.BranchName);
        Append(sb, m.Header.BranchAddress);
        Append(sb, $"PIN: {m.Header.KraPin}");
        Append(sb, $"VAT: {m.Header.VatNumber}");
        Append(sb, $"Tel: {m.Header.Phone}");
        sb.Append("</div>").Append(Hr);

        // Meta
        sb.Append("<div><strong>Receipt No: ").Append(E(m.Meta.ReceiptNo)).Append("</strong></div>");
        Append(sb, $"Date: {m.Meta.DateTimeEat} EAT");
        Append(sb, $"Cashier: {m.Meta.Cashier}  Till: {m.Meta.Register}");
        sb.Append("<div style=\"color:#888;font-size:10px\">Ref: ").Append(E(m.Meta.Ref)).Append("</div>");
        sb.Append(Hr);

        // Items
        sb.Append("<table style=\"width:100%;border-collapse:collapse\">");
        foreach (var it in m.Items)
        {
            sb.Append("<tr><td>").Append(E($"{it.Description} [{it.TaxCode}]")).Append("</td>")
              .Append("<td style=\"text-align:right\">").Append(E(ReceiptFormat.Money(it.LineTotal))).Append("</td></tr>");
            sb.Append("<tr><td colspan=\"2\" style=\"color:#666;padding-left:1em\">").Append(E(it.QtyLine)).Append("</td></tr>");
        }
        sb.Append("</table>").Append(Hr);

        // VAT breakdown
        sb.Append(Strong("VAT Summary"));
        sb.Append("<table style=\"width:100%\">");
        foreach (var v in m.Vat)
            sb.Append("<tr><td>").Append(E($"{v.TaxCode} {v.ClassLabel}")).Append("</td>")
              .Append("<td style=\"text-align:right\">").Append(E($"Net {ReceiptFormat.Money(v.Taxable)}  VAT {ReceiptFormat.Money(v.Vat)}")).Append("</td></tr>");
        sb.Append("</table>").Append(Hr);

        // Totals
        sb.Append("<table style=\"width:100%\">");
        sb.Append(Row("Subtotal (excl VAT)", ReceiptFormat.Money(m.Totals.Subtotal)));
        sb.Append(Row("Total VAT", ReceiptFormat.Money(m.Totals.TotalVat)));
        sb.Append("<tr><td><strong>GRAND TOTAL</strong></td><td style=\"text-align:right\"><strong>")
          .Append(E($"{m.Currency} {ReceiptFormat.Money(m.Totals.GrandTotal)}")).Append("</strong></td></tr>");
        sb.Append("</table>").Append(Hr);

        // Tenders
        sb.Append("<table style=\"width:100%\">");
        foreach (var t in m.Tenders)
        {
            sb.Append(Row(t.Type, ReceiptFormat.Money(t.Amount)));
            if (!string.IsNullOrWhiteSpace(t.Reference))
                sb.Append("<tr><td colspan=\"2\" style=\"color:#666\">").Append(E($"Ref: {t.Reference}")).Append("</td></tr>");
        }
        if (m.Change > 0m) sb.Append(Row("Change", ReceiptFormat.Money(m.Change)));
        sb.Append("</table>").Append(Hr);

        if (!string.IsNullOrWhiteSpace(m.BuyerPin)) Append(sb, $"Buyer PIN: {m.BuyerPin}");
        if (m.Legend.Count > 0) Append(sb, "Tax: " + string.Join("  ", m.Legend.Select(l => $"{l.Code}={l.Label}")));
        sb.Append(Hr);

        RenderFiscal(sb, m.Fiscal);

        sb.Append(Hr).Append("<div style=\"text-align:center\">Thank you / Asante sana</div>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private static void RenderFiscal(StringBuilder sb, ReceiptFiscal f)
    {
        sb.Append("<div style=\"text-align:center\">");
        if (f.Fiscalized)
        {
            sb.Append(Strong("eTIMS FISCAL RECEIPT"));
            Append(sb, $"CU INV: {f.Cuin}");
            if (!string.IsNullOrWhiteSpace(f.SignedAtEat)) Append(sb, $"Signed: {f.SignedAtEat} EAT");
            if (!string.IsNullOrWhiteSpace(f.SyncedAtEat)) Append(sb, $"Synced: {f.SyncedAtEat} EAT");
            if (!string.IsNullOrWhiteSpace(f.QrData))
            {
                // Render the QR target from QrData (a KRA verification URL). A scannable QR raster
                // component drops in later; the link is the on-screen preview of what it encodes.
                sb.Append("<div>Scan to verify:</div>");
                sb.Append("<div><a href=\"").Append(E(f.QrData)).Append("\">").Append(E(f.QrData)).Append("</a></div>");
            }
        }
        else if (f.Status == "NotRequired")
        {
            sb.Append(Strong("NON-FISCAL / TRAINING"));
        }
        else
        {
            sb.Append(Strong(f.StatusText)); // e.g. "eTIMS: NOT FISCALIZED"
        }
        sb.Append("</div>");
    }

    private const string Hr = "<hr style=\"border:none;border-top:1px dashed #999;margin:4px 0\">";
    private static string E(string s) => WebUtility.HtmlEncode(s);
    private static void Append(StringBuilder sb, string text) => sb.Append("<div>").Append(E(text)).Append("</div>");
    private static string Strong(string text) => "<div><strong>" + E(text) + "</strong></div>";
    private static string Row(string label, string value) =>
        "<tr><td>" + E(label) + "</td><td style=\"text-align:right\">" + E(value) + "</td></tr>";
}
