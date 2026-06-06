using Pos.Application.Receipts;
using Pos.Domain.Tenancy;

namespace Pos.Application.Printing;

/// <summary>Per-register printer config lookup (tenant-scoped).</summary>
public interface IPrinterProfileRepository
{
    Task<PrinterProfile?> GetByRegisterAsync(Guid tenantId, Guid registerId, CancellationToken ct = default);
    Task AddAsync(PrinterProfile profile, CancellationToken ct = default);
}

/// <summary>Sends a raw ESC/POS byte stream to a printer per its profile (transport/connection).</summary>
public interface IReceiptPrinter
{
    Task PrintAsync(byte[] escpos, PrinterProfile profile, CancellationToken ct = default);
}

/// <summary>
/// Converts a persisted <see cref="ReceiptModel"/> + the register's <see cref="PrinterProfile"/> into a
/// raw ESC/POS byte stream. <paramref name="clientLogo"/> (the merchant's logo, any format) is rastered
/// into the header when present; <paramref name="footerMark"/> is the Corebalt mark for the optional
/// "Powered by Corebalt POS" footer. Hardware actions honour the profile's capability flags.
/// </summary>
public interface IEscPosBuilder
{
    byte[] Build(ReceiptModel model, PrinterProfile profile, byte[]? clientLogo = null, byte[]? footerMark = null);
}

/// <summary>Renders the SAME ReceiptModel to a PNG that mirrors the paper at the profile's dot width
/// (client logo + real QR) — the printer-free verification path.</summary>
public interface IReceiptPreviewRenderer
{
    byte[] RenderPng(ReceiptModel model, PrinterProfile profile, byte[]? clientLogo = null);
}

/// <summary>Vendor (Corebalt) brand assets — the mono mark used ONLY for the "Powered by Corebalt POS"
/// footer, loaded once from wwwroot. Null when the asset is absent (footer degrades to text).</summary>
public sealed class BrandAssets
{
    public byte[]? PoweredByMark { get; init; }
}
