using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Pos.Application.Printing;
using Pos.Domain.Tenancy;
using Pos.SharedKernel.Ids;

namespace Pos.Infrastructure.Printing;

/// <summary>Dev/test default: doesn't touch hardware, just logs how many bytes would print.</summary>
public sealed class NullPrinter : IReceiptPrinter
{
    private readonly ILogger<NullPrinter> _log;
    public NullPrinter(ILogger<NullPrinter> log) => _log = log;

    public Task PrintAsync(byte[] escpos, PrinterProfile profile, CancellationToken ct = default)
    {
        _log.LogInformation("NullPrinter: would print {Bytes} ESC/POS bytes ({Paper}).", escpos.Length, profile.PaperWidth);
        return Task.CompletedTask;
    }
}

/// <summary>Writes the raw bytes to a .escpos file — send to a real printer later (Windows: copy /b).</summary>
public sealed class EscPosFilePrinter : IReceiptPrinter
{
    public async Task PrintAsync(byte[] escpos, PrinterProfile profile, CancellationToken ct = default)
    {
        var target = profile.FilePath;
        if (string.IsNullOrWhiteSpace(target)) target = Path.Combine(Path.GetTempPath(), "corebalt-receipts");
        if (Directory.Exists(target) || target.EndsWith(Path.DirectorySeparatorChar) || !Path.HasExtension(target))
        {
            Directory.CreateDirectory(target);
            target = Path.Combine(target, $"receipt-{Uuid7.NewGuid():N}.escpos");
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        }
        await File.WriteAllBytesAsync(target, escpos, ct);
    }
}

/// <summary>Streams the bytes to a network thermal printer over raw TCP (default port 9100).</summary>
public sealed class EscPosNetworkPrinter : IReceiptPrinter
{
    public async Task PrintAsync(byte[] escpos, PrinterProfile profile, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(profile.NetworkHost))
            throw new InvalidOperationException("Network printer profile has no host.");

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(profile.NetworkHost, profile.NetworkPort, ct);
        await using var stream = tcp.GetStream();
        await stream.WriteAsync(escpos, ct);
        await stream.FlushAsync(ct);
    }
}

/// <summary>Selects the implementation from the profile's transport. The default IReceiptPrinter.</summary>
public sealed class ReceiptPrinterRouter : IReceiptPrinter
{
    private readonly NullPrinter _null;
    private readonly EscPosFilePrinter _file;
    private readonly EscPosNetworkPrinter _network;

    public ReceiptPrinterRouter(NullPrinter nullPrinter, EscPosFilePrinter file, EscPosNetworkPrinter network)
    {
        _null = nullPrinter;
        _file = file;
        _network = network;
    }

    public Task PrintAsync(byte[] escpos, PrinterProfile profile, CancellationToken ct = default) =>
        profile.Transport switch
        {
            PrinterTransport.Network => _network.PrintAsync(escpos, profile, ct),
            PrinterTransport.File => _file.PrintAsync(escpos, profile, ct),
            _ => _null.PrintAsync(escpos, profile, ct),
        };
}
