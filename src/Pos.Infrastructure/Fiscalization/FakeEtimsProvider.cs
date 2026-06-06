using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Pos.Application.Abstractions;
using Pos.Application.Fiscalization;

namespace Pos.Infrastructure.Fiscalization;

/// <summary>
/// Training-mode stand-in for the real KRA eTIMS provider. NOT real fiscalization. SignAsync mints a
/// clearly-fake, deterministic CUIN ("TEST-" + receipt number), a fake signature, and a QR payload
/// shaped like a KRA verification URL carrying the CUIN. SyncAsync is a logged no-op success. The
/// real VSCU/OSCU client drops in behind <see cref="IFiscalizationProvider"/> once credentials exist.
/// </summary>
public sealed class FakeEtimsProvider : IFiscalizationProvider
{
    private const string QrBase = "https://etims-sbx.kra.go.ke/common/link/etims/receipt/indexEtimsReceiptData";

    private readonly ILogger<FakeEtimsProvider> _log;
    private readonly IClock _clock;

    public FakeEtimsProvider(ILogger<FakeEtimsProvider> log, IClock clock)
    {
        _log = log;
        _clock = clock;
    }

    public Task<FiscalizationResult> SignAsync(FiscalInvoice invoice, CancellationToken ct = default)
    {
        var cuin = "TEST-" + invoice.ReceiptNumber;                       // clearly fake, deterministic
        var signature = "TESTSIG-" + Hash16($"{invoice.ReceiptNumber}|{invoice.GrandTotal}|{invoice.SellerPin}");
        var qrData = $"{QrBase}?Data={Uri.EscapeDataString(cuin)}";       // KRA-shaped verification URL with the CUIN
        _log.LogInformation("FakeEtims SIGN receipt={Receipt} -> CUIN={Cuin} (TRAINING, not real)", invoice.ReceiptNumber, cuin);
        return Task.FromResult(FiscalizationResult.Ok(cuin, signature, qrData, _clock.UtcNow));
    }

    public Task<FiscalizationResult> SyncAsync(FiscalInvoice invoice, CancellationToken ct = default)
    {
        // Seam for the real KRA batch upload — here a logged no-op that always "transmits" successfully.
        _log.LogInformation("FakeEtims SYNC (no-op) receipt={Receipt} (TRAINING, not real)", invoice.ReceiptNumber);
        return Task.FromResult(FiscalizationResult.Ok("TEST-" + invoice.ReceiptNumber, string.Empty, string.Empty, _clock.UtcNow));
    }

    public Task<FiscalizationResult> SignCreditNoteAsync(FiscalCreditNote note, CancellationToken ct = default)
    {
        var cuin = "TEST-CN-" + note.ReturnNumber;                        // clearly fake, deterministic
        var signature = "TESTSIG-" + Hash16($"{note.ReturnNumber}|{note.GrandTotal}|{note.OriginalCuin}");
        var qrData = $"{QrBase}?Data={Uri.EscapeDataString(cuin)}";
        _log.LogInformation("FakeEtims SIGN credit-note={Return} -> CUIN={Cuin} (orig {Orig}) (TRAINING, not real)",
            note.ReturnNumber, cuin, note.OriginalCuin);
        return Task.FromResult(FiscalizationResult.Ok(cuin, signature, qrData, _clock.UtcNow));
    }

    private static string Hash16(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..16];
}
