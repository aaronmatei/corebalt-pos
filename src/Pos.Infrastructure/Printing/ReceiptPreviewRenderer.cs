using Pos.Application.Printing;
using Pos.Application.Receipts;
using Pos.Domain.Tenancy;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Pos.Infrastructure.Printing;

/// <summary>
/// Renders the SAME ReceiptModel to a PNG that mirrors the paper at the profile's dot width — client
/// logo + a real rendered QR — so you can verify output without a printer. Built from the deterministic
/// fixed-width text + the same MonoBitmap rasterizer the ESC/POS path uses: what you see is what prints.
/// </summary>
public sealed class ReceiptPreviewRenderer : IReceiptPreviewRenderer
{
    private static readonly Lazy<FontFamily> Family = new(ResolveMonospace);

    public byte[] RenderPng(ReceiptModel m, PrinterProfile profile, byte[]? clientLogo = null)
    {
        var width = profile.DotWidth;
        var cols = profile.Columns;
        var fontSize = Math.Max(12f, width / (float)cols / 0.62f); // fit `cols` monospace chars across the paper
        var font = Family.Value.CreateFont(fontSize, FontStyle.Regular);
        var lineH = (int)Math.Ceiling(fontSize * 1.35f);
        const int margin = 8;
        const int pad = 8;

        // ── Lay out elements (text lines + images) from the rendered text, splicing the logo + QR ──
        var elements = new List<object>(); // string = text line; MonoBitmap = image

        // The CLIENT's logo (if any) renders at the top, centered, at the print width — never a placeholder.
        if (clientLogo is { Length: > 0 }) elements.Add(MonoBitmap.FromImage(clientLogo, width / 2));

        var lines = ReceiptTextRenderer.Render(m, cols).Split('\n');
        var qr = m.Fiscal.QrData;
        var qrSkip = string.IsNullOrEmpty(qr) ? 0 : (qr!.Length + cols - 1) / cols;

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "Scan to verify (QR):" && !string.IsNullOrEmpty(qr))
            {
                elements.Add(lines[i]);
                elements.Add(MonoBitmap.FromQr(qr!, width * 6 / 10));
                i += qrSkip; // skip the wrapped URL text lines (the image replaces them)
                continue;
            }
            elements.Add(lines[i]);
        }

        var height = pad * 2 + elements.Sum(e => e is MonoBitmap b ? b.Height + pad : lineH);

        using var canvas = new Image<Rgba32>(width, height, Color.White);
        var y = pad;
        foreach (var el in elements)
        {
            if (el is MonoBitmap bmp)
            {
                var ox = Math.Max(0, (width - bmp.Width) / 2);
                for (var by = 0; by < bmp.Height; by++)
                    for (var bx = 0; bx < bmp.Width; bx++)
                        if (bmp.IsBlack(bx, by) && ox + bx < width && y + by < height)
                            canvas[ox + bx, y + by] = Color.Black;
                y += bmp.Height + pad;
            }
            else
            {
                var text = (string)el;
                if (text.Length > 0)
                    canvas.Mutate(ctx => ctx.DrawText(text, font, Color.Black, new PointF(margin, y)));
                y += lineH;
            }
        }

        using var ms = new MemoryStream();
        canvas.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static FontFamily ResolveMonospace()
    {
        foreach (var name in new[] { "Consolas", "Courier New", "DejaVu Sans Mono", "Liberation Mono", "Menlo", "Cascadia Mono" })
            if (SystemFonts.TryGet(name, out var f)) return f;
        return SystemFonts.Families.First(); // any installed font (dev/host always has one)
    }
}
