using Pos.SharedKernel;
using Pos.SharedKernel.Ids;

namespace Pos.Domain.Tenancy;

public enum PrinterTransport { Null = 0, File = 1, Network = 2 } // USB later
public enum PaperWidth { Mm80 = 0, Mm58 = 1 }

/// <summary>
/// Per-register (per-lane) thermal printer config for a tenant. Nothing hardcoded — transport,
/// paper width and hardware capabilities are read from here. Defaults to Null transport / 80mm so a
/// fresh lane is safe (logs only) until configured.
/// </summary>
public sealed class PrinterProfile : Entity, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public Guid RegisterId { get; private set; }
    public PrinterTransport Transport { get; private set; }
    public string? NetworkHost { get; private set; }
    public int NetworkPort { get; private set; } = 9100;
    public string? FilePath { get; private set; }
    public PaperWidth PaperWidth { get; private set; }
    public bool HasCutter { get; private set; }
    public bool HasCashDrawer { get; private set; }
    public bool NativeQrSupported { get; private set; }

    /// <summary>Printable width in dots: 576 (80mm) / 384 (58mm).</summary>
    public int DotWidth => PaperWidth == PaperWidth.Mm58 ? 384 : 576;
    /// <summary>Monospace columns for the text body: 32 (58mm) / 48 (80mm).</summary>
    public int Columns => PaperWidth == PaperWidth.Mm58 ? 32 : 48;

    private PrinterProfile() { } // EF

    public static PrinterProfile Create(Guid tenantId, Guid registerId) => new()
    {
        Id = Uuid7.NewGuid(),
        TenantId = tenantId,
        RegisterId = registerId,
        Transport = PrinterTransport.Null,
        NetworkPort = 9100,
        PaperWidth = PaperWidth.Mm80,
    };

    public void Configure(PrinterTransport transport, string? networkHost, int networkPort, string? filePath,
        PaperWidth paperWidth, bool hasCutter, bool hasCashDrawer, bool nativeQrSupported)
    {
        Transport = transport;
        NetworkHost = string.IsNullOrWhiteSpace(networkHost) ? null : networkHost.Trim();
        NetworkPort = networkPort is > 0 and <= 65535 ? networkPort : 9100;
        FilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath.Trim();
        PaperWidth = paperWidth;
        HasCutter = hasCutter;
        HasCashDrawer = hasCashDrawer;
        NativeQrSupported = nativeQrSupported;
    }

    /// <summary>The safe default used when a register has no saved profile (dev/first-run).</summary>
    public static PrinterProfile Default(Guid tenantId, Guid registerId) => Create(tenantId, registerId);
}
