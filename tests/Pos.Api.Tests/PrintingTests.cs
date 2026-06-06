using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Pos.Application.Receipts;
using Pos.Domain.Tenancy;
using Pos.Infrastructure.Printing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace Pos.Api.Tests;

/// <summary>
/// Thermal printing pipeline — fully verifiable without a printer. ESC/POS framing honours the
/// per-register capability flags; logos/QR rasterize; printers write to file/socket; preview renders.
/// </summary>
public sealed class PrintingTests
{
    private static readonly byte[] Init = { 0x1B, 0x40 };
    private static readonly byte[] Align = { 0x1B, 0x61 };
    private static readonly byte[] NativeQr = { 0x1D, 0x28, 0x6B };
    private static readonly byte[] Raster = { 0x1D, 0x76, 0x30 };
    private static readonly byte[] Cut = { 0x1D, 0x56 };
    private static readonly byte[] Drawer = { 0x1B, 0x70 };

    [Fact]
    public void Builder_emits_init_alignment_native_qr_cut_and_drawer_for_a_fully_capable_cash_sale()
    {
        var bytes = new EscPosBuilder().Build(Sample(), Profile(cutter: true, drawer: true, nativeQr: true));

        bytes.Should().StartWith(Init);              // ESC @
        Has(bytes, Align).Should().BeTrue();         // ESC a
        Has(bytes, NativeQr).Should().BeTrue();      // GS ( k native QR
        Has(bytes, Cut).Should().BeTrue();           // GS V cut
        Has(bytes, Drawer).Should().BeTrue();        // ESC p drawer kick (cash)
    }

    [Fact]
    public void Capability_flags_are_respected()
    {
        // No cutter → no cut.
        Has(new EscPosBuilder().Build(Sample(), Profile(cutter: false, drawer: true, nativeQr: true)), Cut).Should().BeFalse();

        // No drawer → no kick (even on cash).
        Has(new EscPosBuilder().Build(Sample(), Profile(cutter: true, drawer: false, nativeQr: true)), Drawer).Should().BeFalse();

        // Drawer present but a non-cash (M-Pesa) tender → no kick.
        Has(new EscPosBuilder().Build(Sample(cash: false), Profile(cutter: true, drawer: true, nativeQr: true)), Drawer).Should().BeFalse();

        // No native QR → rasterized QR image instead of the GS ( k block.
        var raster = new EscPosBuilder().Build(Sample(), Profile(cutter: true, drawer: true, nativeQr: false));
        Has(raster, NativeQr).Should().BeFalse();
        Has(raster, Raster).Should().BeTrue();
    }

    [Theory]
    [InlineData(576)]
    [InlineData(384)]
    public void A_client_logo_rasterizes_to_a_valid_one_bit_raster(int dotWidth)
    {
        var logo = SampleLogoPng(240, 80);
        var bmp = MonoBitmap.FromImage(logo, dotWidth);
        bmp.Width.Should().BeLessThanOrEqualTo(dotWidth);
        var raster = bmp.ToEscPosRaster();
        raster.Should().StartWith(Raster);                 // GS v 0
        raster.Length.Should().BeGreaterThan(8);            // header + data
    }

    [Fact]
    public void Text_receipt_never_prints_a_logo_placeholder()
    {
        var text = ReceiptTextRenderer.Render(Sample(), 48);
        text.Should().NotContain("[LOGO]");
        text.Should().Contain("Test Retailer Ltd"); // the real header is still there
    }

    [Fact]
    public void No_logo_omits_cleanly_and_prints_the_text_header()
    {
        var bytes = new EscPosBuilder().Build(Sample(), Profile(cutter: true, drawer: true, nativeQr: true), clientLogo: null);
        Has(bytes, Raster).Should().BeFalse("no logo and a native QR → no raster image at all");
        bytes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task File_printer_writes_a_non_empty_escpos_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"corebalt-{Guid.NewGuid():N}.escpos");
        var profile = Profile(cutter: true, drawer: true, nativeQr: true);
        profile.Configure(PrinterTransport.File, null, 9100, path, PaperWidth.Mm80, true, true, true);
        var bytes = new EscPosBuilder().Build(Sample(), profile);

        await new EscPosFilePrinter().PrintAsync(bytes, profile);

        File.Exists(path).Should().BeTrue();
        new FileInfo(path).Length.Should().Be(bytes.Length);
        File.Delete(path);
    }

    [Fact]
    public async Task Network_printer_writes_the_full_buffer_to_the_socket()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var bytes = new EscPosBuilder().Build(Sample(), Profile(cutter: true, drawer: true, nativeQr: true));
        var profile = Profile(cutter: true, drawer: true, nativeQr: true);
        profile.Configure(PrinterTransport.Network, "127.0.0.1", port, null, PaperWidth.Mm80, true, true, true);

        var received = new byte[bytes.Length];
        var accept = Task.Run(async () =>
        {
            using var server = await listener.AcceptTcpClientAsync();
            await using var stream = server.GetStream();
            var read = 0;
            while (read < received.Length)
            {
                var n = await stream.ReadAsync(received.AsMemory(read));
                if (n == 0) break;
                read += n;
            }
            return read;
        });

        await new EscPosNetworkPrinter().PrintAsync(bytes, profile);
        var total = await accept;
        listener.Stop();

        total.Should().Be(bytes.Length);
        received.Should().Equal(bytes);
    }

    [Fact]
    public void Preview_renders_a_png_for_a_sale_and_a_credit_note()
    {
        var renderer = new ReceiptPreviewRenderer();
        var profile = Profile(cutter: true, drawer: true, nativeQr: true);

        var sale = renderer.RenderPng(Sample(), profile, SampleLogoPng(240, 80));
        sale.Should().NotBeEmpty();
        IsPng(sale).Should().BeTrue();

        var creditNote = renderer.RenderPng(Sample(creditNote: true), profile);
        creditNote.Should().NotBeEmpty();
        IsPng(creditNote).Should().BeTrue();
    }

    // ── helpers ──
    private static PrinterProfile Profile(bool cutter, bool drawer, bool nativeQr, PaperWidth paper = PaperWidth.Mm80)
    {
        var p = PrinterProfile.Create(Guid.NewGuid(), Guid.NewGuid());
        p.Configure(PrinterTransport.Null, null, 9100, null, paper, cutter, drawer, nativeQr);
        return p;
    }

    private static ReceiptModel Sample(bool cash = true, bool creditNote = false)
    {
        var header = new ReceiptHeader("Test Retailer Ltd", "Main Branch", "Nairobi", "P051234567X", "VAT0012345", "+254700000000");
        var meta = new ReceiptMeta("MB-000001", Guid.NewGuid().ToString(), "2026-06-07 10:00:00", "Jane (C1)", "REG1", "Main Branch");
        var items = new List<ReceiptItem> { new("Cooking Oil 1L", "1 @ 232.00", creditNote ? -232m : 232m, "A") };
        var vat = new List<ReceiptVatLine> { new("A", "16%", 200m, 32m) };
        var totals = new ReceiptTotals(200m, 32m, creditNote ? -232m : 232m);
        var tenders = new List<ReceiptTender>
        {
            cash ? new("Cash", creditNote ? -232m : 300m, null) : new("Mpesa", 232m, "SGR123"),
        };
        var fiscal = new ReceiptFiscal("Signed", true, "TEST-MB-000001",
            "https://etims-sbx.kra.go.ke/common/link/etims/receipt/indexEtimsReceiptData?Data=TEST-MB-000001",
            "2026-06-07 10:00:00", null, "eTIMS CU INV: TEST-MB-000001", creditNote ? "TEST-MB-000000" : null);
        var legend = new List<ReceiptLegend> { new("A", "16%") };

        return new ReceiptModel(header, meta, items, vat, totals, tenders, Change: cash && !creditNote ? 68m : 0m,
            BuyerPin: null, fiscal, legend, "KES",
            DocumentTitle: creditNote ? "CREDIT NOTE / REFUND" : "", AgainstReceiptNo: creditNote ? "MB-000001" : null,
            Footer: "Returnable within 7 days", ShowPoweredBy: true);
    }

    private static byte[] SampleLogoPng(int w, int h)
    {
        using var img = new Image<Rgba32>(w, h);
        img.Mutate(x => x.BackgroundColor(Color.DarkBlue));
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static bool IsPng(byte[] b) => b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;

    private static bool Has(byte[] hay, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= hay.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < needle.Length; j++)
                if (hay[i + j] != needle[j]) { ok = false; break; }
            if (ok) return true;
        }
        return false;
    }
}
